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

    [Header("Wall Collision")]
    public bool wallCollisionEnabled = true;
    public float playerRadius = 0.25f;
    public LayerMask wallMask = ~0;

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

        // Capture starting yaw/pitch from the current camera rotation.
        Vector3 e = _camera.transform.eulerAngles;
        _pitch = NormalizeAngle(e.x);
        _yaw   = NormalizeAngle(e.y);

        Debug.Log("[DesktopTestRig] Active — WASD move | RMB look | LMB block | E grab | M menu | R restart.");
    }

    private void Update()
    {
        if (!_enabled || _camera == null) return;

        HandleMouseLook();
        HandleMovement();
        HandleHotkeys();
        HandleHandSimulation();
    }

    // ── Activation ─────────────────────────────────────────────────────────
    private bool ShouldEnable()
    {
        if (forceDesktopMode) return true;
        if (Application.isMobilePlatform && disableOnAndroid) return false;

        // If an HMD is connected and active, defer to XR.
        var displays = new System.Collections.Generic.List<UnityEngine.XR.XRDisplaySubsystem>();
        UnityEngine.XR.SubsystemManager.GetSubsystems(displays);
        foreach (var d in displays) if (d != null && d.running) return false;

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

        float speed = moveSpeed * (kb.leftShiftKey.isPressed ? sprintMultiplier : 1f);

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

        if (Physics.CapsuleCast(bottom, top, playerRadius, desired.normalized, out RaycastHit hit,
                                desired.magnitude + 0.02f, wallMask, QueryTriggerInteraction.Ignore))
        {
            Vector3 n = hit.normal; n.y = 0f;
            if (n.sqrMagnitude < 1e-4f) return Vector3.zero;
            n.Normalize();
            float into = Vector3.Dot(desired, -n);
            if (into > 0f) desired += n * into;
            if (Physics.CapsuleCast(bottom, top, playerRadius, desired.normalized, out _,
                                    desired.magnitude + 0.02f, wallMask, QueryTriggerInteraction.Ignore))
                return Vector3.zero;
        }
        return desired;
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

        // LMB → block (existing reflex slap)
        if (mouse.leftButton.wasPressedThisFrame)
            SpawnVirtualHand();

        if (_virtualHand != null && Time.time >= _handHideAt)
        {
            Destroy(_virtualHand);
            _virtualHand = null;
        }

        // E key → deliberate grab (one-shot save). Held = persistent grab hand.
        if (kb != null)
        {
            if (kb.eKey.wasPressedThisFrame)
                StartGrabHand();
            else if (_grabHand != null)
            {
                if (kb.eKey.isPressed) UpdateGrabHandPosition();
                else                   StopGrabHand();
            }
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
        _grabHand.transform.position = ComputeHandPosition(reachDistance);
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

    private void UpdateGrabHandPosition()
    {
        if (_grabHand == null) return;
        // Follow the camera reach point so the held baby tracks the player's view.
        _grabHand.transform.position = ComputeHandPosition(reachDistance);
    }

    private void StopGrabHand()
    {
        if (_grabHand == null) return;
        if (_grabHandComponent != null) _grabHandComponent.Release();
        Destroy(_grabHand);
        _grabHand = null;
        _grabHandComponent = null;
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
