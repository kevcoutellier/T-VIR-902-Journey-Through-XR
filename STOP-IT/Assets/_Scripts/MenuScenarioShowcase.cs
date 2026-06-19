using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;
using TMPro;

/// <summary>
/// STOP IT! — MenuScenarioShowcase
/// The video-game main menu: a screen-space overlay shown over the cinematic
/// <see cref="MenuCameraTour"/> backdrop. It presents the scenarios one at a time
/// in an "attract mode" lower-third (number / title / objective) that stays in
/// sync with whichever room the camera has settled in, plus a clickable scenario
/// list and Play buttons.
///
/// Wiring: built procedurally (no prefab needed). Assign <see cref="cameraTour"/>
/// and <see cref="scenarioManager"/> (auto-found if left null). The
/// <see cref="scenarioPerStop"/> array maps each camera waypoint to a scenario
/// index (-1 = a non-scenario stop such as a corridor); leave empty for a 1:1
/// identity mapping.
///
/// Behaviour (auto-advance + selection):
///   • The camera cycles rooms on its own → the showcase follows via OnArrive.
///   • Clicking a scenario flies the camera to that room (auto-advance pauses).
///   • "Commencer" launches story mode; "Jouer ce scénario" launches the one shown.
///
/// Note: a ScreenSpaceOverlay canvas is the right fit for a flat 2D menu on a
/// dedicated menu camera / desktop. For an in-HMD world-space menu, the existing
/// ScenarioMenu covers that case.
/// </summary>
[DefaultExecutionOrder(25)]
public class MenuScenarioShowcase : MonoBehaviour
{
    [Header("References")]
    public MenuCameraTour cameraTour;
    public ScenarioManager scenarioManager;

    [Header("Mapping")]
    [Tooltip("Scenario index shown at each camera stop. Length should match the " +
             "camera's waypoint count; use -1 for a non-scenario stop. Empty = identity.")]
    public int[] scenarioPerStop;

    [Tooltip("Optional longer objective line per scenario. Falls back to the " +
             "scenario's actionHint when empty.")]
    [TextArea(1, 2)] public string[] objectiveOverrides;

    [Header("Style")]
    public Color accentColor = new Color(1f, 0.81f, 0.25f);
    public Color panelColor  = new Color(0.04f, 0.05f, 0.08f, 0.55f);
    [Tooltip("Seconds for the lower-third cross-fade when the room changes.")]
    public float fadeDuration = 0.45f;

    [Header("Display")]
    [Tooltip("OFF = full-screen overlay (2D / desktop, like the original menu). " +
             "ON = world-space panel that floats in front of the player (VR).")]
    public bool worldSpace = false;

    [Header("World-space placement (VR)")]
    [Tooltip("Distance (m) in front of the player the menu panel floats.")]
    public float followDistance = 2.2f;
    [Tooltip("Vertical offset (m) relative to eye height.")]
    public float verticalOffset = -0.1f;
    [Tooltip("How quickly the panel drifts to follow the head (lower = lazier).")]
    public float followLerpSpeed = 4f;
    [Tooltip("Panel re-centres once the head has turned past this angle (degrees).")]
    public float recenterAngle = 35f;

    // ── UI runtime ────────────────────────────────────────────────────────────
    private Canvas          _canvas;
    private CanvasGroup     _lowerThirdGroup;
    private TextMeshProUGUI _kicker;
    private TextMeshProUGUI _title;
    private TextMeshProUGUI _objective;
    private TextMeshProUGUI _actionPill;
    private readonly List<Image> _listButtonBgs = new List<Image>();
    private readonly List<int>   _listScenarioIndex = new List<int>();

    private bool _built;
    private bool _subscribed;
    private int  _currentScenario = -1;
    private Coroutine _fadeRoutine;

    // World-space head-follow.
    private Transform _camT;
    private Vector3   _targetPos;
    private bool      _hasTarget;

    private static readonly Color ListIdle   = new Color(1f, 1f, 1f, 0.06f);
    private static readonly Color ListActive = new Color(1f, 1f, 1f, 0.16f);
    private static readonly Color InkColor   = new Color(0.06f, 0.07f, 0.10f);

    // ─────────────────────────────────────────────────────────────
    private void Start()
    {
        if (scenarioManager == null) scenarioManager = FindAnyObjectByType<ScenarioManager>();
        if (cameraTour      == null) cameraTour      = FindAnyObjectByType<MenuCameraTour>();

        EnsureEventSystem();
        BuildUI();

        if (cameraTour != null && cameraTour.OnArrive != null)
        {
            cameraTour.OnArrive.AddListener(OnCameraArrive);
            _subscribed = true;
        }

        if (GameManager.Instance != null)
            GameManager.Instance.OnStateChanged.AddListener(OnStateChanged);

        bool inMenu = GameManager.Instance == null ||
                      GameManager.Instance.State == GameManager.GameState.Menu;
        SetVisible(inMenu);
    }

