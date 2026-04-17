using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;
using TMPro;

/// <summary>
/// STOP IT! — ScenarioMenu
/// Floating VR menu that follows the player's gaze with a smooth lag.
/// Opens/closes with the Y button on the left controller (via XRLocomotionBinder).
/// Shows buttons for each scenario + a "Play All" button.
///
/// The Canvas stays in World Space (required for VR raycasting) but repositions
/// itself in front of the camera each time it is shown, then gently follows.
/// </summary>
public class ScenarioMenu : MonoBehaviour
{
    [Header("References")]
    public ScenarioManager scenarioManager;

    [Header("Follow Settings")]
    [Tooltip("Distance from camera at which the menu floats")]
    public float followDistance = 2f;
    [Tooltip("Vertical offset relative to camera (negative = slightly below eye)")]
    public float verticalOffset = -0.15f;
    [Tooltip("How quickly the menu drifts toward its target position (lower = lazier)")]
    public float followLerpSpeed = 3f;
    [Tooltip("Angle threshold before the menu starts re-centering (degrees)")]
    public float angularThreshold = 30f;

    [Header("UI Settings")]
    public float buttonHeight  = 40f;
    public float buttonSpacing = 8f;

    // ── Internal ──────────────────────────────────────────────────
    private Canvas    _canvas;
    private bool      _built    = false;
    private bool      _visible  = false;
    private Transform _camTransform;

    // Target world position the menu is lerping toward
    private Vector3 _targetPos;
    private bool    _hasTarget = false;

    // ─────────────────────────────────────────────────────────────
    private void Start()
    {
        _canvas = GetComponent<Canvas>();
        if (_canvas == null)
        {
            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
        }

        // VR raycasting
        if (GetComponent<TrackedDeviceGraphicRaycaster>() == null)
            gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();

        // Remove standard raycaster (breaks VR pointer)
        var stdRaycaster = GetComponent<GraphicRaycaster>();
        if (stdRaycaster != null) Destroy(stdRaycaster);

        if (scenarioManager == null)
            scenarioManager = FindAnyObjectByType<ScenarioManager>();

        if (GameManager.Instance != null)
            GameManager.Instance.OnStateChanged.AddListener(OnStateChanged);

        BuildMenu();

        // Start hidden — player opens it manually with Y button
        Hide();
    }

