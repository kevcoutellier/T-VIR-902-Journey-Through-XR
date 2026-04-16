using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using Unity.XR.CoreUtils;
using System.Reflection;

/// <summary>
/// STOP IT! — XRLocomotionBinder
/// Binds XR input actions to TrackedPoseDriver and ContinuousMoveProvider at runtime.
/// Adapted from StopTheBaby project for XRI 3.4 Default Input Actions.
///
/// Controls:
/// - Head tracking: HMD position + rotation
/// - Left joystick: Movement (ContinuousMoveProvider)
/// - Right joystick: disabled (VR headset handles view)
/// - Controllers: tracked for hand presence + interaction
/// </summary>
[DefaultExecutionOrder(-100)]
public class XRLocomotionBinder : MonoBehaviour
{
    [Header("Input Actions Asset")]
    [Tooltip("Assign 'XRI Default Input Actions' from Samples/XR Interaction Toolkit/3.4.0/Starter Assets")]
    public InputActionAsset actionAsset;

    void Awake()
    {
        // Auto-find the InputActionAsset if not assigned in Inspector
        if (actionAsset == null)
        {
            // Try to find it from InputActionManager on same object
            var iam = GetComponent<InputActionManager>();
            if (iam != null && iam.actionAssets != null && iam.actionAssets.Count > 0)
                actionAsset = iam.actionAssets[0];
        }
        if (actionAsset == null)
        {
            // Search all loaded InputActionAssets
            var allAssets = Resources.FindObjectsOfTypeAll<InputActionAsset>();
            foreach (var asset in allAssets)
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

        var xrOrigin = GetComponent<XROrigin>();

        // Force floor tracking for Meta Quest
        if (xrOrigin != null)
            xrOrigin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;

        // ── Head tracking (TrackedPoseDriver on Main Camera) ──────
        var mainCam = xrOrigin?.Camera;
        if (mainCam != null)
        {
            var tpd = mainCam.GetComponent<TrackedPoseDriver>();
            if (tpd == null) tpd = mainCam.gameObject.AddComponent<TrackedPoseDriver>();

            var headMap = actionAsset.FindActionMap("XRI Head");
            if (headMap != null)
            {
                tpd.positionAction = headMap.FindAction("Position");
                tpd.rotationAction = headMap.FindAction("Rotation");
                tpd.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
                tpd.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
            }
        }

        // ── Controller tracking ───────────────────────────────────
        BindControllerTPD("Left Controller", "XRI Left");
        BindControllerTPD("Right Controller", "XRI Right");

        // ── Movement: left joystick only ──────────────────────────
        var moveProvider = GetComponentInChildren<ContinuousMoveProvider>();
        if (moveProvider != null)
        {
            // The Move action is in "XRI Left Locomotion" map, not "XRI Left"
            var moveAction = actionAsset.FindActionMap("XRI Left Locomotion")?.FindAction("Move");
            if (moveAction != null)
            {
                SetXRInputReader(moveProvider, "m_LeftHandMoveInput", moveAction);
                Debug.Log("[XRLocomotionBinder] Move action bound to left joystick.");
            }
            else
            {
                Debug.LogWarning("[XRLocomotionBinder] 'Move' action not found in 'XRI Left Locomotion' map!");
            }
            if (mainCam != null)
                moveProvider.forwardSource = mainCam.transform;
        }
        else
        {
            Debug.LogWarning("[XRLocomotionBinder] No ContinuousMoveProvider found!");
        }

        Debug.Log("[XRLocomotionBinder] All XR actions bound successfully.");
    }

    void BindControllerTPD(string controllerPath, string mapName)
    {
        var xrOrigin = GetComponent<XROrigin>();
        if (xrOrigin == null) return;

        // Search in Camera Offset children
        var cameraOffset = xrOrigin.CameraFloorOffsetObject;
        if (cameraOffset == null) return;

        var ctrl = cameraOffset.transform.Find(controllerPath);
        if (ctrl == null)
        {
            // Try direct child of XR Origin
            ctrl = transform.Find(controllerPath);
        }
        if (ctrl == null) return;

        var tpd = ctrl.GetComponent<TrackedPoseDriver>();
        if (tpd == null) tpd = ctrl.gameObject.AddComponent<TrackedPoseDriver>();

        var map = actionAsset.FindActionMap(mapName);
        if (map == null) return;

        tpd.positionAction = map.FindAction("Position");
        tpd.rotationAction = map.FindAction("Rotation");
        tpd.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
        tpd.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
    }

    static void SetXRInputReader(Component comp, string fieldName, InputAction action)
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var type = comp.GetType();
        FieldInfo field = null;

        while (field == null && type != null)
        {
            field = type.GetField(fieldName, flags);
            type = type.BaseType;
        }
        if (field == null) return;

        var reader = field.GetValue(comp);
        var readerType = reader.GetType();
        var modeField = readerType.GetField("m_InputSourceMode", flags);
        var actionField = readerType.GetField("m_InputAction", flags);

        // InputSourceMode.InputAction = 3
        modeField?.SetValue(reader, 3);
        actionField?.SetValue(reader, action);

        field.SetValue(comp, reader);
    }
}
