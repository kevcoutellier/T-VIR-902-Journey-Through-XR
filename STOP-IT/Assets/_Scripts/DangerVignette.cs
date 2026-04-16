using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// STOP IT! — DangerVignette
/// Full-screen red overlay whose alpha tracks the child's proximity to the hazard.
/// Creates its own Screen-Space-Overlay Canvas + Image on the fly so no scene wiring is needed.
/// In VR this renders per-eye via the overlay canvas — perfectly fine for a gentle fade.
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

    private ChildNPC _child;
    private Canvas _canvas;
    private Image _image;
    private float _currentAlpha;

    private void Awake()
    {
        EnsureCanvas();
    }

    private void OnEnable()
    {
        if (hazard == null) hazard = FindAnyObjectByType<HazardZone>();
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

    private void EnsureCanvas()
    {
        var canvasGO = new GameObject("DangerVignetteCanvas");
        canvasGO.transform.SetParent(transform, false);
        _canvas = canvasGO.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 9999;

        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        canvasGO.AddComponent<GraphicRaycaster>();

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
}