    private void OnDestroy()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnStateChanged.RemoveListener(OnStateChanged);
    }

    // ─────────────────────────────────────────────────────────────
    private void LateUpdate()
    {
        if (!_visible) return;

        // Find camera once
        if (_camTransform == null)
        {
            var cam = Camera.main;
            if (cam == null) return;
            _camTransform = cam.transform;
        }

        // ── Compute ideal position in front of camera ─────────────
        Vector3 camForward = _camTransform.forward;
        camForward.y = 0f;
        if (camForward.sqrMagnitude < 0.001f) camForward = Vector3.forward;
        camForward.Normalize();

        Vector3 ideal = _camTransform.position
                      + camForward * followDistance
                      + Vector3.up * verticalOffset;

        // ── Snap target if first frame or camera turned a lot ─────
        if (!_hasTarget)
        {
            _targetPos = ideal;
            transform.position = ideal;
            _hasTarget = true;
        }
        else
        {
            // Re-center if camera has turned significantly
            Vector3 toMenu   = (transform.position - _camTransform.position).normalized;
            float   angle    = Vector3.Angle(camForward, toMenu);
            if (angle > angularThreshold)
                _targetPos = ideal;

            // Lazy follow
            transform.position = Vector3.Lerp(transform.position, _targetPos, followLerpSpeed * Time.deltaTime);
        }

        // ── Always face the camera ─────────────────────────────────
        Vector3 lookDir = transform.position - _camTransform.position;
        if (lookDir.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(lookDir);
    }

    // ─────────────────────────────────────────────────────────────
    // Called by XRLocomotionBinder when Y button is pressed
    public void ToggleMenu()
    {
        if (_visible) Hide();
        else          ShowInFront();
    }

    // ─────────────────────────────────────────────────────────────
    private void OnStateChanged(GameManager.GameState state)
    {
        if (state == GameManager.GameState.Menu)
            ShowInFront();
        else
            Hide();
    }

    // ─────────────────────────────────────────────────────────────
    /// <summary>Snap the menu directly in front of the camera, then show it.</summary>
    public void ShowInFront()
    {
        // Force a position snap on next LateUpdate
        _hasTarget = false;
        _visible   = true;
        if (_canvas != null) _canvas.enabled = true;
    }

    public void Hide()
    {
        _visible = false;
        if (_canvas != null) _canvas.enabled = false;
    }

    // ─────────────────────────────────────────────────────────────
    private void BuildMenu()
    {
        if (_built || scenarioManager == null) return;
        _built = true;

        // Canvas size (0.5m × 0.4m in world space)
        var rt = GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(500, 400);
        transform.localScale = Vector3.one * 0.001f; // 1 pixel = 1mm

        // Background
        var bg = gameObject.AddComponent<Image>();
        bg.color = new Color(0.08f, 0.08f, 0.14f, 0.95f);

        // ── Title ─────────────────────────────────────────────────
        MakeText("Title", "STOP IT !", 36, FontStyles.Bold,
                 new Color(1f, 0.85f, 0.2f), new Vector2(0, -10), 50);

        // ── Subtitle ──────────────────────────────────────────────
        MakeText("Subtitle", "Choisissez un scenario", 18, FontStyles.Normal,
                 Color.white, new Vector2(0, -55), 30);

        MakeText("Hint", "[Bouton Y] pour ouvrir/fermer", 12, FontStyles.Italic,
                 new Color(0.7f, 0.7f, 0.7f), new Vector2(0, -82), 22);

        // ── Scenario buttons ──────────────────────────────────────
        float yOffset = -110f;
        for (int i = 0; i < scenarioManager.ScenarioCount; i++)
        {
            int    idx   = i;
            string label = (i + 1) + ". " + scenarioManager.GetScenarioName(i);
            CreateButton(label, yOffset, () => LaunchScenario(idx));
            yOffset -= buttonHeight + buttonSpacing;
        }

        // ── Play All button ───────────────────────────────────────
        yOffset -= 10f;
        CreateButton(">>> JOUER TOUT <<<", yOffset, LaunchAll, new Color(0.15f, 0.55f, 0.25f));
    }

    // ─────────────────────────────────────────────────────────────
    private void MakeText(string goName, string text, float size, FontStyles style,
                          Color color, Vector2 anchoredPos, float height)
    {
        var go  = new GameObject(goName);
        go.transform.SetParent(transform, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text      = text;
        tmp.fontSize  = size;
        tmp.fontStyle = style;
        tmp.color     = color;
        tmp.alignment = TextAlignmentOptions.Center;
        var r = go.GetComponent<RectTransform>();
        r.anchorMin       = new Vector2(0,    1);
        r.anchorMax       = new Vector2(1,    1);
        r.pivot           = new Vector2(0.5f, 1);
        r.anchoredPosition = anchoredPos;
        r.sizeDelta       = new Vector2(0, height);
    }

    private void CreateButton(string label, float yPos,
                               UnityEngine.Events.UnityAction onClick,
                               Color? color = null)
    {
        var btnGO = new GameObject("Btn_" + label.Substring(0, Mathf.Min(label.Length, 20)));
        btnGO.transform.SetParent(transform, false);

        var img   = btnGO.AddComponent<Image>();
        img.color = color ?? new Color(0.22f, 0.22f, 0.32f, 1f);

        var btn   = btnGO.AddComponent<Button>();
        btn.onClick.AddListener(onClick);

        var colors               = btn.colors;
        colors.highlightedColor  = new Color(0.38f, 0.38f, 0.58f);
        colors.pressedColor      = new Color(0.55f, 0.55f, 0.75f);
        btn.colors               = colors;

        var btnRT                = btnGO.GetComponent<RectTransform>();
        btnRT.anchorMin          = new Vector2(0.05f, 1);
        btnRT.anchorMax          = new Vector2(0.95f, 1);
        btnRT.pivot              = new Vector2(0.5f,  1);
        btnRT.anchoredPosition   = new Vector2(0, yPos);
        btnRT.sizeDelta          = new Vector2(0, buttonHeight);

        // Button label
        var txtGO = new GameObject("Text");
        txtGO.transform.SetParent(btnGO.transform, false);
        var tmp           = txtGO.AddComponent<TextMeshProUGUI>();
        tmp.text          = label;
        tmp.fontSize      = 16;
        tmp.color         = Color.white;
        tmp.alignment     = TextAlignmentOptions.Center;
        var txtRT         = txtGO.GetComponent<RectTransform>();
        txtRT.anchorMin   = Vector2.zero;
        txtRT.anchorMax   = Vector2.one;
        txtRT.sizeDelta   = Vector2.zero;
    }

    // ─────────────────────────────────────────────────────────────
    private void LaunchScenario(int index)
    {
        scenarioManager.SetNextScenario(index);
        GameManager.Instance?.LaunchSingle();
        Hide();
    }

    private void LaunchAll()
    {
        scenarioManager.SetNextScenario(0);
        GameManager.Instance?.LaunchAll();
        Hide();
    }
}
