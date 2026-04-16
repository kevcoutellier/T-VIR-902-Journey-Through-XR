using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// STOP IT! — ScenarioIntroCountdown
/// Shows a big "3 … 2 … 1 … GO !" before the NPC starts walking.
/// Freezes the ChildNPC's startDelay while the countdown runs, then releases it.
/// Attach to the ScenarioCanvas (or any world-space canvas).
/// </summary>
public class ScenarioIntroCountdown : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Big TMP text used for the countdown. Auto-created under this transform if null.")]
    public TextMeshProUGUI countdownText;
    public ChildNPC child;

    [Header("Timing")]
    public float stepDuration = 0.8f;
    public float goHoldDuration = 0.6f;

    [Header("Content")]
    public string[] steps = { "3", "2", "1", "GO !" };

    private void Awake()
    {
        if (countdownText == null) countdownText = CreateCountdownText();
        countdownText.text = string.Empty;
    }

    private void OnEnable()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnStateChanged.AddListener(OnStateChanged);
    }

    private void OnDisable()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnStateChanged.RemoveListener(OnStateChanged);
    }

    private void Start()
    {
        if (child == null) child = FindAnyObjectByType<ChildNPC>();
        StartCoroutine(PlayCountdown());
    }

    private void OnStateChanged(GameManager.GameState state)
    {
        if (state == GameManager.GameState.Playing && gameObject.activeInHierarchy)
            StartCoroutine(PlayCountdown());
    }

    private IEnumerator PlayCountdown()
    {
        // Freeze child walk start by temporarily bumping its startDelay.
        float prevDelay = 0f;
        if (child != null)
        {
            prevDelay = child.startDelay;
            // add enough delay so the countdown fully plays
            child.startDelay = (steps.Length - 1) * stepDuration + goHoldDuration + 0.1f;
        }

        countdownText.gameObject.SetActive(true);

        for (int i = 0; i < steps.Length; i++)
        {
            bool isLast = i == steps.Length - 1;
            yield return StartCoroutine(AnimateStep(steps[i], isLast ? goHoldDuration : stepDuration, isLast));
        }

        countdownText.text = string.Empty;
        countdownText.gameObject.SetActive(false);

        if (child != null) child.startDelay = prevDelay;
    }

    private IEnumerator AnimateStep(string label, float duration, bool isGo)
    {
        countdownText.text = label;
        countdownText.color = isGo ? new Color(0.2f, 1f, 0.3f) : Color.white;

        float elapsed = 0f;
        Vector3 startScale = Vector3.one * 0.6f;
        Vector3 endScale = Vector3.one * 1.4f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            // ease-out + fade
            float scaleT = 1f - Mathf.Pow(1f - t, 3f);
            countdownText.rectTransform.localScale = Vector3.Lerp(startScale, endScale, scaleT);
            Color c = countdownText.color;
            c.a = isGo ? (t < 0.5f ? 1f : Mathf.Lerp(1f, 0f, (t - 0.5f) / 0.5f))
                        : Mathf.Lerp(1f, 0f, t);
            countdownText.color = c;
            yield return null;
        }
    }

    private TextMeshProUGUI CreateCountdownText()
    {
        var go = new GameObject("CountdownText");
        go.transform.SetParent(transform, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.fontSize = 160;
        tmp.alignment = TMPro.TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.fontStyle = FontStyles.Bold;
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(0, 0);
        rt.sizeDelta = new Vector2(600, 300);
        return tmp;
    }
}
