using System.Collections;
using UnityEngine;

/// <summary>
/// STOP IT! — WindowCloser (bedroom window scenario)
///
/// New win condition: the window is ALREADY OPEN (the toddler climbs out after the
/// pigeon). The player slams it shut — as a SLIDING pane — before the child escapes.
///
/// • Self-contained: drives a single sliding pane (no WindowOpener needed). The pane
///   starts displaced by <see cref="openOffset"/> (open) and slides back to its
///   authored pose (closed/covering) when the player closes it.
/// • Arms only while the window scenario is active (its hazard is the active
///   scenario's hazard), so it's inert elsewhere.
/// • Shows a floating prompt once the player camera is within
///   <see cref="interactionRadius"/> of the window opening.
/// • The interact press (desktop "E" / VR the 4 triggers held together) is routed
///   here through IProximityInteractable: close the window → scenario won.
///
/// Disabling the baby-grab is handled by ScenarioManager flipping
/// ChildNPC.canBeSavedDirectly — this component only adds the close-window verb.
/// </summary>
public class WindowCloser : MonoBehaviour, IProximityInteractable
{
    [Header("Sliding pane")]
    [Tooltip("The sliding pane transform. Authored at the CLOSED (covering) pose; starts the round OPEN (displaced by openOffset) and slides back to closed when the player closes it.")]
    public Transform windowPanel;
    [Tooltip("Local-space displacement of the pane in its OPEN position, relative to its authored closed pose. Default slides along the wall (local Z). Flip the sign / change axis to taste.")]
    public Vector3 openOffset = new Vector3(0f, 0f, 1.8f);
    [Tooltip("Slide animation duration in seconds.")]
    public float slideDuration = 0.6f;

    [Header("Interaction")]
    [Tooltip("Distance (metres) from the player camera (measured to the window opening) at which the window can be closed.")]
    public float interactionRadius = 2.5f;
    [Tooltip("Use {KEY} as a placeholder for the active input ('E' desktop / 'Presse les 4 gâchettes' VR).")]
    public string promptText = "{KEY} pour fermer la fenêtre";
    [Tooltip("Offset of the prompt relative to the window opening.")]
    public Vector3 promptOffset = new Vector3(0f, 0.5f, 0f);

    [Header("Success")]
    [Tooltip("Hazard that arms this closer (auto-found by name if null). Also neutralised on success.")]
    public HazardZone targetHazard;
    [Tooltip("Hazard GameObject name searched when targetHazard is null.")]
    public string targetHazardName = "HazardZone_Window";
    [Tooltip("Report success immediately on close. Off for the story window scenario, where ChildNPC decides win/lose by whether the close beat the toddler's climb.")]
    public bool reportSuccessOnClose = true;

    [Header("Testing")]
    [Tooltip("Sandbox helper: arm even when no ScenarioManager is present.")]
    public bool forceArmed = false;

    private bool _armed;
    private bool _closed;
    private Vector3 _closedLocalPos;   // authored (covering) pose, in the pane's local space
    private Vector3 _openingWorldPos;  // stable anchor at the opening (for prompt + proximity)
    private bool _havePose;
    private GameObject _prompt;
    private Coroutine _slide;

    public bool IsArmed => _armed && !_closed;

    private void Awake()
    {
        if (windowPanel != null)
        {
            _closedLocalPos = windowPanel.localPosition;
            _openingWorldPos = windowPanel.position; // captured BEFORE we open it
            _havePose = true;
        }
    }

    private void OnEnable() => ProximityInteractables.Register(this);
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

        SetOpen(); // window starts open
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
        _armed = forceArmed || (cfg != null && targetHazard != null && cfg.hazardZone == targetHazard);
    }

    private void OnGameStateChanged(GameManager.GameState state)
    {
        if (state == GameManager.GameState.Playing)
        {
            _closed = false;
            SetOpen(); // new round — window is open again
        }
        else if (_prompt != null)
        {
            _prompt.SetActive(false);
        }
    }

    /// <summary>Snap the pane to its OPEN (slid-aside) position instantly.</summary>
    private void SetOpen()
    {
        if (_slide != null) { StopCoroutine(_slide); _slide = null; }
        if (_havePose && windowPanel != null)
            windowPanel.localPosition = _closedLocalPos + openOffset;
    }

    private void Update()
    {
        if (_prompt == null) return;
        var cam = Camera.main;
        if (cam == null) { _prompt.SetActive(false); return; }

        bool show = IsArmed
                 && _havePose
                 && GameManager.Instance != null
                 && GameManager.Instance.State == GameManager.GameState.Playing
                 && Vector3.Distance(cam.transform.position, _openingWorldPos) <= interactionRadius;

        _prompt.SetActive(show);
        if (show)
            ProximityPrompt.Face(_prompt, _openingWorldPos + promptOffset, cam.transform.position);
    }

    // ── IProximityInteractable ───────────────────────────────────────────────
    public bool TryInteract(Vector3 cameraPosition)
    {
        if (!IsArmed || !_havePose) return false;
        if (Vector3.Distance(cameraPosition, _openingWorldPos) > interactionRadius) return false;
        CloseWindow();
        return true;
    }

    private void CloseWindow()
    {
        _closed = true;
        if (_prompt != null) _prompt.SetActive(false);

        // Slide the pane shut.
        if (_slide != null) StopCoroutine(_slide);
        if (windowPanel != null) _slide = StartCoroutine(SlideTo(_closedLocalPos));

        // Neutralise the ledge hazard so the climbing toddler sees "window closed" and topples back inside.
        if (targetHazard != null) targetHazard.MarkNeutralised();

        // The bird on the sill flies off the instant the window shuts — so we never close it on the bird.
        var pigeon = FindAnyObjectByType<PigeonEscape>();
        if (pigeon != null) pigeon.TakeOff();

        if (reportSuccessOnClose)
        {
            Debug.Log("[WindowCloser] Window slid shut — scenario won.", this);
            GameManager.Instance?.ReportSuccess();
        }
        else
        {
            // Story window: ChildNPC's climb beat owns win/lose (closed-in-time → backward fall → success).
            Debug.Log("[WindowCloser] Window slid shut — outcome deferred to the climb (neutralised).", this);
        }
    }

    private IEnumerator SlideTo(Vector3 targetLocal)
    {
        if (windowPanel == null) yield break;
        Vector3 start = windowPanel.localPosition;
        float t = 0f;
        while (t < slideDuration)
        {
            t += Time.deltaTime;
            float u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / slideDuration));
            windowPanel.localPosition = Vector3.Lerp(start, targetLocal, u);
            yield return null;
        }
        windowPanel.localPosition = targetLocal;
        _slide = null;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (windowPanel == null) return;
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.5f);
        Gizmos.DrawWireSphere(Application.isPlaying ? _openingWorldPos : windowPanel.position, interactionRadius);
        // Show the open (slid) pose.
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.25f);
        Vector3 openWorld = windowPanel.parent != null
            ? windowPanel.parent.TransformPoint(windowPanel.localPosition + openOffset)
            : windowPanel.localPosition + openOffset;
        Gizmos.DrawWireCube(openWorld, windowPanel.lossyScale);
    }
#endif
}
