using UnityEngine;
using UnityEngine.InputSystem;
using Unity.XR.CoreUtils;

/// <summary>
/// STOP IT! — DesktopTestRig
/// Lightweight desktop-only camera/controller for fast iteration without an HMD.
/// Drop this on the XR Origin (or any persistent object). It activates ONLY when:
///  • a Quest/OpenXR runtime is NOT detected at runtime, OR
///  • the "Force Desktop Mode" toggle is set in the inspector.
///
/// Controls (Editor / Standalone):
///  • WASD or arrows — move
///  • Mouse          — look around (hold right mouse button OR always — see "Mouse Look Mode")
///  • Space          — jump (desktop-only stand-in for the VR Jump action)
///  • Left click     — "block" hand: brief sphere that touches the child (reflex slap)
///  • E (held)       — "grab" hand that picks the baby up (one-shot save)
///  • R              — restart current scenario
///  • Tab / M        — toggle ScenarioMenu
///  • Esc            — release mouse cursor
///
/// This rig is intentionally orthogonal to XR: when XR is active it disables itself
/// and lets XRLocomotionBinder/HMD handle input. So you can leave it in the scene.
/// </summary>
[DefaultExecutionOrder(-90)]
public class DesktopTestRig : MonoBehaviour
{
    public enum LookMode { Always, RightMouseHeld }

    [Header("Activation")]
    [Tooltip("Force desktop mode even when an XR runtime is detected (useful for editor iteration).")]
    public bool forceDesktopMode = false;

    [Tooltip("If true, also disables this rig when running on Android (Quest builds).")]
    public bool disableOnAndroid = true;

    [Header("Movement")]
    public float moveSpeed = 3.5f;
    public float sprintMultiplier = 1.8f;
    public float lookSensitivity = 1.8f;
    public LookMode mouseLookMode = LookMode.RightMouseHeld;
    public float playerHeight = 1.7f;

    [Header("Jump")]
    [Tooltip("Peak jump height in metres when pressing Space.")]
    public float jumpHeight = 1.0f;
    [Tooltip("Vertical acceleration (m/s²). Stronger than -9.81 for a snappier game feel.")]
    public float gravity = -18f;
    [Tooltip("Distance below the feet sampled to detect the floor.")]
    public float groundProbeDistance = 1.0f;
    [Tooltip("Layers considered as walkable ground (floors, stairs, ledges).")]
    public LayerMask groundMask = ~0;

    [Header("Wall Collision")]
    public bool wallCollisionEnabled = true;
    public float playerRadius = 0.25f;
    public LayerMask wallMask = ~0;
    [Tooltip("Surfaces whose normal.y exceeds this threshold are treated as walkable slopes (not walls). 0.5 ≈ 60° max slope.")]
    public float walkableNormalY = 0.5f;
    [Tooltip("Maximum step height the player can climb without jumping (metres).")]
    public float stepHeight = 0.7f;
    [Tooltip("Speed multiplier while climbing stairs (1.0 = full speed, 0.4 = much slower).")]
    [Range(0.1f, 1f)] public float climbSpeedMultiplier = 0.4f;
    [Tooltip("How long the climb slowdown stays active after the last step-up (seconds). Re-applied on every step.")]
    public float climbSlowDuration = 0.4f;

    [Header("Hand Simulation")]
    [Tooltip("When the user clicks, we project a small sphere at this distance forward to interact with the child.")]
    public float reachDistance = 1.6f;
    public float handRadius = 0.2f;
    [Tooltip("How long the LMB-spawned 'block' hand stays alive after a click (s)")]
    public float handLifetime = 0.25f;
    [Tooltip("Radius of the E-key 'grab' hand (slightly larger than block hand for forgiveness)")]
    public float grabHandRadius = 0.25f;

