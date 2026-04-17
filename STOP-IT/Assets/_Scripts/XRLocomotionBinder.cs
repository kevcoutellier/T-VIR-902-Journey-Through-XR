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

    // Internal state
    private XROrigin  _xrOrigin;
    private Transform _cameraTransform;
    private InputAction _moveAction;
    private InputAction _menuToggleAction; // Y button or Menu button

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

        Debug.Log("[XRLocomotionBinder] Initialised successfully.");
    }

    void OnDestroy()
    {
        if (_menuToggleAction != null)
            _menuToggleAction.performed -= OnMenuToggle;
    }

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
        _xrOrigin.transform.position += move;
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
