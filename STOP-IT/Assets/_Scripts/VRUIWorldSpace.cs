using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Unity.XR.CoreUtils;

/// <summary>
/// STOP IT! — VRUIWorldSpace
/// Runtime adapter that makes the game's 2D UI work inside the Quest headset.
/// ScreenSpaceOverlay canvases don't render to an HMD, so in VR (and ONLY in VR) this
/// converts the menu, HUD and end-screens to world-space:
///   • HUD / lose / victory → head-locked in front of the camera (<see cref="VRCanvasFollow"/>).
///   • Main menu            → world-anchored in front of the player on entry, driven by
///                            a controller laser (<see cref="VRMenuPointer"/>).
///
/// It also fixes the camera: HousePreview keeps the HMD camera DISABLED during the menu and
/// renders a separate cinematic 'MenuCamera' (a desktop-only setup). In VR there is no
/// MenuCamera — the headset must render through the XR camera — so this forces the XR camera
/// on (and the cinematic ones off) and keeps them that way.
///
/// On desktop <see cref="InputHints.IsVRActive"/> is false → this disables itself and the
/// original ScreenSpaceOverlay + mouse/keyboard flow is untouched. It self-bootstraps via
/// RuntimeInitializeOnLoadMethod, so no scene wiring is required.
/// </summary>
[DefaultExecutionOrder(50)]
public class VRUIWorldSpace : MonoBehaviour
{
    [Header("Distances / widths (metres)")]
    public float hudDistance = 1.8f, hudWidth = 2.2f;
    public float endDistance = 1.2f, endWidth = 3.0f;
    public float menuDistance = 1.5f, menuWidth = 2.2f;

    private Camera _cam;
    private Transform _rightController;
    private Transform _leftController;
    private Canvas _menuCanvas;
    private VRMenuPointer _pointer;
    private readonly List<Camera> _disabledCams = new();

