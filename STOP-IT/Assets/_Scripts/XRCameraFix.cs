using System.Reflection;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem.XR;

/// <summary>
/// STOP IT! — XRCameraFix
/// Exécuté très tôt (ordre -1000) pour assigner la caméra au XROrigin
/// avant que GravityProvider.Update() ne plante.
/// Attacher sur le GameManager ou tout objet persistant.
/// </summary>
[DefaultExecutionOrder(-1000)]
public class XRCameraFix : MonoBehaviour
{
    private void Awake()
    {
        var xrOrigin = FindAnyObjectByType<XROrigin>();
        if (xrOrigin == null) return;
        if (xrOrigin.Camera != null) return;   // déjà assigné

        // Chercher la caméra dans les enfants (y compris inactifs)
        var cam = xrOrigin.GetComponentInChildren<Camera>(true);

        // Sinon créer sous Camera Offset
        if (cam == null)
        {
            var offsetT = xrOrigin.CameraFloorOffsetObject != null
                ? xrOrigin.CameraFloorOffsetObject.transform
                : xrOrigin.transform;

            var camGO = new GameObject("Main Camera");
            camGO.transform.SetParent(offsetT, false);
            camGO.tag = "MainCamera";
            cam = camGO.AddComponent<Camera>();
            Debug.Log("[XRCameraFix] Caméra créée sous " + offsetT.name);
        }

        // Assigner via réflexion (m_Camera est private)
        var field = typeof(XROrigin).GetField("m_Camera",
            BindingFlags.NonPublic | BindingFlags.Instance);
        field?.SetValue(xrOrigin, cam);

        // Tag MainCamera pour Camera.main
        if (!cam.CompareTag("MainCamera"))
            cam.gameObject.tag = "MainCamera";

        // AudioListener
        if (cam.GetComponent<AudioListener>() == null)
            cam.gameObject.AddComponent<AudioListener>();

        // TrackedPoseDriver
        if (cam.GetComponent<TrackedPoseDriver>() == null)
            cam.gameObject.AddComponent<TrackedPoseDriver>();

        Debug.Log("[XRCameraFix] XROrigin.Camera assigné → " + cam.gameObject.name);
    }
}
