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
    private readonly List<VRMenuPointer> _pointers = new(); // one per available controller (or one gaze pointer)
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

        // The VR menu is a SIMPLE static launch menu: the cinematic camera-tour AND the roaming
        // "stroller" babies are a DESKTOP-only attract mode (a moving camera in VR is uncomfortable
        // and was ruled out). Disable every menu stroller so no baby ever wanders the VR menu.
        // (MenuRoamingNPC is independent from the gameplay ChildNPC, so this never touches gameplay.)
        foreach (var roamer in FindObjectsByType<MenuRoamingNPC>(FindObjectsInactive.Include))
            if (roamer != null) roamer.gameObject.SetActive(false);

        // CRITICAL: HousePreview leaves the HMD camera disabled during the menu (the desktop build
        // shows the cinematic MenuCamera instead). In VR the headset MUST render through the XR
        // camera, so force it on now — and LateUpdate keeps it on no matter what the menu does.
        if (!_cam.gameObject.activeSelf) _cam.gameObject.SetActive(true);
        _cam.enabled = true;

        _rightController = ResolveController(xr, "Right Controller");
        _leftController  = ResolveController(xr, "Left Controller");

        // Visible hands so the player can see + aim the Quest controllers.
        AddControllerVisual(_leftController);
        AddControllerVisual(_rightController);

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
            // One laser per available controller so EITHER hand can aim and pull its own trigger.
            // The controller anchor's forward is already the OpenXR aim/pointer pose (no manual tilt).
            // Each pointer lives on its OWN child GameObject: VRMenuPointer builds its LineRenderer on
            // its gameObject, so two on one object would collide. When NO controller resolves
            // (desktop / edge cases) we fall back to a single camera pointer so gaze still works.
            if (_rightController != null)
                CreatePointer("Menu Pointer R", _rightController, VRMenuPointer.ClickHand.Right);
            if (_leftController != null)
                CreatePointer("Menu Pointer L", _leftController, VRMenuPointer.ClickHand.Left);
            if (_pointers.Count == 0)
                CreatePointer("Menu Pointer (Gaze)", _cam.transform, VRMenuPointer.ClickHand.Any);
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
        foreach (var p in _pointers) if (p != null) p.RefreshButtons();
    }

    /// <summary>Spawn a laser pointer on its own child object, aimed from <paramref name="origin"/> and clicked by <paramref name="hand"/>.</summary>
    private void CreatePointer(string name, Transform origin, VRMenuPointer.ClickHand hand)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var pointer = go.AddComponent<VRMenuPointer>();
        pointer.rayOrigin = origin;
        pointer.clickHand = hand;
        pointer.rayAngleDownDeg = 0f;
        pointer.menuCanvas = _menuCanvas;
        pointer.RefreshButtons();
        _pointers.Add(pointer);
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

        // Safety-net: correct the wired model's local transform on-device before anything else, in case
        // the saved scene still carries the old flipped override that rendered the controller upside-down.
        FixWiredModelOrientation(ctrl);

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

    /// <summary>
    /// Runtime safety-net for the on-device controller MODEL orientation. When the real wired model
    /// (the XRI prefab instance — a direct child whose name starts with "XR Controller") is present,
    /// force its local transform back to the authored values. This corrects the upside-down rendering
    /// that a stale scene override could otherwise reintroduce; it never touches the aim/anchor
    /// transform (whose forward is the pointer pose the laser rides on).
    ///   RIGHT hand: Euler(0,180,0), scale (-1,1,1) (X-mirror = right hand), pos (0,0,-0.05)
    ///   LEFT  hand: Euler(0,180,0), scale ( 1,1,1),                         pos (0,0,-0.05)
    /// </summary>
    private static void FixWiredModelOrientation(Transform ctrl)
    {
        if (ctrl == null) return;

        Transform model = null;
        for (int i = 0; i < ctrl.childCount; i++)
        {
            var child = ctrl.GetChild(i);
            if (child.name.StartsWith("XR Controller")) { model = child; break; }
        }
        if (model == null) return; // no wired model → blue-cube fallback handles visibility.

        bool isRight = ctrl.name.Contains("Right");
        bool isLeft  = ctrl.name.Contains("Left");
        if (!isRight && !isLeft) return; // unknown hand → leave the authored transform untouched.

        model.localRotation = Quaternion.Euler(0f, 180f, 0f);
        model.localPosition = new Vector3(0f, 0f, -0.05f);
        model.localScale    = isRight ? new Vector3(-1f, 1f, 1f) : Vector3.one;
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
