using UnityEngine;

/// <summary>
/// STOP IT! — HazardIndicator
/// Floating cartoon chevron/arrow that hovers above the hazard and points down at it.
/// Bobs gently, scales with urgency (child proximity), and shifts from yellow → red.
/// Auto-creates a primitive mesh (cone) if no mesh is assigned.
/// </summary>
[ExecuteAlways]
public class HazardIndicator : MonoBehaviour
{
    [Header("References")]
    public HazardZone hazard;
    public Transform arrow;       // the visible arrow/cone — auto-created if null

    [Header("Shape")]
    public float heightAboveHazard = 0.9f;
    public float baseScale = 0.12f;
    public float pulseScale = 0.04f;
    public float bobAmplitude = 0.06f;
    public float bobFrequency = 1.5f;

    [Header("Colors")]
    public Color calmColor = new Color(1f, 0.85f, 0.2f);
    public Color alertColor = new Color(1f, 0.15f, 0.1f);

    private static readonly int BaseColorProp = Shader.PropertyToID("_BaseColor");
    private static readonly int EmissionColorProp = Shader.PropertyToID("_EmissionColor");
    private MaterialPropertyBlock _mpb;
    private Renderer _arrowRenderer;
    private ChildNPC _child;

    private void Awake()
    {
        _mpb = new MaterialPropertyBlock();
        EnsureArrow();
    }

    private void OnEnable()
    {
        if (hazard == null) hazard = GetComponent<HazardZone>();
        if (hazard == null) hazard = FindAnyObjectByType<HazardZone>();
        if (Application.isPlaying && ScenarioManager.Instance != null)
            ScenarioManager.Instance.OnScenarioActivated.AddListener(OnScenarioActivated);
    }

    private void OnDisable()
    {
        if (Application.isPlaying && ScenarioManager.Instance != null)
            ScenarioManager.Instance.OnScenarioActivated.RemoveListener(OnScenarioActivated);
    }

    private void OnScenarioActivated(ScenarioManager.ScenarioConfig cfg)
    {
        if (cfg != null && cfg.hazardZone != null)
        {
            hazard = cfg.hazardZone;
            // Re-acquire child reference so the new hazard's proximity reads correctly.
            _child = null;
        }
    }

    private void Update()
    {
        if (hazard == null || arrow == null) return;

        // Always above the hazard in world space (works if hazard or indicator moves).
        Vector3 basePos = hazard.transform.position + Vector3.up * heightAboveHazard;
        float bob = Mathf.Sin(Time.time * Mathf.PI * bobFrequency) * bobAmplitude;
        arrow.position = basePos + Vector3.up * bob;

        // Face camera horizontally, pointing down.
        var cam = Camera.main;
        if (cam != null)
        {
            Vector3 toCam = cam.transform.position - arrow.position;
            toCam.y = 0f;
            if (toCam.sqrMagnitude > 0.001f)
            {
                Quaternion yaw = Quaternion.LookRotation(toCam, Vector3.up);
                arrow.rotation = yaw * Quaternion.Euler(180f, 0f, 0f); // point down
            }
        }

        // Urgency from child proximity
        float proximity = 0f;
        if (Application.isPlaying)
        {
            if (_child == null) _child = FindAnyObjectByType<ChildNPC>();
            if (_child != null) proximity = _child.GetHazardProximity01(hazard.warningRadius);
        }

        float scale = baseScale + Mathf.Abs(Mathf.Sin(Time.time * (2f + 6f * proximity))) * pulseScale * (0.3f + proximity);
        arrow.localScale = Vector3.one * scale;

        if (_arrowRenderer != null)
        {
            _arrowRenderer.GetPropertyBlock(_mpb);
            Color c = Color.Lerp(calmColor, alertColor, proximity);
            _mpb.SetColor(BaseColorProp, c);
            _mpb.SetColor(EmissionColorProp, c * (0.5f + proximity * 2.5f));
            _arrowRenderer.SetPropertyBlock(_mpb);
        }
    }

    private void EnsureArrow()
    {
        if (arrow != null)
        {
            _arrowRenderer = arrow.GetComponent<Renderer>();
            return;
        }

        // Use a primitive cylinder squished into a chevron/cone shape.
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = "IndicatorArrow";
        var col = go.GetComponent<Collider>();
        if (col != null) DestroyImmediate(col);
        go.transform.SetParent(transform, false);

        // Scale to a down-pointing wedge (cone-like).
        go.transform.localScale = new Vector3(0.4f, 0.6f, 0.4f);

        // URP Lit material for emissive support.
        var mr = go.GetComponent<MeshRenderer>();
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        if (mat == null) mat = new Material(Shader.Find("Standard"));
        mat.EnableKeyword("_EMISSION");
        mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        mr.sharedMaterial = mat;

        arrow = go.transform;
        _arrowRenderer = mr;
    }
}