    private void OnDestroy()
    {
        if (_subscribed && cameraTour != null)
            cameraTour.OnArrive.RemoveListener(OnCameraArrive);
        if (GameManager.Instance != null)
            GameManager.Instance.OnStateChanged.RemoveListener(OnStateChanged);
    }

    private void OnStateChanged(GameManager.GameState state)
    {
        SetVisible(state == GameManager.GameState.Menu);
    }

    private void SetVisible(bool on)
    {
        if (_canvas != null) _canvas.enabled = on;
        if (on) _hasTarget = false; // re-centre in front of the player when (re)shown
    }

    private void LateUpdate()
    {
        if (!worldSpace || _canvas == null || !_canvas.enabled) return;

        if (_camT == null)
        {
            var c = Camera.main;
            if (c == null) return;
            _camT = c.transform;
            if (_canvas.worldCamera == null) _canvas.worldCamera = c;
        }

        Vector3 fwd = _camT.forward; fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.001f) fwd = Vector3.forward;
        fwd.Normalize();

        Vector3 ideal = _camT.position + fwd * followDistance + Vector3.up * verticalOffset;

        if (!_hasTarget)
        {
            _targetPos = ideal;
            transform.position = ideal;
            _hasTarget = true;
        }
        else
        {
            Vector3 toPanel = transform.position - _camT.position; toPanel.y = 0f;
            if (toPanel.sqrMagnitude > 0.001f && Vector3.Angle(fwd, toPanel.normalized) > recenterAngle)
                _targetPos = ideal;
            transform.position = Vector3.Lerp(transform.position, _targetPos, followLerpSpeed * Time.deltaTime);
        }

        Vector3 lookDir = transform.position - _camT.position;
        if (lookDir.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(lookDir);
    }

    // ─────────────────────────────────────────────────────────────
    private int ScenarioForStop(int stop)
    {
        if (scenarioPerStop != null && stop >= 0 && stop < scenarioPerStop.Length)
            return scenarioPerStop[stop];
        return stop;
    }

    private int StopForScenario(int scenario)
    {
        if (scenarioPerStop != null)
        {
            for (int i = 0; i < scenarioPerStop.Length; i++)
                if (scenarioPerStop[i] == scenario) return i;
        }
        return scenario;
    }

    private void OnCameraArrive(int stop)
    {
        int scenario = ScenarioForStop(stop);
        if (scenario < 0) return; // corridor / non-scenario stop — keep the last card
        ShowScenario(scenario);
    }

    // ─────────────────────────────────────────────────────────────
    private void ShowScenario(int scenario)
    {
        if (scenarioManager == null || scenario < 0 || scenario >= scenarioManager.ScenarioCount) return;
        _currentScenario = scenario;

        var cfg   = scenarioManager.scenarios[scenario];
        int total = scenarioManager.ScenarioCount;
        string name = !string.IsNullOrEmpty(cfg.scenarioName) ? cfg.scenarioName : $"Scénario {scenario + 1}";

        if (_kicker != null) _kicker.text = $"SCÉNARIO {scenario + 1} / {total}";
        if (_title  != null) _title.text  = name;
        if (_objective != null)
        {
            string obj = (objectiveOverrides != null && scenario < objectiveOverrides.Length &&
                          !string.IsNullOrEmpty(objectiveOverrides[scenario]))
                ? objectiveOverrides[scenario]
                : cfg.actionHint;
            _objective.text = obj;
        }
        if (_actionPill != null) _actionPill.text = cfg.actionHint;

        HighlightList(scenario);
        PlayFade();
    }

    private void HighlightList(int scenario)
    {
        for (int i = 0; i < _listButtonBgs.Count; i++)
            _listButtonBgs[i].color = _listScenarioIndex[i] == scenario ? ListActive : ListIdle;
    }

