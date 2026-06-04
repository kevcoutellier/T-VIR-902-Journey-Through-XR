using UnityEngine;

/// <summary>
/// STOP IT! — WindowCloser (Scenario 5 — pigeon room / south window)
///
/// New win condition: instead of grabbing the baby, the player slams the window
/// shut before the toddler can climb out after the pigeon. Pairs with the
/// <see cref="WindowOpener"/> that the child uses to open the same window — attach
/// this alongside it (same GameObject).
///
/// • Arms only while the pigeon-window scenario is active (its hazard is the
///   active scenario's hazard), so it's inert elsewhere.
/// • While armed it shows a floating prompt once the player camera is within
///   <see cref="interactionRadius"/> of the window panel.
/// • The interact press (desktop "E" / VR the 4 triggers held together) is routed
///   here through IProximityInteractable: close the window → scenario won.
///
/// Disabling the baby-grab is handled by ScenarioManager flipping
/// ChildNPC.canBeSavedDirectly — this component only adds the new verb.
/// </summary>
[RequireComponent(typeof(WindowOpener))]
public class WindowCloser : MonoBehaviour, IProximityInteractable
{
    [Header("Interaction")]
    [Tooltip("Distance (metres) from the player camera at which the window can be closed.")]
    public float interactionRadius = 2.5f;

    [Tooltip("Use {KEY} as a placeholder for the active input ('E' desktop / 'Presse les 4 gâchettes' VR).")]
    public string promptText = "{KEY} pour fermer la fenêtre";

    [Tooltip("Local offset of the prompt relative to the window panel.")]
    public Vector3 promptOffset = new Vector3(0f, 0.5f, 0f);

    [Header("Success")]
    [Tooltip("Hazard that arms this closer (auto-found by name if null). Also neutralised on success.")]
    public HazardZone targetHazard;
    [Tooltip("Hazard GameObject name searched when targetHazard is null.")]
    public string targetHazardName = "HazardZone_PigeonWindow";

    [Header("Testing")]
    [Tooltip("Sandbox helper: arm even when no ScenarioManager is present.")]
    public bool forceArmed = false;

    private WindowOpener _opener;
    private Transform _anchor;     // window panel (fallback: this transform)
    private bool _armed;
    private bool _closed;
    private GameObject _prompt;

    public bool IsArmed => _armed && !_closed;

    private void Awake()
    {
        _opener = GetComponent<WindowOpener>();
        _anchor = (_opener != null && _opener.windowPanel != null) ? _opener.windowPanel : transform;
    }

    private void OnEnable()  => ProximityInteractables.Register(this);
    private void OnDisable()
    {
        ProximityInteractables.Unregister(this);
        if (_prompt != null) _prompt.SetActive(false);
    }

    private void Start()
    {
        _prompt = ProximityPrompt.Build("WindowCloser_Prompt", InputHints.ResolvePrompt(promptText));
        _armed = forceArmed;

        if (targetHazard == null && !string.IsNullOrEmpty(targetHazardName))
        {
            var go = GameObject.Find(targetHazardName);
            if (go != null) targetHazard = go.GetComponent<HazardZone>();
        }

        if (GameManager.Instance != null)
            GameManager.Instance.OnStateChanged.AddListener(OnGameStateChanged);

        if (ScenarioManager.Instance != null)
        {
            ScenarioManager.Instance.OnScenarioActivated.AddListener(OnScenarioActivated);
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
        // Armed only for the scenario whose hazard is the pigeon window.
        _armed = forceArmed || (cfg != null && targetHazard != null && cfg.hazardZone == targetHazard);
    }

    private void OnGameStateChanged(GameManager.GameState state)
    {
        if (state == GameManager.GameState.Playing)
            _closed = false; // new round — window starts closed, WindowOpener resets it
        else if (_prompt != null)
            _prompt.SetActive(false);
    }

    private void Update()
    {
        if (_prompt == null || _anchor == null) return;
        var cam = Camera.main;
        if (cam == null) { _prompt.SetActive(false); return; }

        bool show = IsArmed
                 && GameManager.Instance != null
                 && GameManager.Instance.State == GameManager.GameState.Playing
                 && Vector3.Distance(cam.transform.position, _anchor.position) <= interactionRadius;

        _prompt.SetActive(show);
        if (show)
            ProximityPrompt.Face(_prompt, _anchor.position + promptOffset, cam.transform.position);
    }

    // ── IProximityInteractable ───────────────────────────────────────────────
    public bool TryInteract(Vector3 cameraPosition)
    {
        if (!IsArmed || _anchor == null) return false;
        if (Vector3.Distance(cameraPosition, _anchor.position) > interactionRadius) return false;
        CloseWindow();
        return true;
    }

    private void CloseWindow()
    {
        _closed = true;
        if (_prompt != null) _prompt.SetActive(false);

        // Stop the opening animation and snap the panel shut.
        if (_opener != null) _opener.CloseNow();

        // Neutralise the ledge hazard so a same-frame arrival can't flash a fail,
        // then report the save (which also stops the child via the state change).
        if (targetHazard != null) targetHazard.MarkNeutralised();

        Debug.Log("[WindowCloser] Window slammed shut — scenario won.", this);
        GameManager.Instance?.ReportSuccess();
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Transform a = (_opener != null && _opener.windowPanel != null) ? _opener.windowPanel : transform;
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.5f);
        Gizmos.DrawWireSphere(a.position, interactionRadius);
    }
#endif
}