    private static bool _spawned;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (_spawned) return;
        _spawned = true;
        var go = new GameObject("VR UI Adapter");
        go.AddComponent<VRUIWorldSpace>();
    }

    private void Start() => StartCoroutine(SetupWhenVrReady());

    private IEnumerator SetupWhenVrReady()
    {
        // Standalone (IL2CPP/Quest) brings the XR display up a moment AFTER Start — Link already had
        // it up. Poll instead of checking once, so the camera-force + UI setup still run in the build.
        float t = 0f;
        while (!InputHints.IsVRActive() && t < 6f) { t += Time.unscaledDeltaTime; yield return null; }
        if (!InputHints.IsVRActive()) { enabled = false; yield break; }

        var xr = FindAnyObjectByType<XROrigin>();
        _cam = xr != null ? xr.Camera : Camera.main;
        if (_cam == null)
        {
            Debug.LogError("[VRUIWorldSpace] No camera found — cannot set up VR UI.");
            enabled = false;
            yield break;
        }

        // In VR only the HMD camera should render — disable any leftover menu/tour camera
        // (but never an XR-rig camera, which may be a URP overlay in the stack).
        if (xr != null)
        {
            foreach (var c in FindObjectsByType<Camera>(FindObjectsInactive.Include))
                if (c != _cam && !c.transform.IsChildOf(xr.transform))
                {
                    c.enabled = false;
                    _disabledCams.Add(c);
                    Debug.Log($"[VRUIWorldSpace] Disabled non-XR camera '{c.name}' (VR renders through the HMD camera).");
                }
        }
        foreach (var tour in FindObjectsByType<MenuCameraTour>(FindObjectsInactive.Include))
            tour.enabled = false;

        // CRITICAL: HousePreview leaves the HMD camera disabled during the menu (the desktop build
        // shows the cinematic MenuCamera instead). In VR the headset MUST render through the XR
        // camera, so force it on now — and LateUpdate keeps it on no matter what the menu does.
        if (!_cam.gameObject.activeSelf) _cam.gameObject.SetActive(true);
        _cam.enabled = true;

        _leftController  = ResolveController(xr, "Left Controller");
        _rightController = ResolveController(xr, "Right Controller")
                        ?? _leftController
                        ?? _cam.transform;

        // Visible hands so the player can see + aim the Quest controllers.
        AddControllerVisual(ResolveController(xr, "Left Controller"));
        AddControllerVisual(ResolveController(xr, "Right Controller"));

        // HUD + end screens → head-locked.
        var hud = FindAnyObjectByType<ScenarioUI>(FindObjectsInactive.Include);
        if (hud) ConvertHeadLocked(hud, hudDistance, hudWidth);
        var lose = FindAnyObjectByType<StoryLoseScreen>(FindObjectsInactive.Include);
        if (lose) ConvertHeadLocked(lose, endDistance, endWidth);
        var end = FindAnyObjectByType<StoryEndScreen>(FindObjectsInactive.Include);
        if (end) ConvertHeadLocked(end, endDistance, endWidth);

        // Menu → world-anchored + laser pointer.
        var menu = FindAnyObjectByType<MenuScenarioShowcase>(FindObjectsInactive.Include);
        if (menu)
        {
            _menuCanvas = GetCanvas(menu);
            ConvertWorldSpace(_menuCanvas, menuWidth);
            _pointer = gameObject.AddComponent<VRMenuPointer>();
            // Controller laser via OpenXR AIM pose (pointer space) — correct pointing direction
            // on Quest 3. Gaze (camera) kept as fallback when no controller data is available.
            _pointer.gazeRayFallback        = _cam.transform;
            _pointer.rightControllerForLine = _rightController != _cam.transform ? _rightController : null;
            _pointer.leftControllerForLine  = _leftController;
            _pointer.menuCanvas = _menuCanvas;
            _pointer.RefreshButtons();
        }

        if (GameManager.Instance != null)
            GameManager.Instance.OnStateChanged.AddListener(OnStateChanged);

        StartCoroutine(InitialMenuPlacement());

        Debug.Log("[VRUIWorldSpace] VR UI active — XR camera forced on; menu/HUD/end-screens world-space.");
    }

    private void LateUpdate()
    {
        // In VR the HMD camera must always render and the desktop cinematic cameras must not,
        // regardless of what the menu/state logic toggles. Cheap per-frame assertion.
        if (_cam != null && !_cam.enabled) _cam.enabled = true;
        for (int i = 0; i < _disabledCams.Count; i++)
            if (_disabledCams[i] != null && _disabledCams[i].enabled) _disabledCams[i].enabled = false;
    }

    /// <summary>Anchor the menu once the TrackedPoseDriver has applied the real HMD pose (a few frames in).</summary>
    private IEnumerator InitialMenuPlacement()
    {
        yield return null;
        yield return null;
        yield return new WaitForSeconds(0.3f);
        if (GameManager.Instance == null || GameManager.Instance.State == GameManager.GameState.Menu)
            PositionMenuInFront();
    }

    private void OnDestroy()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnStateChanged.RemoveListener(OnStateChanged);
    }

    private void OnStateChanged(GameManager.GameState state)
    {
        if (state == GameManager.GameState.Menu) PositionMenuInFront();
    }

    /// <summary>Anchor the menu upright in front of the player's current gaze (then leave it fixed so the laser can aim).</summary>
    private void PositionMenuInFront()
    {
        if (_menuCanvas == null || _cam == null) return;
        Vector3 fwd = _cam.transform.forward; fwd.y = 0f;
        if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
        fwd.Normalize();

        Vector3 pos = _cam.transform.position + fwd * menuDistance;
        pos.y = _cam.transform.position.y;
        _menuCanvas.transform.position = pos;
        _menuCanvas.transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);
        if (_pointer != null) _pointer.RefreshButtons();
    }

    // ── conversion helpers ────────────────────────────────────────────────────
    private void ConvertHeadLocked(Component ui, float distance, float widthMeters)
    {
        var canvas = GetCanvas(ui);
        if (canvas == null) return;
        ConvertWorldSpace(canvas, widthMeters);
        var follow = canvas.gameObject.GetComponent<VRCanvasFollow>()
                  ?? canvas.gameObject.AddComponent<VRCanvasFollow>();
        follow.cam = _cam.transform;
        follow.distance = distance;
    }

    private void ConvertWorldSpace(Canvas canvas, float widthMeters)
    {
        if (canvas == null) return;
        var rt = (RectTransform)canvas.transform;
        Vector2 size = rt.rect.size;

        var scaler = canvas.GetComponent<CanvasScaler>();
        if (scaler != null && scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize)
        {
            size = scaler.referenceResolution;
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;
        }
        if (size.x < 1f || size.y < 1f) size = new Vector2(1920f, 1080f);

        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = _cam;
        rt.sizeDelta = size;
        float s = widthMeters / size.x;
        rt.localScale = new Vector3(s, s, s);
    }

    private static Canvas GetCanvas(Component c)
        => c.GetComponent<Canvas>() ?? c.GetComponentInParent<Canvas>() ?? c.GetComponentInChildren<Canvas>();

    private static void AddControllerVisual(Transform ctrl)
    {
        if (ctrl == null) return;

        // If a scene-placed model (from editor tool "Wire Controller Models") is already here, keep it.
        // Only fall back to the blue-cube placeholder when nothing exists.
        if (ctrl.GetComponentInChildren<MeshRenderer>() != null) return;

        // Blue-cube fallback so the controller is at least visible without the editor tool.
        if (ctrl.Find("VR Controller Visual") != null) return;
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "VR Controller Visual";
        var col = go.GetComponent<Collider>(); if (col) Destroy(col);
        go.transform.SetParent(ctrl, false);
        go.transform.localPosition = new Vector3(0f, 0f, 0.03f);
        go.transform.localScale = new Vector3(0.05f, 0.05f, 0.14f);
        var mr = go.GetComponent<MeshRenderer>();
        var sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
        if (sh != null)
        {
            var tint = new Color(0.25f, 0.9f, 1f);
            var m = new Material(sh); m.color = tint; m.SetColor("_BaseColor", tint);
            mr.sharedMaterial = m;
        }
    }

    private static Transform ResolveController(XROrigin xr, string controllerName)
    {
        if (xr == null) return null;
        var offset = xr.CameraFloorOffsetObject;
        Transform ctrl = offset != null ? offset.transform.Find(controllerName) : null;
        if (ctrl == null) ctrl = xr.transform.Find(controllerName);
        if (ctrl == null)
            foreach (var t in xr.GetComponentsInChildren<Transform>(true))
                if (t.name == controllerName) { ctrl = t; break; }
        return ctrl;
    }
}