    // ── Runtime ────────────────────────────────────────────────────────────
    private XROrigin _xrOrigin;
    private Camera _camera;
    private float _yaw;
    private float _pitch;
    private bool _enabled;
    private GameObject _virtualHand;
    private float _handHideAt;
    private GameObject _grabHand;
    private ChildGrabber _grabHandComponent;
    private float _verticalVelocity;
    private bool _grounded = true;
    private float _climbCooldown;

    private void Awake()
    {
        _xrOrigin = GetComponent<XROrigin>();
        if (_xrOrigin == null) _xrOrigin = FindAnyObjectByType<XROrigin>();
    }

    private void Start()
    {
        _enabled = ShouldEnable();
        if (!_enabled) { enabled = false; return; }

        // Resolve camera
        _camera = _xrOrigin != null ? _xrOrigin.Camera : Camera.main;
        if (_camera == null)
        {
            Debug.LogWarning("[DesktopTestRig] No camera available. Disabling rig.");
            enabled = false; return;
        }

        // Give the player a sensible eye height when running outside VR.
        if (_xrOrigin != null && _xrOrigin.CameraFloorOffsetObject != null)
        {
            var off = _xrOrigin.CameraFloorOffsetObject.transform;
            off.localPosition = new Vector3(off.localPosition.x, playerHeight, off.localPosition.z);
        }

        // Disable XRLocomotionBinder so it doesn't fight us.
        var binder = GetComponent<XRLocomotionBinder>();
        if (binder != null) binder.enabled = false;

        // Desktop is the authority for its OWN mode: the XRI controller MODELS (and their
        // controller-mounted ChildGrabber / PlayerBlocker) are VR-only PRESENTATION baked under the
        // XR rig. With no HMD they'd float in front of the desktop camera and could even phantom-grab
        // the baby. Hide the whole controller objects here — DesktopTestRig spawns its own hands.
        HideVrControllers();

        // Capture starting yaw/pitch from the current camera rotation.
        Vector3 e = _camera.transform.eulerAngles;
        _pitch = NormalizeAngle(e.x);
        _yaw   = NormalizeAngle(e.y);

        Debug.Log("[DesktopTestRig] Active — WASD move | Space jump | RMB look | LMB block | E grab | M menu | R restart.");
    }

    private void Update()
    {
        if (!_enabled || _camera == null) return;

        HandleMouseLook();
        HandleMovement();
        HandleJump();
        HandleHotkeys();
        HandleHandSimulation();
    }

    // ── Activation ─────────────────────────────────────────────────────────
    private bool ShouldEnable()
    {
        if (forceDesktopMode) return true;
        if (Application.isMobilePlatform && disableOnAndroid) return false;

        // If an opaque HMD is connected and actively rendering, defer to XR.
        // Note: in the Unity Editor, the XR subsystem initialises even without a physical headset —
        // displayOpaque disambiguates an actual opaque VR device from a dormant subsystem.
        var displays = new System.Collections.Generic.List<UnityEngine.XR.XRDisplaySubsystem>();
        SubsystemManager.GetSubsystems(displays);
        foreach (var d in displays) if (d != null && d.running && d.displayOpaque) return false;

        return true;
    }

    // ── Look ───────────────────────────────────────────────────────────────
    private void HandleMouseLook()
    {
        var mouse = Mouse.current;
        if (mouse == null) return;

        bool active = mouseLookMode == LookMode.Always
                   || mouse.rightButton.isPressed;

        if (!active)
        {
            // Release cursor when not looking
            if (Cursor.lockState != CursorLockMode.None)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            return;
        }

        if (Cursor.lockState != CursorLockMode.Locked)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        Vector2 delta = mouse.delta.ReadValue() * lookSensitivity * 0.1f;
        _yaw   += delta.x;
        _pitch -= delta.y;
        _pitch = Mathf.Clamp(_pitch, -85f, 85f);

        _camera.transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
    }

