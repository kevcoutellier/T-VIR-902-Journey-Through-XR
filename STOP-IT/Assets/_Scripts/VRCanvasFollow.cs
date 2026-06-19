using UnityEngine;

/// <summary>
/// STOP IT! — VRCanvasFollow
/// Head-locks a world-space canvas a fixed distance in front of the VR camera so the
/// HUD / end-screens stay readable in the headset (ScreenSpaceOverlay canvases do not
/// render to an HMD). Runs LAST (high execution order) so it overrides any per-frame
/// billboard (e.g. <see cref="ScenarioUI"/>) that would otherwise fight it for the
/// transform.
///
/// Added at runtime by <see cref="VRUIWorldSpace"/> — only in VR. Desktop never gets it.
/// </summary>
[DefaultExecutionOrder(1000)]
public class VRCanvasFollow : MonoBehaviour
{
    public Transform cam;
    [Tooltip("Distance (m) in front of the camera.")]
    public float distance = 1.8f;
    [Tooltip("World-up offset (m) from the camera-forward point. Negative drops it below the line of sight.")]
    public float verticalOffset = 0f;

    private void LateUpdate()
    {
        if (cam == null) return;
        transform.position = cam.position + cam.forward * distance + Vector3.up * verticalOffset;
        transform.rotation = cam.rotation; // canvas +Z points away from the camera → readable
    }
}