    private void PlayFade()
    {
        if (_lowerThirdGroup == null) return;
        if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);
        _fadeRoutine = StartCoroutine(FadeInRoutine());
    }

    private IEnumerator FadeInRoutine()
    {
        _lowerThirdGroup.alpha = 0f;
        float t = 0f, dur = Mathf.Max(0.01f, fadeDuration);
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            _lowerThirdGroup.alpha = Mathf.Clamp01(t / dur);
            yield return null;
        }
        _lowerThirdGroup.alpha = 1f;
        _fadeRoutine = null;
    }

    // ─────────────────────────────────────────────────────────────
    // Button handlers
    private void OnScenarioClicked(int scenario)
    {
        if (cameraTour != null) cameraTour.GoToStop(StopForScenario(scenario));
        else ShowScenario(scenario);
    }

    private void OnPlayStory()
    {
        var director = FindAnyObjectByType<StoryModeDirector>();
        if (director != null) director.StartStoryMode();
        else GameManager.Instance?.LaunchAll();
    }

    private void OnPlayCurrent()
    {
        if (scenarioManager == null || _currentScenario < 0) { OnPlayStory(); return; }
        scenarioManager.SetNextScenario(_currentScenario);
        GameManager.Instance?.LaunchSingle();
    }

    // ─────────────────────────────────────────────────────────────
    // UI construction (reference resolution 1920×1080)
    private void BuildUI()
    {
        if (_built) return;
        _built = true;

        _canvas = gameObject.GetComponent<Canvas>();
        if (_canvas == null) _canvas = gameObject.AddComponent<Canvas>();

        var root = (RectTransform)transform;

        if (worldSpace)
        {
            // VR: floating world-space panel (overlay canvases don't render in the HMD).
            _canvas.renderMode = RenderMode.WorldSpace;
            if (_canvas.worldCamera == null) _canvas.worldCamera = Camera.main;
            root.sizeDelta = new Vector2(1920, 1080);
            root.localScale = Vector3.one * 0.001f; // 1px = 1mm → ~1.92m × 1.08m panel
            if (gameObject.GetComponent<TrackedDeviceGraphicRaycaster>() == null)
                gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();
        }
        else
        {
            // 2D / desktop: full-screen overlay, scaled to the screen.
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 100;
            var scaler = gameObject.GetComponent<CanvasScaler>();
            if (scaler == null) scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
        }

        if (gameObject.GetComponent<GraphicRaycaster>() == null)
            gameObject.AddComponent<GraphicRaycaster>();
        BuildTitle(root);
        BuildLowerThird(root);
        BuildScenarioList(root);
        BuildBottomBar(root);

        if (scenarioManager != null && scenarioManager.ScenarioCount > 0)
            ShowScenario(0);
    }

    private void BuildTitle(RectTransform root)
    {
        var logo = MakeText(root, "Title", "STOP IT!", 84, FontStyles.Bold, accentColor, TextAlignmentOptions.TopLeft);
        TopLeft(logo.rectTransform, 60, 50, 900, 110);

        var tag = MakeText(root, "Tagline", "PAPA, OUVRE L'ŒIL", 26, FontStyles.Normal,
                           new Color(0.78f, 0.81f, 0.88f), TextAlignmentOptions.TopLeft);
        TopLeft(tag.rectTransform, 64, 164, 900, 40);
    }

    private void BuildLowerThird(RectTransform root)
    {
        var panel = MakePanel(root, "LowerThird", panelColor);
        BottomLeft(panel, 60, 190, 900, 280);
        _lowerThirdGroup = panel.gameObject.AddComponent<CanvasGroup>();

        _kicker = MakeText(panel, "Kicker", "SCÉNARIO 1 / 5", 26, FontStyles.Normal,
                           new Color(0.78f, 0.81f, 0.88f), TextAlignmentOptions.TopLeft);
        StretchTop(_kicker.rectTransform, 30, 22, 30, 36);

        _title = MakeText(panel, "ScenarioTitle", "", 52, FontStyles.Bold, Color.white, TextAlignmentOptions.TopLeft);
        StretchTop(_title.rectTransform, 30, 58, 30, 70);

        _objective = MakeText(panel, "Objective", "", 30, FontStyles.Normal,
                              new Color(0.85f, 0.88f, 0.94f), TextAlignmentOptions.TopLeft);
        _objective.enableWordWrapping = true;
        StretchTop(_objective.rectTransform, 30, 134, 30, 80);

        var pill = MakePanel(panel, "ActionPill", accentColor);
        BottomLeft(pill, 30, 24, 380, 50);
        _actionPill = MakeText(pill, "ActionText", "", 24, FontStyles.Bold, InkColor, TextAlignmentOptions.Center);
        Fill(_actionPill.rectTransform);
    }

    private void BuildScenarioList(RectTransform root)
    {
        if (scenarioManager == null) return;

        var panel = MakePanel(root, "ScenarioListBg", panelColor);
        TopRight(panel, 60, 120, 420, 470);

        var header = MakeText(panel, "ListHeader", "SCÉNARIOS", 24, FontStyles.Normal,
                              new Color(0.78f, 0.81f, 0.88f), TextAlignmentOptions.TopLeft);
        StretchTop(header.rectTransform, 24, 18, 24, 34);

        int count = scenarioManager.ScenarioCount;
        float btnH = 64f, gap = 12f, top = 66f;
        for (int i = 0; i < count; i++)
        {
            int scenario = i;
            string label = (i + 1) + ".  " + scenarioManager.GetScenarioName(i);
            var (rt, bg) = MakeListButton(panel, label, () => OnScenarioClicked(scenario));
            StretchTop(rt, 24, top + i * (btnH + gap), 24, btnH);
            _listButtonBgs.Add(bg);
            _listScenarioIndex.Add(scenario);
        }
    }

    private void BuildBottomBar(RectTransform root)
    {
        var story = MakeBigButton(root, "Commencer (Histoire)", accentColor, InkColor, OnPlayStory);
        BottomLeft((RectTransform)story.transform, 60, 90, 420, 76);

        var single = MakeBigButton(root, "Jouer ce scénario", new Color(1f, 1f, 1f, 0.14f), Color.white, OnPlayCurrent);
        BottomLeft((RectTransform)single.transform, 500, 90, 360, 76);
    }

    // ─────────────────────────────────────────────────────────────
    // Low-level UI helpers
    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
        if (FindAnyObjectByType<EventSystem>() != null) return;
        var es = new GameObject("EventSystem", typeof(EventSystem));
        es.AddComponent<InputSystemUIInputModule>();
    }

    private RectTransform MakePanel(Transform parent, string name, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        go.AddComponent<Image>().color = color;
        return (RectTransform)go.transform;
    }

    private TextMeshProUGUI MakeText(Transform parent, string name, string text, float size,
                                     FontStyles style, Color color, TextAlignmentOptions align)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.color = color;
        tmp.alignment = align;
        tmp.enableWordWrapping = false;
        tmp.raycastTarget = false;
        return tmp;
    }

    private (RectTransform, Image) MakeListButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick)
    {
        var go = new GameObject("Item_" + Safe(label), typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var bg = go.AddComponent<Image>();
        bg.color = ListIdle;
        var btn = go.AddComponent<Button>();
        btn.onClick.AddListener(onClick);
        btn.targetGraphic = bg;
        var colors = btn.colors;
        colors.highlightedColor = new Color(1f, 1f, 1f, 0.30f);
        colors.pressedColor     = new Color(1f, 1f, 1f, 0.45f);
        btn.colors = colors;

        var txt = MakeText(go.transform, "Text", label, 26, FontStyles.Normal, Color.white, TextAlignmentOptions.Left);
        StretchLeftPadded(txt.rectTransform, 20, 40);
        return ((RectTransform)go.transform, bg);
    }

    private GameObject MakeBigButton(Transform parent, string label, Color bgColor, Color textColor,
                                     UnityEngine.Events.UnityAction onClick)
    {
        var go = new GameObject("Btn_" + Safe(label), typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var bg = go.AddComponent<Image>();
        bg.color = bgColor;
        var btn = go.AddComponent<Button>();
        btn.onClick.AddListener(onClick);
        btn.targetGraphic = bg;

        var txt = MakeText(go.transform, "Text", label, 30, FontStyles.Bold, textColor, TextAlignmentOptions.Center);
        Fill(txt.rectTransform);
        return go;
    }

    private static string Safe(string s) => s.Substring(0, Mathf.Min(s.Length, 18));

    // ── Anchored placement (all sizes in reference-resolution pixels) ──────────
    private static void TopLeft(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0, 1);
        rt.anchoredPosition = new Vector2(x, -y);
        rt.sizeDelta = new Vector2(w, h);
    }

    private static void TopRight(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(1, 1);
        rt.pivot = new Vector2(1, 1);
        rt.anchoredPosition = new Vector2(-x, -y);
        rt.sizeDelta = new Vector2(w, h);
    }

    private static void BottomLeft(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0, 0);
        rt.pivot = new Vector2(0, 0);
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(w, h);
    }

    /// <summary>Stretch horizontally inside the parent, anchored at the parent's top.</summary>
    private static void StretchTop(RectTransform rt, float left, float top, float right, float height)
    {
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(1, 1);
        rt.pivot = new Vector2(0.5f, 1);
        rt.offsetMin = new Vector2(left, -(top + height));
        rt.offsetMax = new Vector2(-right, -top);
    }

    /// <summary>Stretch to fill the parent with a left/right padding (height fills too).</summary>
    private static void StretchLeftPadded(RectTransform rt, float left, float right)
    {
        rt.anchorMin = new Vector2(0, 0);
        rt.anchorMax = new Vector2(1, 1);
        rt.offsetMin = new Vector2(left, 0);
        rt.offsetMax = new Vector2(-right, 0);
    }

    private static void Fill(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
