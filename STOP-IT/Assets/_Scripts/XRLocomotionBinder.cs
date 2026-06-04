using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using Unity.XR.CoreUtils;

/// <summary>
/// STOP IT! — XRLocomotionBinder
/// Binds XR input actions to TrackedPoseDriver and handles left-joystick locomotion.
/// Movement is handled directly via XROrigin.transform to bypass ContinuousMoveProvider
/// binding issues with XRI 3.4 reflection API.
///
/// Controls:
/// - Head tracking   : HMD position + rotation → Camera TrackedPoseDriver
/// - Left joystick   : Move player (XRI Left Locomotion > Move)
/// - Right joystick  : Disabled (VR headset handles view rotation)
/// - 4 triggers held : Grab child / take cat / close window / pickup-drop bottle.
///                     Squeeze BOTH index triggers + BOTH grips together; release any to drop.
/// - A / X buttons   : Unused for grab (the grab no longer uses face buttons).
/// - Y (left Touch)  : Toggle scenario menu
/// - Controllers     : Tracked for hand presence + interaction
/// </summary>
[DefaultExecutionOrder(-100)]
public class XRLocomotionBinder : MonoBehaviour
{
    [Header("Input Actions Asset")]
    [Tooltip("Assign 'XRI Default Input Actions' from Samples/XR Interaction Toolkit/3.4.0/Starter Assets")]
    public InputActionAsset actionAsset;

    [Header("Movement Settings")]
    [Tooltip("Walking speed in m/s")]
    public float moveSpeed = 3f;

    [Header("Wall Collision")]
    [Tooltip("If true, the player cannot move through walls. Uses a CapsuleCast around the camera.")]
    public bool wallCollisionEnabled = true;

    [Tooltip("Radius of the player capsule used for wall checks (m)")]
    public float playerRadius = 0.25f;

    [Tooltip("Player height used for wall checks (m)")]
    public float playerHeight = 1.7f;

    [Tooltip("Layers considered as walls / static obstacles")]
    public LayerMask wallMask = ~0;

    [Header("VR Grab")]
    [Tooltip("Sphere radius (m) around each hand used to detect the child NPC. Bigger = more forgiving (don't have to hug the kid). ChildGrabber default is 0.30; we bump it for VR because pushing a tracked hand right onto a moving toddler is finicky.")]
    [Range(0.1f, 1.0f)] public float vrGrabRadius = 0.50f;

    // Internal state
    private XROrigin  _xrOrigin;
    private Transform _cameraTransform;
    private InputAction _moveAction;
    private InputAction _menuToggleAction; // Y button or Menu button
    // The 4 "triggers" that must be held TOGETHER to grab: index trigger + grip on
    // BOTH hands. Holding all 4 grabs the baby (or takes the cat / closes the window
    // / picks up the bottle); releasing any one drops it. Replaces the old A/X grab.
    private InputAction _leftTriggerAction;
    private InputAction _leftGripAction;
    private InputAction _rightTriggerAction;
    private InputAction _rightGripAction;
    private bool _fourGrabActive;
    private ChildGrabber _activeGrabber;
    private ChildGrabber _leftGrabber;
    private ChildGrabber _rightGrabber;