    // ── Movement ───────────────────────────────────────────────────────────
    private void HandleMovement()
    {
        if (_xrOrigin == null) return;

        var kb = Keyboard.current;
        if (kb == null) return;

        Vector2 input = Vector2.zero;
        if (kb.wKey.isPressed || kb.upArrowKey.isPressed)    input.y += 1f;
        if (kb.sKey.isPressed || kb.downArrowKey.isPressed)  input.y -= 1f;
        if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) input.x += 1f;
        if (kb.aKey.isPressed || kb.leftArrowKey.isPressed)  input.x -= 1f;

        if (input.sqrMagnitude < 0.01f) return;
        input = Vector2.ClampMagnitude(input, 1f);

        float speed = moveSpeed * (kb.leftShiftKey.isPressed ? sprintMultiplier : 1f)
                    * FloorObstacle.GetSpeedMultiplierAt(_xrOrigin.transform.position);

        // Climb slowdown: while a step-up was triggered recently, the player
        // moves slower so the staircase doesn't feel like a teleport.
        if (_climbCooldown > 0f)
        {
            _climbCooldown -= Time.deltaTime;
            speed *= climbSpeedMultiplier;
        }

        Vector3 fwd = _camera.transform.forward; fwd.y = 0f; fwd.Normalize();
        Vector3 right = _camera.transform.right; right.y = 0f; right.Normalize();
        Vector3 move = (fwd * input.y + right * input.x) * speed * Time.deltaTime;

        if (wallCollisionEnabled) move = ResolveWall(move);

