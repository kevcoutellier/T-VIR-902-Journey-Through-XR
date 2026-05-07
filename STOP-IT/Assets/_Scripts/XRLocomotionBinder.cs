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

    // Internal state
    private XROrigin  _xrOrigin;
    private Transform _cameraTransform;
    private InputAction _moveAction;
    private InputAction _menuToggleAction; // Y button or Menu button
    private InputAction _leftGripAction;
    private InputAction _rightGripAction;
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

        // ── Grip → child grab (one-shot save) ─────────────────────
        ResolveGrabbers();
        _leftGripAction  = ResolveGripAction("XRI Left");
        _rightGripAction = ResolveGripAction("XRI Right");
        if (_leftGripAction != null)
        {
            _leftGripAction.Enable();
            _leftGripAction.performed += OnLeftGrab;
            _leftGripAction.canceled  += OnLeftRelease;
        }
        if (_rightGripAction != null)
        {
            _rightGripAction.Enable();
            _rightGripAction.performed += OnRightGrab;
            _rightGripAction.canceled  += OnRightRelease;
        }

        Debug.Log("[XRLocomotionBinder] Initialised successfully.");
    }

    void OnDestroy()
    {
        if (_menuToggleAction != null)
            _menuToggleAction.performed -= OnMenuToggle;
        if (_leftGripAction != null)
        {
            _leftGripAction.performed -= OnLeftGrab;
            _leftGripAction.canceled  -= OnLeftRelease;
        }
        if (_rightGripAction != null)
        {
            _rightGripAction.performed -= OnRightGrab;
            _rightGripAction.canceled  -= OnRightRelease;
        }
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
    }

    private static string GetHierarchyPath(Transform t)
    {
        var sb = new System.Text.StringBuilder(t.name);
        while (t.parent != null) { t = t.parent; sb.Insert(0, t.name + "/"); }
        return sb.ToString();
    }

    private InputAction ResolveGripAction(string mapName)
    {
        var map = actionAsset.FindActionMap(mapName);
        if (map == null) return null;
        return map.FindAction("Select")
            ?? map.FindAction("SelectAction")
            ?? map.FindAction("Grip")
            ?? map.FindAction("GripPressed");
    }

    private void OnLeftGrab(InputAction.CallbackContext _)    { _leftGrabber?.Trigger(); }
    private void OnLeftRelease(InputAction.CallbackContext _) { _leftGrabber?.Release(); }
    private void OnRightGrab(InputAction.CallbackContext _)    { _rightGrabber?.Trigger(); }
    private void OnRightRelease(InputAction.CallbackContext _) { _rightGrabber?.Release(); }

    // ─────────────────────────────────────────────────────────────
    void Update()
    {
        if (_moveAction == null || _xrOrigin == null || _cameraTransform == null) return;

        // Read left thumbstick
        Vector2 stick = _moveAction.ReadValue<Vector2>();
        if (stick.sqrMagnitude < 0.01f) return;

        // Project camera forward/right onto horizontal plane
        Vector3 forward = _cameraTransform.forward;
        Vector3 right   = _cameraTransform.right;
        forward.y = 0f; forward.Normalize();
        right.y   = 0f; right.Normalize();

        Vector3 move = (forward * stick.y + right * stick.x) * moveSpeed * Time.deltaTime;
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
