using UnityEngine;

/// <summary>
/// STOP IT! — CatGrab (Scenario 2 — Kitchen / cat in the microwave)
///
/// New win condition: instead of grabbing the baby, the player snatches the cat
/// out of the toddler's hands before it reaches the microwave. Attach this to the
/// "Cat" GameObject (the one wired as scenario 2's carriedItem).
///
/// • Arms only while it is the active scenario's carried item — so it's inert in
///   every other scenario.
/// • While armed it shows a floating prompt as soon as the player camera is within
///   <see cref="interactionRadius"/> of the cat (which rides in the child's hands).
/// • The interact press (desktop "E" / VR the 4 triggers held together) is routed
///   here by ChildGrabber.Trigger() through IProximityInteractable: if the player
///   is close enough, the cat is taken and the scenario is won.
///
/// Disabling the baby-grab itself is handled by ScenarioManager flipping
/// ChildNPC.canBeSavedDirectly — this component only adds the new verb.
/// </summary>
public class CatGrab : MonoBehaviour, IProximityInteractable
{
    [Header("Interaction")]
    [Tooltip("Distance (metres) from the player camera at which the cat can be taken.")]
    public float interactionRadius = 1.5f;

    [Tooltip("Use {KEY} as a placeholder for the active input ('E' desktop / 'Presse les 4 gâchettes' VR).")]
    public string promptText = "{KEY} pour récupérer le chat";

    [Tooltip("Local offset of the prompt above the cat.")]
    public Vector3 promptOffset = new Vector3(0f, 0.35f, 0f);

    [Header("Success")]
    [Tooltip("Hazard to neutralise on success (auto = active scenario's hazard, e.g. the microwave).")]
    public HazardZone targetHazard;

    [Header("Testing")]
    [Tooltip("Sandbox helper: arm even when no ScenarioManager is present.")]
    public bool forceArmed = false;

    private bool _armed;
    private bool _taken;
    private Renderer[] _renderers;
    private GameObject _prompt;

    public bool IsArmed => _armed && !_taken;

    private void Awake()
    {
        _renderers = GetComponentsInChildren<Renderer>(includeInactive: true);
    }

    private void OnEnable()  => ProximityInteractables.Register(this);
    private void OnDisable()
    {
        ProximityInteractables.Unregister(this);
        if (_prompt != null) _prompt.SetActive(false);
    }

    private void Start()
    {
        _prompt = ProximityPrompt.Build("CatGrab_Prompt", InputHints.ResolvePrompt(promptText));
        _armed = forceArmed;

        if (GameManager.Instance != null)
            GameManager.Instance.OnStateChanged.AddListener(OnGameStateChanged);

        if (ScenarioManager.Instance != null)
        {
            ScenarioManager.Instance.OnScenarioActivated.AddListener(OnScenarioActivated);
            // Catch the scenario that may already be active when we spin up.
            OnScenarioActivated(ScenarioManager.Instance.CurrentScenario);
        }
    }

    private void OnDestroy()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnStateChanged.RemoveListener(OnGameStateChanged);
        if (ScenarioManager.Instance != null)
            ScenarioManager.Instance.OnScenarioActivated.RemoveListener(OnScenarioActivated);
    }

    private void OnScenarioActivated(ScenarioManager.ScenarioConfig cfg)
    {
        // We are the take-target only when we're this scenario's carried item.
        _armed = forceArmed || (cfg != null && cfg.carriedItem == gameObject);
        if (_armed && cfg != null && cfg.hazardZone != null && targetHazard == null)
            targetHazard = cfg.hazardZone;
    }

    private void OnGameStateChanged(GameManager.GameState state)
    {
        if (state == GameManager.GameState.Playing)
        {
            // New round: the cat is back in the child's hands.
            _taken = false;
            SetVisible(true);
        }
        else if (_prompt != null)
        {
            _prompt.SetActive(false);
        }
    }

    private void Update()
    {
        if (_prompt == null) return;
        var cam = Camera.main;
        if (cam == null) { _prompt.SetActive(false); return; }

        bool show = IsArmed
                 && GameManager.Instance != null
                 && GameManager.Instance.State == GameManager.GameState.Playing
                 && Vector3.Distance(cam.transform.position, transform.position) <= interactionRadius;

        _prompt.SetActive(show);
        if (show)
            ProximityPrompt.Face(_prompt, transform.position + promptOffset, cam.transform.position);
    }

    // ── IProximityInteractable ───────────────────────────────────────────────
    public bool TryInteract(Vector3 cameraPosition)
    {
        if (!IsArmed) return false;
        if (Vector3.Distance(cameraPosition, transform.position) > interactionRadius) return false;
        TakeCat();
        return true;
    }

    private void TakeCat()
    {
        _taken = true;
        SetVisible(false);
        if (_prompt != null) _prompt.SetActive(false);

        // Neutralise the microwave so a same-frame arrival can't flash a fail,
        // then report the save (which also stops the child via the state change).
        if (targetHazard != null) targetHazard.MarkNeutralised();

        Debug.Log("[CatGrab] Cat taken from the toddler — scenario won.", this);
        GameManager.Instance?.ReportSuccess();
    }

    private void SetVisible(bool on)
    {
        if (_renderers == null) return;
        for (int i = 0; i < _renderers.Length; i++)
            if (_renderers[i] != null) _renderers[i].enabled = on;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, interactionRadius);
    }
#endif
}