    // ─────────────────────────────────────────────────────────────
    void Awake()
    {
        _xrOrigin = GetComponent<XROrigin>();

        // ── Auto-find InputActionAsset ────────────────────────────
        if (actionAsset == null)
        {
            var iam = GetComponent<InputActionManager>();
            if (iam != null && iam.actionAssets?.Count > 0)
                actionAsset = iam.actionAssets[0];
        }
        if (actionAsset == null)
        {
            foreach (var asset in Resources.FindObjectsOfTypeAll<InputActionAsset>())
            {
                if (asset.FindActionMap("XRI Head") != null)
                {
                    actionAsset = asset;
                    break;
                }
            }
        }
        if (actionAsset == null)
        {
            Debug.LogError("[XRLocomotionBinder] No InputActionAsset found with 'XRI Head' map!");
            return;
        }

        // Enable all action maps
        foreach (var map in actionAsset.actionMaps)
            map.Enable();

        // ── Force floor-level tracking ────────────────────────────
        if (_xrOrigin != null)
            _xrOrigin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;

        // ── Head tracking (TrackedPoseDriver on Main Camera) ──────
        _cameraTransform = _xrOrigin?.Camera?.transform;
        var mainCam = _xrOrigin?.Camera;
        if (mainCam != null)
        {
            var tpd = mainCam.GetComponent<TrackedPoseDriver>();
            if (tpd == null) tpd = mainCam.gameObject.AddComponent<TrackedPoseDriver>();

            var headMap = actionAsset.FindActionMap("XRI Head");
            if (headMap != null)
            {
                tpd.positionAction = headMap.FindAction("Position");
                tpd.rotationAction = headMap.FindAction("Rotation");
                tpd.trackingType   = TrackedPoseDriver.TrackingType.RotationAndPosition;
                tpd.updateType     = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
            }
        }

        // ── Controller tracking ───────────────────────────────────
        BindControllerTPD("Left Controller",  "XRI Left");
        BindControllerTPD("Right Controller", "XRI Right");

        // ── Movement action (left joystick) ───────────────────────
        // Try "XRI Left Locomotion > Move" first, fallback to "XRI Left > Thumbstick"
        _moveAction = actionAsset.FindActionMap("XRI Left Locomotion")?.FindAction("Move")
                   ?? actionAsset.FindActionMap("XRI Left")?.FindAction("Thumbstick")
                   ?? actionAsset.FindActionMap("XRI Left")?.FindAction("Primary2DAxis");

        if (_moveAction != null)
        {
            _moveAction.Enable();
            Debug.Log($"[XRLocomotionBinder] Move action bound: '{_moveAction.actionMap.name} > {_moveAction.name}'");
        }
        else
        {
            Debug.LogWarning("[XRLocomotionBinder] No move action found — joystick movement disabled.");
        }

        // Disable ContinuousMoveProvider to avoid double-movement
        var moveProvider = GetComponentInChildren<ContinuousMoveProvider>();
        if (moveProvider != null)
        {
            moveProvider.enabled = false;
            Debug.Log("[XRLocomotionBinder] ContinuousMoveProvider disabled (movement handled directly).");
        }

        // ── Menu toggle action (Y button on left controller) ──────
        _menuToggleAction = actionAsset.FindActionMap("XRI Left")?.FindAction("SecondaryButton") // Y button
                         ?? actionAsset.FindActionMap("XRI Left")?.FindAction("MenuButton");
        if (_menuToggleAction != null)
        {
            _menuToggleAction.Enable();
            _menuToggleAction.performed += OnMenuToggle;
            Debug.Log($"[XRLocomotionBinder] Menu toggle action bound: '{_menuToggleAction.actionMap.name} > {_menuToggleAction.name}'");
        }

        // ── Grab → hold ALL 4 triggers together (index trigger + grip, both hands) ──
        // Per design: the baby (and the cat / window / bottle verbs) are grabbed by
        // squeezing all four triggers at once, and dropped when you let go. The old
        // A / X face buttons are intentionally NOT bound to grab anymore.
        // Bound directly to controller paths (rig-agnostic). We POLL the 4 in Update()
        // rather than using per-action callbacks because "all held" is a conjunction
        // across 4 controls, not a single button event.
        ResolveGrabbers();
        _leftTriggerAction  = BuildHoldAction("LeftHand",  isGrip: false);
        _leftGripAction     = BuildHoldAction("LeftHand",  isGrip: true);
        _rightTriggerAction = BuildHoldAction("RightHand", isGrip: false);
        _rightGripAction    = BuildHoldAction("RightHand", isGrip: true);
        _leftTriggerAction.Enable();
        _leftGripAction.Enable();
        _rightTriggerAction.Enable();
        _rightGripAction.Enable();

        Debug.Log("[XRLocomotionBinder] Initialised successfully.");
    }

    void OnDestroy()
    {
        if (_menuToggleAction != null)
            _menuToggleAction.performed -= OnMenuToggle;
        DisposeAction(ref _leftTriggerAction);
        DisposeAction(ref _leftGripAction);
        DisposeAction(ref _rightTriggerAction);
        DisposeAction(ref _rightGripAction);
    }

    private static void DisposeAction(ref InputAction action)
    {
        if (action == null) return;
        action.Disable();
        action.Dispose();
        action = null;
    }

    // ─────────────────────────────────────────────────────────────
    // Grip → grab
    // ─────────────────────────────────────────────────────────────
    private void ResolveGrabbers()
    {
        if (_xrOrigin == null) return;
        // The 2 grabbers live on the hand objects, alongside PlayerBlocker.
        var grabbers = _xrOrigin.GetComponentsInChildren<ChildGrabber>(true);
        foreach (var g in grabbers)
        {
            // Heuristic: walk up the tree, the closest "Left" / "Right" name wins.
            string path = GetHierarchyPath(g.transform).ToLowerInvariant();
            if (path.Contains("left"))       _leftGrabber  = g;
            else if (path.Contains("right")) _rightGrabber = g;
            else if (_leftGrabber == null)   _leftGrabber  = g; // fallback
            else                             _rightGrabber = g;
        }

        // Fallback: the main scene only ships PlayerBlocker on each hand, no
        // ChildGrabber. Without this, the 4-trigger grab in VR would fire into a
        // null grabber and "nothing happens". Auto-attach a grabber to each
        // controller transform on first run.
        if (_leftGrabber == null)  _leftGrabber  = EnsureGrabberOnController("Left Controller");
        if (_rightGrabber == null) _rightGrabber = EnsureGrabberOnController("Right Controller");

        Debug.Log($"[XRLocomotionBinder] Grabbers — left: {(_leftGrabber != null ? _leftGrabber.gameObject.name : "<missing>")}, right: {(_rightGrabber != null ? _rightGrabber.gameObject.name : "<missing>")}");
    }

