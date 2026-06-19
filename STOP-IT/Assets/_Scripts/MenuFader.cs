using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// STOP IT! — MenuFader
/// A simple comfort fade-to-black that works in both VR and on a 2D screen. It
/// builds a black quad on a World-Space canvas pinned just in front of the active
/// camera (so it covers both eyes in the HMD), and animates its alpha.
///
/// Used by <see cref="MenuCameraTour"/> in TeleportFade mode: fade out → snap the
/// rig to the next room → fade in. Because the rig only ever moves while the view
/// is black, the player never sees continuous motion → no VR sickness.
/// </summary>
[DefaultExecutionOrder(15)]
public class MenuFader : MonoBehaviour
{
    [Tooltip("Distance (m) in front of the camera the black quad sits.")]
    public float quadDistance = 0.35f;

    [Tooltip("Half-size of the quad (m) — large enough to cover the HMD field of view.")]
    public float quadHalfSize = 1.2f;

    private Canvas _canvas;
    private CanvasGroup _group;
    private Transform _cam;

    public bool IsBuilt => _canvas != null;

    private void Awake()
    {
        Build();
        SetAlphaImmediate(0f);
    }

    private void Build()
    {
        if (_canvas != null) return;

        var go = new GameObject("FadeQuad", typeof(RectTransform));
        go.transform.SetParent(transform, false);
        _canvas = go.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.WorldSpace;
        _canvas.sortingOrder = 200; // above the world-space menu

        _group = go.AddComponent<CanvasGroup>();
        _group.interactable = false;
        _group.blocksRaycasts = false;

        var img = go.AddComponent<Image>();
        img.color = Color.black;
        img.raycastTarget = false;

        var rt = (RectTransform)go.transform;
        rt.sizeDelta = new Vector2(quadHalfSize * 2f, quadHalfSize * 2f);
        rt.localScale = Vector3.one; // 1 unit = 1 metre in world space
    }

    private void LateUpdate()
    {
        // Keep the quad pinned in front of whatever camera is currently rendering.
        if (_cam == null)
        {
            var c = Camera.main;
            if (c != null) _cam = c.transform;
        }
        if (_cam == null || _canvas == null) return;

        var t = _canvas.transform;
        t.position = _cam.position + _cam.forward * quadDistance;
        t.rotation = _cam.rotation;
    }

    public void SetAlphaImmediate(float a)
    {
        if (_group != null) _group.alpha = Mathf.Clamp01(a);
    }

    /// <summary>Fade the screen toward <paramref name="target"/> alpha over <paramref name="dur"/> seconds.</summary>
    public IEnumerator FadeTo(float target, float dur)
    {
        if (_group == null) yield break;
        float start = _group.alpha;
        float t = 0f;
        dur = Mathf.Max(0.01f, dur);
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            _group.alpha = Mathf.Lerp(start, target, t / dur);
            yield return null;
        }
        _group.alpha = Mathf.Clamp01(target);
    }
}
