using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// STOP IT! — StoryEndScreen
/// Full-screen BLACK end card shown by StoryModeDirector when the story concludes at the
/// final scenario (the window):
///   • Victory (window closed in time): the home-safety prevention message scrolls up like
///     credits — slow enough to read.
///   • Defeat (the child fell): the scenario's prevention message is held, centred.
/// When the scroll/hold finishes (or the player skips with E / Space / click), it invokes the
/// onDone callback — the director then returns to the menu.
///
/// The GameObject stays ACTIVE; visibility is driven by the CanvasGroup alpha so the
/// coroutine keeps running and Show() can re-display it reliably.
/// </summary>
public class StoryEndScreen : MonoBehaviour
{
    [SerializeField] private CanvasGroup group;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text bodyText;
    [SerializeField] private RectTransform bodyScroll; // the rect that scrolls (holds bodyText)

    [Tooltip("Fade in/out duration (s).")]
    public float fadeDuration = 0.6f;
    [Tooltip("Scroll speed (reference px/s) for the victory credits — low = slow / readable.")]
    public float scrollSpeed = 90f;
    [Tooltip("Hold time (s) for a non-scrolling (defeat) message.")]
    public float holdDuration = 6f;
    [Tooltip("Ignore skip input for this long so a held key doesn't skip instantly.")]
    public float minDisplay = 1.5f;

    private Action _onDone;
    private float _shownAt;

    private void Awake()
    {
        if (group == null) group = GetComponent<CanvasGroup>();
        if (group) { group.alpha = 0f; group.blocksRaycasts = false; }
    }

    /// <summary>Show the end card. scroll=true → the body scrolls up (victory); false → held centred (defeat).</summary>
    public void Show(string title, Color titleColor, string body, bool scroll, Action onDone)
    {
        _onDone = onDone;
        if (titleText) { titleText.text = title; titleText.color = titleColor; }
        if (bodyText)  bodyText.text = body;
        if (group) group.blocksRaycasts = true;
        _shownAt = Time.unscaledTime;
        StopAllCoroutines();
        StartCoroutine(Run(scroll));
    }

    private IEnumerator Run(bool scroll)
    {
        yield return Fade(1f);

        float screenH = ((RectTransform)transform).rect.height;
        if (bodyText) bodyText.ForceMeshUpdate();
        float textH = bodyText != null ? Mathf.Max(bodyText.preferredHeight, 200f) : 600f;
        if (bodyScroll != null) bodyScroll.sizeDelta = new Vector2(bodyScroll.sizeDelta.x, textH);

        if (scroll && bodyScroll != null)
        {
            // Credits scroll: from fully below the screen to fully above it.
            float startY = -(screenH * 0.5f) - (textH * 0.5f);
            float endY   =  (screenH * 0.5f) + (textH * 0.5f);
            float y = startY;
            while (y < endY)
            {
                y += scrollSpeed * Time.unscaledDeltaTime;
                bodyScroll.anchoredPosition = new Vector2(bodyScroll.anchoredPosition.x, y);
                if (CanSkip() && SkipPressed()) break;
                yield return null;
            }
        }
        else
        {
            if (bodyScroll != null) bodyScroll.anchoredPosition = new Vector2(bodyScroll.anchoredPosition.x, 0f); // centred
            float t = 0f;
            while (t < holdDuration) { t += Time.unscaledDeltaTime; if (CanSkip() && SkipPressed()) break; yield return null; }
        }

        yield return Fade(0f);
        if (group) group.blocksRaycasts = false;
        var cb = _onDone; _onDone = null;
        cb?.Invoke();
    }

    private bool CanSkip() => Time.unscaledTime - _shownAt >= minDisplay;

    private bool SkipPressed()
    {
        var kb = Keyboard.current;
        if (kb != null && (kb.eKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame)) return true;
        var mouse = Mouse.current;
        return mouse != null && mouse.leftButton.wasPressedThisFrame;
    }

    private IEnumerator Fade(float target)
    {
        if (group == null) yield break;
        float start = group.alpha, t = 0f;
        while (t < fadeDuration)
        {
            t += Time.unscaledDeltaTime;
            group.alpha = Mathf.Lerp(start, target, t / fadeDuration);
            yield return null;
        }
        group.alpha = target;
    }
}