    /// <summary>
    /// Find the controller transform under the XR Origin and add a ChildGrabber
    /// to it if one is missing. Returns the resulting grabber (or null if the
    /// controller transform itself can't be found).
    /// </summary>
    private ChildGrabber EnsureGrabberOnController(string controllerName)
    {
        if (_xrOrigin == null) return null;

        var cameraOffset = _xrOrigin.CameraFloorOffsetObject;
        Transform ctrl = cameraOffset != null
            ? cameraOffset.transform.Find(controllerName)
            : null;
        if (ctrl == null) ctrl = transform.Find(controllerName);

        // Last-ditch: search anywhere in the rig by name match.
        if (ctrl == null)
        {
            foreach (var t in _xrOrigin.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == controllerName) { ctrl = t; break; }
            }
        }

        if (ctrl == null)
        {
            Debug.LogWarning($"[XRLocomotionBinder] Couldn't find '{controllerName}' under XR Origin — grab disabled for that hand.");
            return null;
        }

        var grabber = ctrl.GetComponent<ChildGrabber>() ?? ctrl.gameObject.AddComponent<ChildGrabber>();
        // VR-only override: enlarge the detection sphere so the player doesn't
        // have to be glued to the toddler. Desktop rig sets its own radius on
        // the temporary hand it spawns, so this doesn't affect that path.
        grabber.grabRadius = vrGrabRadius;
        return grabber;
    }

    private static string GetHierarchyPath(Transform t)
    {
        var sb = new System.Text.StringBuilder(t.name);
        while (t.parent != null) { t = t.parent; sb.Insert(0, t.name + "/"); }
        return sb.ToString();
    }

    /// <summary>
    /// Build a Button InputAction for ONE of the 4 grab triggers — the index
    /// trigger or the grip of the given hand. Several candidate bindings are added
    /// so it resolves on the XRI Default asset whatever the exact control naming;
    /// axis controls (trigger / grip) act as buttons via the default press point.
    /// Unresolved paths are silently ignored by the Input System.
    /// </summary>
    private static InputAction BuildHoldAction(string hand, bool isGrip)
    {
        var action = new InputAction($"{(isGrip ? "Grip" : "Trigger")}_{hand}", InputActionType.Button);
        if (isGrip)
        {
            action.AddBinding($"<XRController>{{{hand}}}/gripPressed");
            action.AddBinding($"<XRController>{{{hand}}}/{{GripButton}}");
            action.AddBinding($"<XRController>{{{hand}}}/grip");        // axis as button
        }
        else
        {
            action.AddBinding($"<XRController>{{{hand}}}/triggerPressed");
            action.AddBinding($"<XRController>{{{hand}}}/triggerButton");
            action.AddBinding($"<XRController>{{{hand}}}/trigger");     // axis as button
        }
        return action;
    }

    /// <summary>
    /// Polls the 4 triggers each frame. ALL held → grab (rising edge); ANY released
    /// → drop (falling edge). The baby attaches to whichever hand is closest to it;
    /// scenario verbs (cat / window / bottle) are camera-based so the chosen hand
    /// doesn't matter for them.
    /// </summary>
    private void UpdateFourTriggerGrab()
    {
        bool all = IsHeld(_leftTriggerAction)  && IsHeld(_leftGripAction)
                && IsHeld(_rightTriggerAction) && IsHeld(_rightGripAction);

        if (all && !_fourGrabActive)
        {
            _fourGrabActive = true;
            _activeGrabber = ChooseGrabber();
            Debug.Log($"[XRLocomotionBinder] 4-trigger grab fired — grabber={_activeGrabber}");
            _activeGrabber?.Trigger();
        }
        else if (!all && _fourGrabActive)
        {
            _fourGrabActive = false;
            // Release whichever hand might be holding the baby (idempotent if none).
            _leftGrabber?.Release();
            _rightGrabber?.Release();
            _activeGrabber = null;
        }
    }

    private static bool IsHeld(InputAction a) => a != null && a.IsPressed();

    /// <summary>Pick the hand closest to the child for the baby grab; fall back to right.</summary>
    private ChildGrabber ChooseGrabber()
    {
        var child = FindAnyObjectByType<ChildNPC>();
        if (child != null && _leftGrabber != null && _rightGrabber != null)
        {
            float dl = (_leftGrabber.transform.position  - child.transform.position).sqrMagnitude;
            float dr = (_rightGrabber.transform.position - child.transform.position).sqrMagnitude;
            return dl <= dr ? _leftGrabber : _rightGrabber;
        }
        return _rightGrabber != null ? _rightGrabber : _leftGrabber;
    }

    // ─────────────────────────────────────────────────────────────
    void Update()
    {
        // Grab is independent of locomotion — poll the 4 triggers before the
        // movement early-out (which bails when no move action is bound).
        UpdateFourTriggerGrab();

        if (_moveAction == null || _xrOrigin == null || _cameraTransform == null) return;

        // Read left thumbstick
        Vector2 stick = _moveAction.ReadValue<Vector2>();
        if (stick.sqrMagnitude < 0.01f) return;

        // Project camera forward/right onto horizontal plane
        Vector3 forward = _cameraTransform.forward;
        Vector3 right   = _cameraTransform.right;
        forward.y = 0f; forward.Normalize();
        right.y   = 0f; right.Normalize();

        float effectiveSpeed = moveSpeed * FloorObstacle.GetSpeedMultiplierAt(_xrOrigin.transform.position);
        Vector3 move = (forward * stick.y + right * stick.x) * effectiveSpeed * Time.deltaTime;
        if (move.sqrMagnitude < 1e-6f) return;

        if (wallCollisionEnabled)
            move = ResolveWallCollision(move);

        _xrOrigin.transform.position += move;
    }

    // ─────────────────────────────────────────────────────────────
    /// <summary>
    /// Slide along walls instead of going through them.
    /// Uses a capsule around the camera (HMD) projected onto the floor.
    /// </summary>
    private Vector3 ResolveWallCollision(Vector3 desiredMove)
    {
        Vector3 camPos = _cameraTransform.position;
        // Capsule from feet to head, centered on the camera horizontally.
        Vector3 bottom = new Vector3(camPos.x, _xrOrigin.transform.position.y + playerRadius, camPos.z);
        Vector3 top    = bottom + Vector3.up * Mathf.Max(0.01f, playerHeight - playerRadius * 2f);

        Vector3 dir = desiredMove.normalized;
        float dist = desiredMove.magnitude;

        if (Physics.CapsuleCast(bottom, top, playerRadius, dir, out RaycastHit hit, dist + 0.02f, wallMask, QueryTriggerInteraction.Ignore))
        {
            // Slide: project the remaining motion along the wall plane.
            Vector3 normal = hit.normal; normal.y = 0f;
            if (normal.sqrMagnitude < 1e-4f) return Vector3.zero;
            normal.Normalize();
            float into = Vector3.Dot(desiredMove, -normal);
            if (into > 0f)
                desiredMove += normal * into;

            // Re-check after sliding to avoid gliding into a corner.
            if (Physics.CapsuleCast(bottom, top, playerRadius, desiredMove.normalized, out _, desiredMove.magnitude + 0.02f, wallMask, QueryTriggerInteraction.Ignore))
                return Vector3.zero;
        }
        return desiredMove;
    }

    // ─────────────────────────────────────────────────────────────
    void OnMenuToggle(InputAction.CallbackContext ctx)
    {
        var menu = FindAnyObjectByType<ScenarioMenu>();
        if (menu != null)
            menu.ToggleMenu();
    }

    // ─────────────────────────────────────────────────────────────
    void BindControllerTPD(string controllerPath, string mapName)
    {
        if (_xrOrigin == null) return;

        var cameraOffset = _xrOrigin.CameraFloorOffsetObject;
        Transform ctrl = cameraOffset != null
            ? cameraOffset.transform.Find(controllerPath)
            : null;
        if (ctrl == null)
            ctrl = transform.Find(controllerPath);
        if (ctrl == null) return;

        var tpd = ctrl.GetComponent<TrackedPoseDriver>();
        if (tpd == null) tpd = ctrl.gameObject.AddComponent<TrackedPoseDriver>();

        var map = actionAsset.FindActionMap(mapName);
        if (map == null) return;

        tpd.positionAction = map.FindAction("Position");
        tpd.rotationAction = map.FindAction("Rotation");
        tpd.trackingType   = TrackedPoseDriver.TrackingType.RotationAndPosition;
        tpd.updateType     = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
    }
}
