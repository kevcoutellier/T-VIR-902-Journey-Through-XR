using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// STOP IT! — StoryLoseScreen
/// Full-screen "you lost" overlay shown by StoryModeDirector when the child completes a
/// mischief (e.g. gets electrocuted). Fades in a dark panel with a title, a home-safety
/// prevention message, and a "press to retry" prompt. Retries on E / click / Space, or
/// auto-retries after a timeout.
///
/// The GameObject stays ACTIVE the whole time; visibility is driven by the CanvasGroup
/// alpha (so coroutines/Update keep running and Show() can re-display it reliably).
/// </summary>
public class StoryLoseScreen : MonoBehaviour
{
    [SerializeField] private CanvasGroup group;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text messageText;
    [SerializeField] private TMP_Text promptText;

    [Tooltip("Fade in/out duration (s).")]
    public float fadeDuration = 0.4f;
    [Tooltip("Ignore input for this long after showing, so a held key doesn't skip it instantly.")]
    public float minDisplay = 1.0f;
    [Tooltip("Auto-retry if the player does nothing for this long (s).")]
    public float autoRetryAfter = 10f;

    private bool _active;
    private float _shownAt;
    private Action _onRetry;

    private void Awake()
    {
        if (group == null) group = GetComponent<CanvasGroup>();
        SetHidden();
    }

    public void Show(string title, string message, Action onRetry)
    {
        _onRetry = onRetry;
        if (titleText)   titleText.text = title;
        if (messageText) messageText.text = message;
        if (promptText)  promptText.text = InputHints.IsVRActive()
            ? "Presse une gâchette pour réessayer"
            : "Appuie sur E pour réessayer";

        _active = true;
        _shownAt = Time.unscaledTime;
        if (group) group.blocksRaycasts = true;
        StopAllCoroutines();
        StartCoroutine(FadeTo(1f));
    }

    public void HideImmediate()
    {
        StopAllCoroutines();
        SetHidden();
    }

    private void SetHidden()
    {
        _active = false;
        if (group) { group.alpha = 0f; group.blocksRaycasts = false; group.interactable = false; }
    }

    private void Update()
    {
        if (!_active) return;
        float elapsed = Time.unscaledTime - _shownAt;
        if (elapsed < minDisplay) return;

        bool retry = elapsed >= autoRetryAfter;

        var kb = Keyboard.current;
        if (kb != null && (kb.eKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame))
            retry = true;
        var mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            retry = true;

        if (retry)
        {
            _active = false;
            var cb = _onRetry;
            _onRetry = null;
            StopAllCoroutines();
            StartCoroutine(FadeOutThen(cb));
        }
    }

    private IEnumerator FadeTo(float target)
    {
        if (group == null) yield break;
        float start = group.alpha;
        float t = 0f;
        while (t < fadeDuration)
        {
            t += Time.unscaledDeltaTime;
            group.alpha = Mathf.Lerp(start, target, t / fadeDuration);
            yield return null;
        }
        group.alpha = target;
    }

    private IEnumerator FadeOutThen(Action done)
    {
        yield return FadeTo(0f);
        if (group) group.blocksRaycasts = false;
        done?.Invoke();
    }
}
