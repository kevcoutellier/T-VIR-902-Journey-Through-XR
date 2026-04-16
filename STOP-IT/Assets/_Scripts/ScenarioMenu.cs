using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;
using TMPro;

/// <summary>
/// STOP IT! — ScenarioMenu
/// World-space VR menu that appears when the game is in Menu state.
/// Shows buttons for each scenario + a "Play All" button.
/// Attach to a world-space Canvas.
/// </summary>
public class ScenarioMenu : MonoBehaviour
{
    [Header("References")]
    public ScenarioManager scenarioManager;

    [Header("UI Elements (auto-created if null)")]
    public Transform buttonContainer;

    [Header("Settings")]
    public float buttonHeight = 40f;
    public float buttonSpacing = 8f;

    private Canvas _canvas;
    private bool _built = false;

    private void Start()
    {
        _canvas = GetComponent<Canvas>();
        if (_canvas == null)
        {
            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
        }

        // Use TrackedDeviceGraphicRaycaster for VR pointer interaction
        if (GetComponent<TrackedDeviceGraphicRaycaster>() == null)
            gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();
        // Remove standard GraphicRaycaster if present (doesn't work in VR)
        var standardRaycaster = GetComponent<GraphicRaycaster>();
        if (standardRaycaster != null && GetComponent<TrackedDeviceGraphicRaycaster>() != null)
            Destroy(standardRaycaster);

        if (scenarioManager == null)
            scenarioManager = FindAnyObjectByType<ScenarioManager>();

        if (GameManager.Instance != null)
            GameManager.Instance.OnStateChanged.AddListener(OnStateChanged);

        BuildMenu();
        Show();
    }

    private void OnDestroy()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnStateChanged.RemoveListener(OnStateChanged);
    }

    private void LateUpdate()
    {
        // Billboard: face the player
        if (Camera.main == null) return;
        transform.LookAt(Camera.main.transform);
        transform.Rotate(0f, 180f, 0f);
    }

    private void OnStateChanged(GameManager.GameState state)
    {
        if (state == GameManager.GameState.Menu)
            Show();
        else
            Hide();
    }

    private void Show()
    {
        if (_canvas != null) _canvas.enabled = true;
    }

    private void Hide()
    {
        if (_canvas != null) _canvas.enabled = false;
    }

    private void BuildMenu()
    {
        if (_built || scenarioManager == null) return;
        _built = true;

        var rt = GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(500, 400);

        // Background panel
        var bg = gameObject.AddComponent<Image>();
        bg.color = new Color(0.1f, 0.1f, 0.15f, 0.9f);

        // Title
        var titleGO = new GameObject("Title");
        titleGO.transform.SetParent(transform, false);
        var titleTMP = titleGO.AddComponent<TextMeshProUGUI>();
        titleTMP.text = "STOP IT !";
        titleTMP.fontSize = 36;
        titleTMP.fontStyle = FontStyles.Bold;
        titleTMP.color = new Color(1f, 0.85f, 0.2f);
        titleTMP.alignment = TextAlignmentOptions.Center;
        var titleRT = titleGO.GetComponent<RectTransform>();
        titleRT.anchorMin = new Vector2(0, 1);
        titleRT.anchorMax = new Vector2(1, 1);
        titleRT.pivot = new Vector2(0.5f, 1);
        titleRT.anchoredPosition = new Vector2(0, -10);
        titleRT.sizeDelta = new Vector2(0, 50);

        // Subtitle
        var subGO = new GameObject("Subtitle");
        subGO.transform.SetParent(transform, false);
        var subTMP = subGO.AddComponent<TextMeshProUGUI>();
        subTMP.text = "Choisissez un scenario";
        subTMP.fontSize = 18;
        subTMP.color = Color.white;
        subTMP.alignment = TextAlignmentOptions.Center;
        var subRT = subGO.GetComponent<RectTransform>();
        subRT.anchorMin = new Vector2(0, 1);
        subRT.anchorMax = new Vector2(1, 1);
        subRT.pivot = new Vector2(0.5f, 1);
        subRT.anchoredPosition = new Vector2(0, -55);
        subRT.sizeDelta = new Vector2(0, 30);

        // Scenario buttons
        float yOffset = -95;
        for (int i = 0; i < scenarioManager.ScenarioCount; i++)
        {
            int idx = i; // capture for lambda
            string label = (i + 1) + ". " + scenarioManager.GetScenarioName(i);
            CreateButton(label, yOffset, () => LaunchScenario(idx));
            yOffset -= (buttonHeight + buttonSpacing);
        }

        // "Jouer tout" button
        yOffset -= 10;
        CreateButton(">>> JOUER TOUT <<<", yOffset, LaunchAll, new Color(0.2f, 0.7f, 0.3f));
    }

    private void CreateButton(string label, float yPos, UnityEngine.Events.UnityAction onClick, Color? color = null)
    {
        var btnGO = new GameObject("Btn_" + label.Substring(0, Mathf.Min(label.Length, 15)));
        btnGO.transform.SetParent(transform, false);

        var img = btnGO.AddComponent<Image>();
        img.color = color ?? new Color(0.25f, 0.25f, 0.35f);

        var btn = btnGO.AddComponent<Button>();
        btn.onClick.AddListener(onClick);

        // Hover color
        var colors = btn.colors;
        colors.highlightedColor = new Color(0.4f, 0.4f, 0.6f);
        colors.pressedColor = new Color(0.6f, 0.6f, 0.8f);
        btn.colors = colors;

        var btnRT = btnGO.GetComponent<RectTransform>();
        btnRT.anchorMin = new Vector2(0.05f, 1);
        btnRT.anchorMax = new Vector2(0.95f, 1);
        btnRT.pivot = new Vector2(0.5f, 1);
        btnRT.anchoredPosition = new Vector2(0, yPos);
        btnRT.sizeDelta = new Vector2(0, buttonHeight);

        // Text
        var txtGO = new GameObject("Text");
        txtGO.transform.SetParent(btnGO.transform, false);
        var tmp = txtGO.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 16;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;
        var txtRT = txtGO.GetComponent<RectTransform>();
        txtRT.anchorMin = Vector2.zero;
        txtRT.anchorMax = Vector2.one;
        txtRT.sizeDelta = Vector2.zero;
    }

    private void LaunchScenario(int index)
    {
        scenarioManager.SetNextScenario(index);
        GameManager.Instance.LaunchSingle();
    }

    private void LaunchAll()
    {
        scenarioManager.SetNextScenario(0);
        GameManager.Instance.LaunchAll();
    }
}
