using System.Collections;
using UnityEngine;

/// <summary>
/// STOP IT! — HazardZone
/// A dangerous spot (electrical outlet, microwave, window…).
///
/// UX upgrades:
///  • Emissive intensity scales with child proximity (0 → 1).
///  • Spark particles auto-generated, emit rate scales with proximity.
///  • Danger hum audio (AudioSource volume tied to proximity).
///  • Electric-zap VFX on fail (burst of sparks + flash).
///  • Confetti VFX on success.
/// </summary>
public class HazardZone : MonoBehaviour
{
    // ── Inspector ──────────────────────────────────────────────────────────
    [Header("Identity")]
    public string hazardName = "Electrical Outlet";

    [Header("Warning")]
    [Tooltip("Distance at which the hazard starts reacting to the child")]
    public float warningRadius = 1.8f;

    [Tooltip("Renderer whose material we pulse. Defaults to first Renderer on this GO.")]
    public Renderer hazardRenderer;

    [Header("Colors")]
    public Color normalColor = new Color(1f, 0.88f, 0.15f);   // jaune
    public Color warningColor = new Color(1f, 0.18f, 0.10f);  // rouge
    [Tooltip("Emissive strength at max proximity (HDR intensity)")]
    public float maxEmissive = 4f;

    [Header("Audio")]
    [Tooltip("Optional continuous hum played while the child is near.")]
    public AudioSource humSource;
    [Tooltip("Short zap clip on fail.")]
    public AudioClip zapClip;

    [Header("VFX")]
    [Tooltip("Optional particle system for danger sparks. Auto-created if null.")]
    public ParticleSystem sparks;
    [Tooltip("Optional particle system for success confetti. Auto-created if null.")]
    public ParticleSystem confetti;

    [Header("Events")]
    [Tooltip("Triggered when hazard detonates (fail).")]
    public UnityEngine.Events.UnityEvent OnHazardTriggered;

    // ── Runtime ────────────────────────────────────────────────────────────
    private bool _triggered = false;
    private ChildNPC _child;
    private static readonly int BaseColorProp = Shader.PropertyToID("_BaseColor");
    private static readonly int EmissionColorProp = Shader.PropertyToID("_EmissionColor");
    private MaterialPropertyBlock _mpb;
    private Material _runtimeMat;

    // ── Unity ──────────────────────────────────────────────────────────────
    private void Awake()
    {
        if (hazardRenderer == null) hazardRenderer = GetComponent<Renderer>();
        _mpb = new MaterialPropertyBlock();

        if (hazardRenderer != null)
        {
            // Enable emission keyword on the instance material (URP Lit).
            _runtimeMat = hazardRenderer.material;
            if (_runtimeMat != null)
            {
                _runtimeMat.EnableKeyword("_EMISSION");
                _runtimeMat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
        }

        EnsureSparks();
        EnsureConfetti();
    }

    private void Start()
    {
        SetVisualState(normalColor, 0f);
        _child = FindAnyObjectByType<ChildNPC>();

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
        if (_triggered)
            return;

        // Re-acquire child if it was respawned.
        if (_child == null) _child = FindAnyObjectByType<ChildNPC>();
        if (_child == null) return;

        float proximity = _child.GetHazardProximity01(warningRadius); // 0..1
        // Add a fast pulse on top when very close.
        float pulse = 0f;
        if (proximity > 0.2f)
            pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * (3f + proximity * 8f));
        float intensity = Mathf.Clamp01(proximity) * (0.6f + 0.4f * pulse);

        Color lerped = Color.Lerp(normalColor, warningColor, Mathf.Clamp01(proximity * 1.2f));
        SetVisualState(lerped, intensity);

        // Sparks emission scales with proximity
        if (sparks != null)
        {
            var emission = sparks.emission;
            emission.rateOverTime = Mathf.Lerp(0f, 40f, proximity);
            if (proximity > 0.15f && !sparks.isPlaying) sparks.Play();
            else if (proximity <= 0.05f && sparks.isPlaying) sparks.Stop();
        }

        // Hum audio volume
        if (humSource != null)
        {
            if (proximity > 0.05f)
            {
                if (!humSource.isPlaying) { humSource.loop = true; humSource.Play(); }
                humSource.volume = Mathf.Lerp(0f, 0.7f, proximity);
                humSource.pitch = Mathf.Lerp(0.9f, 1.4f, proximity);
            }
            else if (humSource.isPlaying)
            {
                humSource.volume = Mathf.MoveTowards(humSource.volume, 0f, Time.deltaTime * 2f);
                if (humSource.volume <= 0.01f) humSource.Stop();
            }
        }
    }

