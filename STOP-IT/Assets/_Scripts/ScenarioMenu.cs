using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;
using TMPro;

/// <summary>
/// STOP IT! — ScenarioMenu
/// Floating VR menu with two sub-panels sharing the same world-space Canvas:
///   • _mainPanel  — scenario list (shown in GameState.Menu, or via Y toggle)
///   • _retryPanel — end-of-round summary with Restart / Continue / Menu buttons
///                   (shown on Success in single mode, on Fail in any mode, on GameOver)
///
/// Y button on the left controller (via XRLocomotionBinder) opens/closes the main
/// panel at any time — useful as an "abandon current round" shortcut.
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

    // Sub-panels
    private GameObject _mainPanel;
    private GameObject _retryPanel;
    private Image      _retryBackground;
    private TextMeshProUGUI _retryTitle;
    private TextMeshProUGUI _retrySubtitle;
    private GameObject _retryRestartBtn;
    private GameObject _retryContinueBtn;
    private GameObject _retryMenuBtn;
    private TextMeshProUGUI _retryRestartLabel;
    private TextMeshProUGUI _retryContinueLabel;
    private TextMeshProUGUI _retryMenuLabel;

    // Retry panel palette
    private static readonly Color FailBgColor    = new Color(0.30f, 0.06f, 0.06f, 0.96f);
    private static readonly Color SuccessBgColor = new Color(0.06f, 0.22f, 0.10f, 0.96f);
    private static readonly Color GameOverBgColor= new Color(0.18f, 0.14f, 0.04f, 0.96f);

    // ─────────────────────────────────────────────────────────────
    private void Start()
    {
        _canvas = GetComponent<Canvas>();
        if (_canvas == null)
        {
            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
        }

        // World-space canvas needs a camera to map mouse-screen rays into world space.
        if (_canvas.worldCamera == null) _canvas.worldCamera = Camera.main;

        // Both raycasters live side by side: XR rays use TrackedDeviceGraphicRaycaster,
        // mouse / touch / gamepad navigation use the standard GraphicRaycaster.
        if (GetComponent<TrackedDeviceGraphicRaycaster>() == null)
            gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();
        if (GetComponent<GraphicRaycaster>() == null)
            gameObject.AddComponent<GraphicRaycaster>();

        // Without an EventSystem (+ input module), no UI click ever fires.
        EnsureEventSystem();

        if (scenarioManager == null)
            scenarioManager = FindAnyObjectByType<ScenarioManager>();

        if (GameManager.Instance != null)
            GameManager.Instance.OnStateChanged.AddListener(OnStateChanged);

        BuildMenu();

        // Start hidden — player opens it manually with Y button (or it auto-shows on Menu).
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
        if (_visible && _mainPanel != null && _mainPanel.activeSelf) Hide();
        else                                                          ShowMainPanel();
    }

    // ─────────────────────────────────────────────────────────────
    private void OnStateChanged(GameManager.GameState state)
    {
        switch (state)
        {
            case GameManager.GameState.Menu:
                ShowMainPanel();
                break;

            case GameManager.GameState.Playing:
                Hide();
                break;

            case GameManager.GameState.Fail:
                ShowRetryPanel(state);
                break;

            case GameManager.GameState.Success:
                // In LaunchAll, the round chains automatically — no retry panel.
                // In single mode, give the player explicit Replay / Menu buttons.
                if (GameManager.Instance != null && GameManager.Instance.IsAutoAdvance) Hide();
                else                                                                    ShowRetryPanel(state);
                break;

            case GameManager.GameState.GameOver:
                ShowRetryPanel(state);
                break;
        }
    }

    // ─────────────────────────────────────────────────────────────
    private void ShowAtFront()
    {
        _hasTarget = false;
        _visible   = true;
        if (_canvas != null) _canvas.enabled = true;
    }

    public void ShowMainPanel()
    {
        if (_mainPanel  != null) _mainPanel.SetActive(true);
        if (_retryPanel != null) _retryPanel.SetActive(false);
        ShowAtFront();
    }

    public void ShowRetryPanel(GameManager.GameState state)
    {
        if (_mainPanel  != null) _mainPanel.SetActive(false);
        if (_retryPanel == null) return;
        _retryPanel.SetActive(true);
        ConfigureRetryPanelForState(state);
        ShowAtFront();
    }

    /// <summary>Legacy alias kept for any existing callers — opens the main panel.</summary>
    public void ShowInFront() => ShowMainPanel();

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

        // Root background (visible behind whichever sub-panel is active)
        var bg = gameObject.AddComponent<Image>();
        bg.color = new Color(0.08f, 0.08f, 0.14f, 0.95f);

        BuildMainPanel();
        BuildRetryPanel();
    }

    // ─────────────────────────────────────────────────────────────
    private void BuildMainPanel()
    {
        _mainPanel = MakePanel("MainPanel");

        // Title
        MakeText(_mainPanel.transform, "Title", "STOP IT !", 36, FontStyles.Bold,
                 new Color(1f, 0.85f, 0.2f), new Vector2(0, -10), 50);

        // Subtitle
        MakeText(_mainPanel.transform, "Subtitle", "Choisissez un scenario", 18, FontStyles.Normal,
                 Color.white, new Vector2(0, -55), 30);

        MakeText(_mainPanel.transform, "Hint", "[Bouton Y] pour ouvrir/fermer", 12, FontStyles.Italic,
                 new Color(0.7f, 0.7f, 0.7f), new Vector2(0, -82), 22);

        // Scenario buttons
        float yOffset = -110f;
        for (int i = 0; i < scenarioManager.ScenarioCount; i++)
        {
            int    idx   = i;
            string label = (i + 1) + ". " + scenarioManager.GetScenarioName(i);
            CreateButton(_mainPanel.transform, label, yOffset, () => LaunchScenario(idx));
            yOffset -= buttonHeight + buttonSpacing;
        }

        // Play All button
        yOffset -= 10f;
        CreateButton(_mainPanel.transform, ">>> JOUER TOUT <<<", yOffset, LaunchAll, new Color(0.15f, 0.55f, 0.25f));
    }

    // ─────────────────────────────────────────────────────────────
    private void BuildRetryPanel()
    {
        _retryPanel = MakePanel("RetryPanel");

        // Tinted background overlay so the panel reads as Fail / Success / GameOver at a glance.
        var bgGO = new GameObject("Bg");
        bgGO.transform.SetParent(_retryPanel.transform, false);
        _retryBackground = bgGO.AddComponent<Image>();
        _retryBackground.color = FailBgColor;
        var bgRT = bgGO.GetComponent<RectTransform>();
        StretchFull(bgRT);

        // Title (e.g. "TROP TARD !" / "BIEN JOUÉ !" / "FIN DE PARTIE")
        _retryTitle = MakeText(_retryPanel.transform, "Title", "TROP TARD !", 44, FontStyles.Bold,
                               new Color(1f, 0.95f, 0.95f), new Vector2(0, -25), 60);

        // Subtitle (score / scenario hint)
        _retrySubtitle = MakeText(_retryPanel.transform, "Subtitle", "", 18, FontStyles.Normal,
                                  new Color(0.95f, 0.95f, 0.95f, 0.9f), new Vector2(0, -85), 30);

        // Buttons
        const float btnY0 = -150f;
        const float btnGap = 12f;
        float y = btnY0;

        _retryRestartBtn = CreateButton(_retryPanel.transform, "RECOMMENCER", y,
                                        OnRestartClicked, new Color(0.20f, 0.55f, 0.95f));
        _retryRestartLabel = _retryRestartBtn.GetComponentInChildren<TextMeshProUGUI>();
        y -= buttonHeight + btnGap;

        _retryContinueBtn = CreateButton(_retryPanel.transform, "PASSER AU SUIVANT", y,
                                         OnContinueClicked, new Color(0.85f, 0.55f, 0.10f));
        _retryContinueLabel = _retryContinueBtn.GetComponentInChildren<TextMeshProUGUI>();
        y -= buttonHeight + btnGap;

        _retryMenuBtn = CreateButton(_retryPanel.transform, "MENU PRINCIPAL", y,
                                     OnMenuClicked, new Color(0.35f, 0.35f, 0.45f));
        _retryMenuLabel = _retryMenuBtn.GetComponentInChildren<TextMeshProUGUI>();

        _retryPanel.SetActive(false);
    }

    private void ConfigureRetryPanelForState(GameManager.GameState state)
    {
        bool autoAdvance = GameManager.Instance != null && GameManager.Instance.IsAutoAdvance;
        int  score       = GameManager.Instance != null ? GameManager.Instance.Score          : 0;
        int  total       = GameManager.Instance != null ? GameManager.Instance.TotalScenarios : 1;

        switch (state)
        {
            case GameManager.GameState.Fail:
                _retryBackground.color = FailBgColor;
                if (_retryTitle    != null) _retryTitle.text    = "TROP TARD !";
                if (_retryTitle    != null) _retryTitle.color   = new Color(1f, 0.50f, 0.50f);
                if (_retrySubtitle != null) _retrySubtitle.text = autoAdvance
                    ? $"Score : {score} / {total}   —   choisis ton action."
                    : "L'enfant a atteint le danger.";
                SetRestartVisible(true);
                SetContinueVisible(autoAdvance, "PASSER AU SUIVANT");
                SetMenuVisible(true);
                break;

            case GameManager.GameState.Success:
                _retryBackground.color = SuccessBgColor;
                if (_retryTitle    != null) _retryTitle.text    = "BIEN JOUÉ !";
                if (_retryTitle    != null) _retryTitle.color   = new Color(0.55f, 1f, 0.65f);
                if (_retrySubtitle != null) _retrySubtitle.text = "Tu as sauvé l'enfant.";
                SetRestartVisible(true);
                SetContinueVisible(false, null); // single mode only ever shows Success here
                SetMenuVisible(true);
                break;

            case GameManager.GameState.GameOver:
                _retryBackground.color = GameOverBgColor;
                if (_retryTitle    != null) _retryTitle.text    = "FIN DE PARTIE";
                if (_retryTitle    != null) _retryTitle.color   = new Color(1f, 0.90f, 0.30f);
                if (_retrySubtitle != null) _retrySubtitle.text = $"Score final : {score} / {total}";
                SetRestartVisible(false);
                SetContinueVisible(false, null);
                SetMenuVisible(true);
                if (_retryMenuLabel != null) _retryMenuLabel.text = "RETOUR AU MENU";
                break;
        }

        LayoutRetryButtons();
    }

    /// <summary>
    /// Stack only the visible retry buttons from the top, so hidden slots don't leave gaps.
    /// </summary>
    private void LayoutRetryButtons()
    {
        const float btnY0  = -150f;
        const float btnGap = 12f;
        float y = btnY0;

        if (_retryRestartBtn != null && _retryRestartBtn.activeSelf)
        {
            SetButtonY(_retryRestartBtn, y);
            y -= buttonHeight + btnGap;
        }
        if (_retryContinueBtn != null && _retryContinueBtn.activeSelf)
        {
            SetButtonY(_retryContinueBtn, y);
            y -= buttonHeight + btnGap;
        }
        if (_retryMenuBtn != null && _retryMenuBtn.activeSelf)
        {
            SetButtonY(_retryMenuBtn, y);
        }
    }

    private static void SetButtonY(GameObject btn, float y)
    {
        var rt = btn.GetComponent<RectTransform>();
        if (rt == null) return;
        var pos = rt.anchoredPosition;
        pos.y = y;
        rt.anchoredPosition = pos;
    }

    private void SetRestartVisible(bool visible)
    {
        if (_retryRestartBtn != null) _retryRestartBtn.SetActive(visible);
    }

    private void SetContinueVisible(bool visible, string label)
    {
        if (_retryContinueBtn != null) _retryContinueBtn.SetActive(visible);
        if (visible && _retryContinueLabel != null && !string.IsNullOrEmpty(label))
            _retryContinueLabel.text = label;
    }

    private void SetMenuVisible(bool visible)
    {
        if (_retryMenuBtn != null) _retryMenuBtn.SetActive(visible);
        if (visible && _retryMenuLabel != null)
            _retryMenuLabel.text = "MENU PRINCIPAL"; // default; GameOver overrides above
    }

    // ─────────────────────────────────────────────────────────────
    // Button handlers
    private void OnRestartClicked()
    {
        GameManager.Instance?.RestartCurrentScenario();
    }

    private void OnContinueClicked()
    {
        GameManager.Instance?.ContinueToNext();
    }

    private void OnMenuClicked()
    {
        GameManager.Instance?.ReturnToMenu();
    }

    // ─────────────────────────────────────────────────────────────
    /// <summary>
    /// Make sure an EventSystem with an input module exists in the scene.
    /// Without it, the standard GraphicRaycaster fires no click events in desktop mode.
    /// </summary>
    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
        if (FindAnyObjectByType<EventSystem>() != null) return;

        var es = new GameObject("EventSystem", typeof(EventSystem));
        // Project uses the new Input System (XRI 3.x) — InputSystemUIInputModule handles
        // both mouse/keyboard/gamepad and the XR-driven UI ray pointers via XRUIInputModule
        // when present. If the input system package is missing this AddComponent fails fast.
        es.AddComponent<InputSystemUIInputModule>();
    }

    // ─────────────────────────────────────────────────────────────
    // Layout helpers
    private GameObject MakePanel(string panelName)
    {
        // Constructor takes the type so the GameObject is created with a RectTransform
        // instead of the default Transform — you cannot AddComponent<RectTransform> later.
        var go = new GameObject(panelName, typeof(RectTransform));
        go.transform.SetParent(transform, false);
        StretchFull((RectTransform)go.transform);
        return go;
    }

    private static void StretchFull(RectTransform rt)
    {
        rt.anchorMin        = Vector2.zero;
        rt.anchorMax        = Vector2.one;
        rt.pivot            = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta        = Vector2.zero;
    }

    private TextMeshProUGUI MakeText(Transform parent, string goName, string text, float size,
                                     FontStyles style, Color color, Vector2 anchoredPos, float height)
    {
        var go  = new GameObject(goName);
        go.transform.SetParent(parent, false);
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
        return tmp;
    }

    private GameObject CreateButton(Transform parent, string label, float yPos,
                                    UnityEngine.Events.UnityAction onClick,
                                    Color? color = null)
    {
        var safeName = "Btn_" + label.Substring(0, Mathf.Min(label.Length, 20));
        var btnGO = new GameObject(safeName);
        btnGO.transform.SetParent(parent, false);

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
        txtRT.anchoredPosition = Vector2.zero;
        return btnGO;
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
