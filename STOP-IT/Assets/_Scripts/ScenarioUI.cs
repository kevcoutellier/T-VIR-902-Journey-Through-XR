using TMPro;
using UnityEngine;

/// <summary>
/// STOP IT! — ScenarioUI
/// World-space canvas that shows the timer, scenario name, and result feedback.
/// Attach to a Canvas in the scene. The canvas should face the player at all times.
/// </summary>
public class ScenarioUI : MonoBehaviour
{
    [Header("References")]
    public TextMeshProUGUI timerText;
    public TextMeshProUGUI scenarioNameText;
    public TextMeshProUGUI feedbackText;
    public TextMeshProUGUI scoreText;

    [Header("Settings")]
    [Tooltip("Name/description shown for this scenario")]
    public string scenarioName = "Salon — La prise électrique";

    [Tooltip("Feedback messages")]
    public string successMessage = "BIEN JOUÉ ! ✓";
    public string failMessage = "TROP TARD ! ✗";
    public string gameOverMessage = "FIN DE PARTIE";

    [Header("Colors")]
    public Color normalTimerColor = Color.white;
    public Color urgentTimerColor = Color.red;
    [Tooltip("Timer goes red when below this threshold (seconds)")]
    public float urgentThreshold = 8f;

    // ── Unity ──────────────────────────────────────────────────────────────
    private void Start()
    {
        if (scenarioNameText) scenarioNameText.text = scenarioName;
        if (feedbackText) feedbackText.text = string.Empty;
        if (timerText) timerText.color = normalTimerColor;

        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnTimerUpdated.AddListener(OnTimerUpdated);
            GameManager.Instance.OnStateChanged.AddListener(OnStateChanged);
            GameManager.Instance.OnScoreUpdated.AddListener(OnScoreUpdated);
        }
    }

    private void OnDestroy()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnTimerUpdated.RemoveListener(OnTimerUpdated);
            GameManager.Instance.OnStateChanged.RemoveListener(OnStateChanged);
            GameManager.Instance.OnScoreUpdated.RemoveListener(OnScoreUpdated);
        }
    }

    // Billboard: always face the camera
    private void LateUpdate()
    {
        if (Camera.main == null) return;
        transform.LookAt(Camera.main.transform);
        transform.Rotate(0f, 180f, 0f);
    }

    // ── Event handlers ─────────────────────────────────────────────────────
    private void OnTimerUpdated(float remaining)
    {
        if (timerText == null) return;
        int secs = Mathf.CeilToInt(remaining);
        timerText.text = $"{secs:00}";
        timerText.color = remaining <= urgentThreshold ? urgentTimerColor : normalTimerColor;
    }

    private void OnStateChanged(GameManager.GameState state)
    {
        // Hide HUD when in menu
        bool showHUD = state != GameManager.GameState.Menu;
        if (timerText) timerText.gameObject.SetActive(showHUD);
        if (scenarioNameText) scenarioNameText.gameObject.SetActive(showHUD);
        if (scoreText) scoreText.gameObject.SetActive(showHUD);
        if (feedbackText) feedbackText.gameObject.SetActive(showHUD);

        if (feedbackText == null) return;
        switch (state)
        {
            case GameManager.GameState.Menu:
                break;
            case GameManager.GameState.Playing:
                feedbackText.text = string.Empty;
                if (timerText) timerText.color = normalTimerColor;
                break;
            case GameManager.GameState.Success:
                feedbackText.text = successMessage;
                feedbackText.color = Color.green;
                break;
            case GameManager.GameState.Fail:
                feedbackText.text = failMessage;
                feedbackText.color = Color.red;
                break;
            case GameManager.GameState.GameOver:
                feedbackText.text = gameOverMessage;
                feedbackText.color = Color.yellow;
                if (timerText) timerText.text = "00";
                break;
        }
    }

    private void OnScoreUpdated(int current, int total)
    {
        if (scoreText) scoreText.text = $"{current} / {total}";
    }

    /// <summary>Called by ScenarioManager to update the displayed scenario name.</summary>
    public void SetScenarioName(string name)
    {
        scenarioName = name;
        if (scenarioNameText) scenarioNameText.text = name;
    }
}