    // ── Public API ─────────────────────────────────────────────────────────
    /// <summary>Called by ChildNPC when it arrives here — fail.</summary>
    public void TriggerHazard()
    {
        if (_triggered) return;
        _triggered = true;
        OnHazardTriggered?.Invoke();
        StartCoroutine(TriggerSequence());
    }

    /// <summary>Called externally when the scenario is won.</summary>
    public void PlaySuccess()
    {
        if (confetti != null) confetti.Play();
    }

    // ── Private ────────────────────────────────────────────────────────────
    private IEnumerator TriggerSequence()
    {
        if (zapClip != null)
        {
            var src = humSource != null ? humSource : GetComponent<AudioSource>();
            if (src != null) src.PlayOneShot(zapClip);
        }

        // Intense spark burst
        if (sparks != null)
        {
            sparks.Emit(60);
        }

        // Fast red flash
        for (int i = 0; i < 8; i++)
        {
            SetVisualState(i % 2 == 0 ? warningColor : Color.white, 1f);
            yield return new WaitForSeconds(0.06f);
        }
        SetVisualState(warningColor, 0.8f);

        CameraShake.Shake(0.25f, 0.2f);
        GameManager.Instance?.ReportFail();
    }

    private void SetVisualState(Color baseColor, float emissive01)
    {
        if (hazardRenderer == null) return;
        hazardRenderer.GetPropertyBlock(_mpb);
        _mpb.SetColor(BaseColorProp, baseColor);
        Color emissionColor = baseColor * Mathf.LinearToGammaSpace(emissive01 * maxEmissive);
        _mpb.SetColor(EmissionColorProp, emissionColor);
        hazardRenderer.SetPropertyBlock(_mpb);
    }

    private void OnGameStateChanged(GameManager.GameState state)
    {
        switch (state)
        {
            case GameManager.GameState.Playing:
                _triggered = false;
                SetVisualState(normalColor, 0f);
                if (sparks != null) sparks.Stop();
                if (humSource != null && humSource.isPlaying) humSource.Stop();
                break;
            case GameManager.GameState.Success:
                PlaySuccess();
                break;
        }
    }

    // ── VFX auto-creation (so the scene "just works") ──────────────────────
    private void EnsureSparks()
    {
        if (sparks != null) return;
        var go = new GameObject("Sparks");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.zero;
        sparks = go.AddComponent<ParticleSystem>();
        var main = sparks.main;
        main.duration = 2f;
        main.loop = true;
        main.startLifetime = 0.4f;
        main.startSpeed = 1.5f;
        main.startSize = 0.03f;
        main.startColor = new Color(1f, 0.9f, 0.2f);
        main.gravityModifier = -0.5f;
        var emission = sparks.emission;
        emission.rateOverTime = 0f;
        var shape = sparks.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.08f;
        var renderer = sparks.GetComponent<ParticleSystemRenderer>();
        renderer.material = new Material(Shader.Find("Sprites/Default"));
        sparks.Stop();
    }

    private void EnsureConfetti()
    {
        if (confetti != null) return;
        var go = new GameObject("Confetti");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.up * 0.2f;
        confetti = go.AddComponent<ParticleSystem>();
        var main = confetti.main;
        main.duration = 1.2f;
        main.loop = false;
        main.startLifetime = 1.5f;
        main.startSpeed = 3f;
        main.startSize = 0.07f;
        main.gravityModifier = 1.2f;
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.2f, 1f, 0.4f), new Color(1f, 0.8f, 0.2f));
        var emission = confetti.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 60) });
        var shape = confetti.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 35f;
        shape.radius = 0.05f;
        var renderer = confetti.GetComponent<ParticleSystemRenderer>();
        renderer.material = new Material(Shader.Find("Sprites/Default"));
        confetti.Stop();
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.6f, 0f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, warningRadius);
    }
}