        _xrOrigin.transform.position += move;
    }

    private Vector3 ResolveWall(Vector3 desired)
    {
        if (desired.sqrMagnitude < 1e-6f) return desired;
        Vector3 camPos = _camera.transform.position;
        Vector3 bottom = new Vector3(camPos.x, _xrOrigin.transform.position.y + playerRadius, camPos.z);
        Vector3 top    = bottom + Vector3.up * Mathf.Max(0.01f, playerHeight - playerRadius * 2f);
        Vector3 dirN = desired.normalized;

        // Shared cast: skips the toddler so walking up to the baby never slides/steps the rig
        // (the same "ignore the NPC" rule the VR rig uses — one source of truth in LocomotionCasts).
        if (LocomotionCasts.Capsule(bottom, top, playerRadius, dirN, desired.magnitude + 0.02f, wallMask, out RaycastHit hit))
        {
            // 1. Walkable slope (ramp). The ground probe handles the Y lift —
            //    just let the horizontal move through.
            if (hit.normal.y >= walkableNormalY) return desired;

            // 2. Step-up: short vertical obstacle (stair riser). Lift the capsule
            //    by stepHeight; if clear at that height, find the step's top by
            //    probing down at a point that's GUARANTEED past the obstacle's
            //    near face, then place the player on top of the step (Y + nudge
            //    forward) so they land properly on its surface.
            Vector3 raisedBottom = bottom + Vector3.up * stepHeight;
            Vector3 raisedTop = top + Vector3.up * stepHeight;
            if (!LocomotionCasts.Capsule(raisedBottom, raisedTop, playerRadius, dirN,
                                         desired.magnitude + 0.02f, wallMask, out _))
            {
                // Probe origin must be past the obstacle's face. desired.magnitude
                // is one frame of motion (tiny); we need to clear at least the
                // capsule radius + a small epsilon to reach the obstacle's top.
                float probeForward = Mathf.Max(0.15f, desired.magnitude + playerRadius + 0.05f);
                Vector3 probeOrigin = raisedBottom + dirN * probeForward;
                if (LocomotionCasts.Ground(probeOrigin, stepHeight * 1.5f, wallMask, out RaycastHit gh))
                {
                    float currentY = _xrOrigin.transform.position.y;
                    float climb = gh.point.y - currentY;
                    // Only accept genuine UP steps within stepHeight. Skip downsteps
                    // and bogus probes that hit a lower surface (e.g. the floor
                    // before the step).
                    if (climb > 0.01f && climb <= stepHeight)
                    {
                        Vector3 pos = _xrOrigin.transform.position;
                        pos.y = gh.point.y + 0.01f;       // small offset to clear the step edge
                        // Push the player horizontally past the step's near face
                        // so they actually land on the step top, not in mid-air
                        // before it (where gravity would pull them back down).
                        Vector3 nudge = dirN * (playerRadius + 0.05f);
                        pos.x += nudge.x;
                        pos.z += nudge.z;
                        _xrOrigin.transform.position = pos;
                        _climbCooldown = climbSlowDuration; // slow next frames
                        return Vector3.zero; // horizontal move already applied via the nudge
                    }
                }
            }

            // 3. Wall: slide along.
            Vector3 n = hit.normal; n.y = 0f;
            if (n.sqrMagnitude < 1e-4f) return Vector3.zero;
            n.Normalize();
            float into = Vector3.Dot(desired, -n);
            if (into > 0f) desired += n * into;
            if (LocomotionCasts.Capsule(bottom, top, playerRadius, desired.normalized,
                                        desired.magnitude + 0.02f, wallMask, out _))
                return Vector3.zero;
        }
        return desired;
    }

    // ── Jump ───────────────────────────────────────────────────────────────
    private void HandleJump()
    {
        if (_xrOrigin == null) return;

        var kb = Keyboard.current;
        Vector3 pos = _xrOrigin.transform.position;

        // Sample the ground below the feet. The ray starts slightly above to avoid
        // starting inside a step's collider when climbing stairs.
        float groundY = float.NegativeInfinity;
        Vector3 probeOrigin = pos + Vector3.up * 0.1f;
        // Shared cast skips the toddler, so standing near/over the baby never snaps the floor onto its head.
        if (LocomotionCasts.Ground(probeOrigin, groundProbeDistance + 0.1f, groundMask, out RaycastHit hit))
        {
            groundY = hit.point.y;
        }

        // Jump impulse: only while grounded and the player is at (or just above) the floor.
        bool jumpPressed = kb != null && kb.spaceKey.wasPressedThisFrame;
        if (jumpPressed && _grounded)
        {
            _verticalVelocity = Mathf.Sqrt(2f * jumpHeight * -gravity);
            _grounded = false;
        }

        // Integrate gravity.
        _verticalVelocity += gravity * Time.deltaTime;
        pos.y += _verticalVelocity * Time.deltaTime;

        // Land on the detected ground.
        if (!float.IsNegativeInfinity(groundY) && pos.y <= groundY)
        {
            pos.y = groundY;
            _verticalVelocity = 0f;
            _grounded = true;
        }
        else if (float.IsNegativeInfinity(groundY))
        {
            // No ground found below — keep falling but treat as airborne.
            _grounded = false;
        }

        _xrOrigin.transform.position = pos;
    }

    // ── Hotkeys ────────────────────────────────────────────────────────────
    private void HandleHotkeys()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        // Esc — release mouse
        if (kb.escapeKey.wasPressedThisFrame)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        // M / Tab — toggle scenario menu
        if (kb.mKey.wasPressedThisFrame || kb.tabKey.wasPressedThisFrame)
        {
            var menu = FindAnyObjectByType<ScenarioMenu>();
            if (menu != null) menu.ToggleMenu();
        }

        // R — restart current scenario
        if (kb.rKey.wasPressedThisFrame && GameManager.Instance != null)
        {
            var sm = ScenarioManager.Instance ?? FindAnyObjectByType<ScenarioManager>();
            if (sm != null && sm.CurrentIndex >= 0)
            {
                sm.SetNextScenario(sm.CurrentIndex);
                GameManager.Instance.LaunchSingle();
            }
        }
    }

    // ── Hand simulation ────────────────────────────────────────────────────
    private void HandleHandSimulation()
    {
        var mouse = Mouse.current;
        var kb = Keyboard.current;
        if (mouse == null) return;

        // Don't spawn a virtual hand when the click lands on a UI element
        // (otherwise clicking a menu button also produces a phantom slap).
        bool overUI = UnityEngine.EventSystems.EventSystem.current != null
                   && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();

        // LMB → block (existing reflex slap)
        if (!overUI && mouse.leftButton.wasPressedThisFrame)
            SpawnVirtualHand();

        if (_virtualHand != null && Time.time >= _handHideAt)
        {
            Destroy(_virtualHand);
            _virtualHand = null;
        }

        // E key → deliberate grab (one-shot save). Held = persistent grab hand
        // (parented to the camera, so it tracks the head naturally — no per-frame
        // raycast, no jitter).
        if (kb != null)
        {
            if (kb.eKey.wasPressedThisFrame)
                StartGrabHand();
            else if (_grabHand != null && !kb.eKey.isPressed)
                StopGrabHand();
        }
    }

    private void SpawnVirtualHand()
    {
        // Place a transient trigger sphere in front of the camera that will
        // collide with the child (PlayerBlocker handles the rest).
        Vector3 pos = ComputeHandPosition(reachDistance);
        if (_virtualHand != null) Destroy(_virtualHand);

        _virtualHand = new GameObject("DesktopVirtualHand");
        _virtualHand.transform.position = pos;
        var sc = _virtualHand.AddComponent<SphereCollider>();
        sc.isTrigger = true;
        sc.radius = handRadius;
        // Reuse the existing PlayerBlocker logic.
        _virtualHand.AddComponent<PlayerBlocker>();
        // Add a Rigidbody so trigger callbacks fire reliably.
        var rb = _virtualHand.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        _handHideAt = Time.time + handLifetime;
    }

    private void StartGrabHand()
    {
        if (_grabHand != null) return;

        _grabHand = new GameObject("DesktopGrabHand");
        // Parent under the camera so the hand follows head movement smoothly.
        // Re-raycasting every frame (the old behavior) made the hand jitter
        // whenever a different collider was hit forward (a wall, a step, a
        // piece of furniture) — and the held toddler followed the jitter.
        _grabHand.transform.SetParent(_camera.transform, worldPositionStays: false);
        _grabHand.transform.localPosition = new Vector3(0f, -0.2f, reachDistance);
        _grabHand.transform.localRotation = Quaternion.identity;
        var rb = _grabHand.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
        var sc = _grabHand.AddComponent<SphereCollider>();
        sc.isTrigger = true;
        sc.radius = grabHandRadius;
        _grabHandComponent = _grabHand.AddComponent<ChildGrabber>();
        _grabHandComponent.grabRadius = grabHandRadius;

        // One-shot trigger as soon as the hand is in place.
        _grabHandComponent.Trigger();
    }

    private void StopGrabHand()
    {
        if (_grabHand == null) return;
        if (_grabHandComponent != null) _grabHandComponent.Release();
        _grabHand.transform.SetParent(null, worldPositionStays: true);
        Destroy(_grabHand);
        _grabHand = null;
        _grabHandComponent = null;
    }

    /// <summary>Hide the VR controller objects (models + their grabbers) on desktop — VR-only presentation.</summary>
    private void HideVrControllers()
    {
        Transform root = _xrOrigin != null ? _xrOrigin.transform : transform;
        HideByName(root, "Left Controller");
        HideByName(root, "Right Controller");
    }

    private static void HideByName(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) { t.gameObject.SetActive(false); return; }
    }

    private Vector3 ComputeHandPosition(float distance)
    {
        Vector3 origin = _camera.transform.position;
        Vector3 dir = _camera.transform.forward;
        if (Physics.Raycast(origin, dir, out RaycastHit hit, distance, ~0, QueryTriggerInteraction.Collide))
            return hit.point;
        return origin + dir * distance;
    }

    private static float NormalizeAngle(float a)
    {
        a %= 360f;
        if (a > 180f) a -= 360f;
        return a;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        var cam = _camera != null ? _camera : Camera.main;
        if (cam == null) return;
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.6f);
        Gizmos.DrawWireSphere(cam.transform.position + cam.transform.forward * reachDistance, handRadius);
    }
#endif
}
