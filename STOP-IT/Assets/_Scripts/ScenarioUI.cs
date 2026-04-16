using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// STOP IT! — ScenarioUI
/// World-space canvas showing timer, scenario name, score, feedback.
///
/// UX upgrades:
///  • Timer pulses (scale + color) when urgent threshold is reached.
///  • Feedback text bounces in on state change (success/fail).
///  • Billboarding toward the player camera.
/// </summary>
public class ScenarioUI : MonoBehaviour
{
    [Header("References")]
    public TextMeshProUGUI timerText;
    public TextMeshProUGUI scenarioNameText;
    public TextMeshProUGUI feedbackText;
    public TextMeshProUGUI scoreText;

    [Header("Settings")]
    public string scenarioName = "Salon — La prise électrique";
    public string successMessage = "BIEN JOUÉ ! ✓";
    public string failMessage = "TROP TARD ! ✗";
    public string gameOverMessage = "FIN DE PARTIE";

    [Header("Colors")]
    public Color normalTimerColor = Color.white;
    public Color urgentTimerColor = new Color(1f, 0.2f, 0.2f);
    public float urgentThreshold = 8f;

    [Header("Animation")]
    public float urgentPulseFrequency = 3f;
    public float urgentPulseAmplitude = 0.15f;

    private Vector3 _timerBaseScale = Vector3.one;
    private Coroutine _feedbackBounce;

    // ── Unity ──────────────────────────────────────────────────────────────
    private void Start()
    {
        if (scenarioNameText) scenarioNameText.text = scenarioName;
        if (feedbackText) feedbackText.text = string.Empty;
        if (timerText)
        {
            timerText.color = normalTimerColor;
            _timerBaseScale = timerText.rectTransform.localScale;
        }

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
        Vector3 toCam = Camera.main.transform.position - transform.position;
        toCam.y = 0f;
        if (toCam.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(-toCam, Vector3.up);
    }

    // ── Event handlers ─────────────────────────────────────────────────────
    private void OnTimerUpdated(float remaining)
    {
        if (timerText == null) return;
        int secs = Mathf.CeilToInt(remaining);
        timerText.text = $"{secs:00}";

        bool urgent = remaining <= urgentThreshold;
        timerText.color = urgent ? urgentTimerColor : normalTimerColor;

        if (urgent)
        {
            float pulse = 1f + Mathf.Abs(Mathf.Sin(Time.time * Mathf.PI * urgentPulseFrequency)) * urgentPulseAmplitude;
            timerText.rectTransform.localScale = _timerBaseScale * pulse;
        }
        else
        {
            timerText.rectTransform.localScale = _timerBaseScale;
        }
    }

    private void OnStateChanged(GameManager.GameState state)
    {
        if (feedbackText == null) return;
        switch (state)
        {
            case GameManager.GameState.Playing:
                feedbackText.text = string.Empty;
                if (timerText)
                {
                    timerText.color = normalTimerColor;
                    timerText.rectTransform.localScale = _timerBaseScale;
                }
                break;
            case GameManager.GameState.Success:
                BounceFeedback(successMessage, new Color(0.2f, 1f, 0.4f));
                break;
            case GameManager.GameState.Fail:
                BounceFeedback(failMessage, new Color(1f, 0.25f, 0.25f));
                break;
            case GameManager.GameState.GameOver:
                BounceFeedback(gameOverMessage, new Color(1f, 0.9f, 0.2f));
                if (timerText) timerText.text = "00";
                break;
        }
    }

    private void OnScoreUpdated(int current, int total)
    {
        if (scoreText) scoreText.text = $"{current} / {total}";
    }

    private void BounceFeedback(string message, Color color)
    {
        if (feedbackText == null) return;
        feedbackText.text = message;
        feedbackText.color = color;
        if (_feedbackBounce != null) StopCoroutine(_feedbackBounce);
        _feedbackBounce = StartCoroutine(FeedbackBounceRoutine());
    }

    private IEnumerator FeedbackBounceRoutine()
    {
        var rt = feedbackText.rectTransform;
        float elapsed = 0f;
        const float duration = 0.5f;
        Vector3 startScale = Vector3.one * 0.2f;
        Vector3 endScale = Vector3.one * 1f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            float eased = 1f - Mathf.Pow(1f - t, 4f); // ease-out quart
            // Overshoot a bit to feel punchy
            float overshoot = 1f + 0.15f * Mathf.Sin(t * Mathf.PI);
            rt.localScale = Vector3.Lerp(startScale, endScale * overshoot, eased);
            yield return null;
        }
        rt.localScale = endScale;
        _feedbackBounce = null;
    }
}
