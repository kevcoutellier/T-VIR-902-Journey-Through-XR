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
    public TextMeshProUGUI actionHintText;

    [Header("Action Hint")]
    [Tooltip("Legacy — le bandeau d'objectif reste désormais affiché toute la manche (plus de fondu auto). " +
             "Conservé pour ne pas casser la sérialisation des scènes existantes.")]
    public float hintHoldDuration = 2f;
    [Tooltip("Legacy — plus utilisé (le bandeau ne se fond plus). Conservé pour la sérialisation.")]
    public float hintFadeDuration = 0.6f;
    [Tooltip("Durée du petit 'pop' d'entrée du bandeau d'objectif (s).")]
    public float hintPopDuration = 0.35f;
    public Color hintColor = new Color(0.2f, 0.95f, 1f);

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
    private Vector3 _hintBaseScale = Vector3.one;
    private Coroutine _feedbackBounce;
    private Coroutine _hintRoutine;

    // ── Unity ──────────────────────────────────────────────────────────────
    private void Start()
    {
        if (scenarioNameText) scenarioNameText.text = scenarioName;
        if (feedbackText) feedbackText.text = string.Empty;
        if (actionHintText)
        {
            var c = hintColor; c.a = 0f;
            actionHintText.color = c;
            actionHintText.text = string.Empty;
            _hintBaseScale = actionHintText.rectTransform.localScale;
        }
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
        // Hide HUD when in menu
        bool showHUD = state != GameManager.GameState.Menu;
        if (timerText) timerText.gameObject.SetActive(showHUD);
        if (scenarioNameText) scenarioNameText.gameObject.SetActive(showHUD);
        if (scoreText) scoreText.gameObject.SetActive(showHUD);
        if (feedbackText) feedbackText.gameObject.SetActive(showHUD);
        if (actionHintText) actionHintText.gameObject.SetActive(showHUD);

        // Stop any running hint fade as soon as the round resolves —
        // we don't want the cyan verb lingering on top of the success/fail message.
        if (state == GameManager.GameState.Success || state == GameManager.GameState.Fail
            || state == GameManager.GameState.GameOver || state == GameManager.GameState.Menu)
        {
            HideActionHint();
        }

        if (feedbackText == null) return;
        switch (state)
        {
            case GameManager.GameState.Menu:
                break;
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
                if (timerText) { timerText.text = "00"; timerText.color = urgentTimerColor; }
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

    /// <summary>Called by ScenarioManager to update the displayed scenario name.</summary>
    public void SetScenarioName(string name)
    {
        scenarioName = name;
        if (scenarioNameText) scenarioNameText.text = name;
    }

    /// <summary>
    /// Bandeau d'objectif PERSISTANT. Appelé par ScenarioManager au démarrage du
    /// scénario (avec le verbe, ou "" pour les scénarios à interception où le verbe
    /// n'est révélé que plus tard par ChildNPC.ArmCatch), puis à nouveau par ArmCatch
    /// avec le vrai verbe. Le texte reste affiché TOUTE la manche (petit 'pop' d'entrée,
    /// pas de fondu) ; il n'est effacé qu'en quittant l'état Playing (HideActionHint).
    ///
    /// Quand aucun verbe n'est encore connu (hint vide), on retombe sur l'objectif
    /// générique du scénario courant (<see cref="ScenarioManager.CurrentObjectiveFallback"/>)
    /// pour que le bandeau ne soit JAMAIS vide pendant Playing.
    /// </summary>
    public void SetActionHint(string hint)
    {
        if (actionHintText == null) return;

        string text = string.IsNullOrEmpty(hint) ? ResolveFallbackObjective() : hint;

        if (_hintRoutine != null) StopCoroutine(_hintRoutine);
        _hintRoutine = StartCoroutine(ActionHintRoutine(text));
    }

    /// <summary>Objectif générique tant que le verbe d'action n'est pas révélé (S1 fourchette / S4 skate).</summary>
    private string ResolveFallbackObjective()
    {
        if (ScenarioManager.Instance != null)
            return ScenarioManager.Instance.CurrentObjectiveFallback;
        return "Objectif : surveille l'enfant";
    }

    public void HideActionHint()
    {
        if (actionHintText == null) return;
        if (_hintRoutine != null) { StopCoroutine(_hintRoutine); _hintRoutine = null; }
        var c = hintColor; c.a = 0f;
        actionHintText.color = c;
        actionHintText.text = string.Empty;
        actionHintText.rectTransform.localScale = _hintBaseScale;
    }

    private IEnumerator ActionHintRoutine(string hint)
    {
        actionHintText.text = hint;
        var full = hintColor; full.a = 1f;
        actionHintText.color = full;

        // Emphase d'entrée : petit 'pop' d'échelle, puis on RESTE affiché (pas de fondu).
        // Le bandeau ne disparaît qu'en quittant l'état Playing (HideActionHint).
        var rt = actionHintText.rectTransform;
        float t = 0f;
        float pop = Mathf.Max(0.01f, hintPopDuration);
        while (t < pop)
        {
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / pop);
            float eased = 1f - Mathf.Pow(1f - u, 3f);          // ease-out cubic
            float overshoot = 1f + 0.12f * Mathf.Sin(u * Mathf.PI); // petit rebond
            rt.localScale = _hintBaseScale * (Mathf.Lerp(0.6f, 1f, eased) * overshoot);
            yield return null;
        }
        rt.localScale = _hintBaseScale;
        _hintRoutine = null;
    }
}
