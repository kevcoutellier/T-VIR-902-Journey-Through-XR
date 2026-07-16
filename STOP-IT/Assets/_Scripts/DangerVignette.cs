using System.Collections;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

/// <summary>
/// STOP IT! — DangerVignette
/// Full-screen red overlay whose alpha tracks the child's proximity to the hazard.
/// Creates its own Canvas + Image on the fly so no scene wiring is needed.
///
/// Rendering depends on the display:
///   • Desktop → a Screen-Space-Overlay canvas (unchanged; the DesktopTestRig path).
///   • VR      → a head-locked WORLD-SPACE panel parented to the HMD camera. A
///               ScreenSpaceOverlay canvas does NOT render to an HMD, so the desktop
///               overlay is invisible in the headset. The panel sits just in front of
///               the eyes, oversized to fill the field of view, and fades the SAME red
///               alpha with the SAME proximity logic.
/// The choice is made once at setup: the desktop overlay is built immediately, and if
/// the XR display comes up (it can lag scene load on Quest standalone) it is rebuilt
/// as the world-space panel.
/// </summary>
public class DangerVignette : MonoBehaviour
{
    [Header("Tracking")]
    public HazardZone hazard;

    [Header("Vignette")]
    public Color color = new Color(1f, 0.05f, 0.05f, 1f);
    [Range(0f, 1f)] public float maxAlpha = 0.45f;
    public float fadeSpeed = 4f;
    [Tooltip("When child is outside this multiple of warningRadius, vignette is fully transparent.")]
    public float farMultiplier = 2f;

    [Header("VR")]
    [Tooltip("Distance (m) the head-locked VR panel sits in front of the eyes. Kept small so nothing renders in front of it and it fills the FOV. Angular coverage stays constant if you change this.")]
    [SerializeField] private float vrDistance = 0.4f;

    private ChildNPC _child;
    private Canvas _canvas;
    private Image _image;
    private float _currentAlpha;

    private void Awake()
    {
        // Build the desktop Screen-Space-Overlay immediately — unchanged, instant, harmless in VR
        // (an overlay canvas simply does not render to the HMD).
        EnsureOverlayCanvas();
        // On Quest standalone the XR display comes up a moment AFTER scene load, so a single
        // check here would miss it. Poll briefly; if VR is (or becomes) active, rebuild the
        // vignette as a head-locked world-space panel that the headset can actually see.
        StartCoroutine(UpgradeToVrIfActive());
    }

    private IEnumerator UpgradeToVrIfActive()
    {
        float t = 0f;
        while (t < 6f)
        {
            if (InputHints.IsVRActive()) { BuildVrVignette(); yield break; }
            t += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    private void OnEnable()
    {
        if (hazard == null) hazard = FindAnyObjectByType<HazardZone>();
        if (ScenarioManager.Instance != null)
            ScenarioManager.Instance.OnScenarioActivated.AddListener(OnScenarioActivated);
    }

    private void OnDisable()
    {
        if (ScenarioManager.Instance != null)
            ScenarioManager.Instance.OnScenarioActivated.RemoveListener(OnScenarioActivated);
    }

    private void OnScenarioActivated(ScenarioManager.ScenarioConfig cfg)
    {
        if (cfg != null && cfg.hazardZone != null)
            hazard = cfg.hazardZone;
    }

    private void Update()
    {
        if (_child == null) _child = FindAnyObjectByType<ChildNPC>();

        float target = 0f;
        if (_child != null && hazard != null)
        {
            float warn = hazard.warningRadius * farMultiplier;
            float d = Vector3.Distance(_child.transform.position, hazard.transform.position);
            float raw = 1f - Mathf.Clamp01(d / Mathf.Max(0.01f, warn));
            // bias the curve so the red only really shows when the child is close
            target = Mathf.Pow(raw, 1.8f) * maxAlpha;
        }

        _currentAlpha = Mathf.MoveTowards(_currentAlpha, target, fadeSpeed * Time.deltaTime);

        if (_image != null)
        {
            var c = color;
            c.a = _currentAlpha;
            _image.color = c;
        }
    }

    public void Flash(float alpha, float duration = 0.3f)
    {
        _currentAlpha = Mathf.Max(_currentAlpha, alpha);
        // It will decay naturally via the next Update frames.
    }

    private void EnsureOverlayCanvas()
    {
        var canvasGO = new GameObject("DangerVignetteCanvas");
        canvasGO.transform.SetParent(transform, false);
        _canvas = canvasGO.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 9999;

        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        // No GraphicRaycaster — this overlay must never intercept input.

        var imgGO = new GameObject("Vignette");
        imgGO.transform.SetParent(canvasGO.transform, false);
        _image = imgGO.AddComponent<Image>();
        _image.color = new Color(color.r, color.g, color.b, 0f);
        _image.raycastTarget = false;
        var rt = (RectTransform)imgGO.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    /// <summary>
    /// Rebuild the vignette as a head-locked world-space panel the HMD can render. Parented
    /// directly to the camera transform (rigid, zero-latency — a lagging comfort vignette is
    /// nauseating) and oversized so it fills the field of view.
    /// </summary>
    private void BuildVrVignette()
    {
        var xr = FindAnyObjectByType<XROrigin>();
        Camera cam = xr != null ? xr.Camera : Camera.main;
        if (cam == null) cam = Camera.main;
        if (cam == null)
        {
            // No camera to head-lock to — keep the overlay rather than crash. (Guard Camera.main.)
            Debug.LogWarning("[DangerVignette] VR active but no camera found — keeping overlay fallback.");
            return;
        }

        // Drop the desktop overlay; the world-space panel replaces it. _currentAlpha carries over.
        if (_canvas != null) Destroy(_canvas.gameObject);

        var canvasGO = new GameObject("DangerVignetteVR");
        canvasGO.transform.SetParent(cam.transform, false);
        _canvas = canvasGO.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.WorldSpace;
        _canvas.worldCamera = cam;
        _canvas.sortingOrder = 9999;

        var rt = (RectTransform)canvasGO.transform;
        rt.localPosition = new Vector3(0f, 0f, vrDistance);
        rt.localRotation = Quaternion.identity; // +Z points along camera forward → readable/facing the eyes
        rt.sizeDelta = new Vector2(2000f, 2000f);
        // Panel width = vrDistance * 6 metres ⇒ ~143° angular coverage, independent of vrDistance.
        rt.localScale = Vector3.one * (vrDistance * 6f / rt.sizeDelta.x);

        // No GraphicRaycaster — this overlay must never intercept input.

        var imgGO = new GameObject("Vignette");
        imgGO.transform.SetParent(canvasGO.transform, false);
        _image = imgGO.AddComponent<Image>();
        _image.raycastTarget = false;
        var irt = (RectTransform)imgGO.transform;
        irt.anchorMin = Vector2.zero;
        irt.anchorMax = Vector2.one;
        irt.offsetMin = Vector2.zero;
        irt.offsetMax = Vector2.zero;

        // Draw on top of world geometry like an overlay. Close placement is the primary guarantee
        // (nothing renders that near the eyes); forcing ZTest Always is belt-and-suspenders against
        // a wall the player presses their face into.
        var uiShader = Shader.Find("UI/Default");
        if (uiShader != null)
        {
            var mat = new Material(uiShader);
            mat.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
            _image.material = mat;
        }

        _image.color = new Color(color.r, color.g, color.b, _currentAlpha);
    }
}
