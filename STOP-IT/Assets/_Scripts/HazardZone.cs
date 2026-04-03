using System.Collections;
using UnityEngine;

/// <summary>
/// STOP IT! — HazardZone
/// A dangerous spot (electrical outlet, microwave, window…).
/// Glows red when the child gets close. Reports fail when triggered.
/// </summary>
public class HazardZone : MonoBehaviour
{
    // ── Inspector ──────────────────────────────────────────────────────────
    [Header("Identity")]
    public string hazardName = "Electrical Outlet";

    [Header("Warning")]
    [Tooltip("Distance at which the hazard starts pulsing")]
    public float warningRadius = 2f;

    [Tooltip("Material to pulse when child is approaching")]
    public Renderer hazardRenderer;

    [Header("Colors")]
    public Color normalColor = Color.yellow;
    public Color warningColor = Color.red;

    // ── Runtime ────────────────────────────────────────────────────────────
    private bool _triggered = false;
    private Transform _childTransform;
    private Coroutine _pulseCoroutine;
    private static readonly int ColorProp = Shader.PropertyToID("_BaseColor");

    // ── Unity ──────────────────────────────────────────────────────────────
    private void Start()
    {
        SetColor(normalColor);

        var child = FindFirstObjectByType<ChildNPC>();
        if (child != null) _childTransform = child.transform;

        if (GameManager.Instance != null)
            GameManager.Instance.OnStateChanged.AddListener(OnGameStateChanged);
    }

    private void OnDestroy()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnStateChanged.RemoveListener(OnGameStateChanged);
    }

    private void Update()
    {
        if (_triggered || _childTransform == null) return;

        float dist = Vector3.Distance(transform.position, _childTransform.position);
        if (dist <= warningRadius && _pulseCoroutine == null)
            _pulseCoroutine = StartCoroutine(PulseRoutine());
        else if (dist > warningRadius && _pulseCoroutine != null)
        {
            StopCoroutine(_pulseCoroutine);
            _pulseCoroutine = null;
            SetColor(normalColor);
        }
    }

    // ── Public API ─────────────────────────────────────────────────────────
    /// <summary>Called by ChildNPC when it arrives here.</summary>
    public void TriggerHazard()
    {
        if (_triggered) return;
        _triggered = true;

        if (_pulseCoroutine != null) { StopCoroutine(_pulseCoroutine); _pulseCoroutine = null; }
        SetColor(warningColor);

        StartCoroutine(TriggerSequence());
    }

    // ── Private ────────────────────────────────────────────────────────────
    private IEnumerator TriggerSequence()
    {
        // Flash quickly 3×
        for (int i = 0; i < 6; i++)
        {
            SetColor(i % 2 == 0 ? warningColor : normalColor);
            yield return new WaitForSeconds(0.1f);
        }
        SetColor(warningColor);
        GameManager.Instance?.ReportFail();
    }

    private IEnumerator PulseRoutine()
    {
        float t = 0f;
        while (true)
        {
            t += Time.deltaTime * 3f;
            Color c = Color.Lerp(normalColor, warningColor, (Mathf.Sin(t) + 1f) * 0.5f);
            SetColor(c);
            yield return null;
        }
    }

    private void SetColor(Color c)
    {
        if (hazardRenderer == null) return;
        // Works with URP lit materials
        var mpb = new MaterialPropertyBlock();
        hazardRenderer.GetPropertyBlock(mpb);
        mpb.SetColor(ColorProp, c);
        hazardRenderer.SetPropertyBlock(mpb);
    }

    private void OnGameStateChanged(GameManager.GameState state)
    {
        if (state == GameManager.GameState.Playing)
        {
            _triggered = false;
            SetColor(normalColor);
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, warningRadius);
    }
}
