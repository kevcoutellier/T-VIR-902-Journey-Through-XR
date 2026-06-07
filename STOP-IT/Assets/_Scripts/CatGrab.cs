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
    public string promptText = "Appuie sur {KEY} pour récupérer le chat";

    [Tooltip("Local offset of the prompt above the cat.")]
    public Vector3 promptOffset = new Vector3(0f, 0.35f, 0f);

    [Header("Success")]
    [Tooltip("Hazard to neutralise on success (auto = active scenario's hazard, e.g. the microwave).")]
    public HazardZone targetHazard;

    [Header("Testing")]
    [Tooltip("Sandbox helper: arm even when no ScenarioManager is present.")]
    public bool forceArmed = false;

    [Header("Carry to basket (S2 → S3)")]
    [Tooltip("The cat basket (Cat Bed). After snatching the cat, the player must walk HERE and press the input to drop it.")]
    public Transform basket;
    [Tooltip("Distance from the player camera to the basket at which the cat can be dropped.")]
    public float basketDropRadius = 1.8f;
    [Tooltip("Prompt shown while carrying the cat, near the basket. {KEY} = active input.")]
    public string dropPromptText = "Appuie sur {KEY} pour déposer le chat dans le panier";

    private bool _armed;
    private bool _taken;
    private bool _carrying;
    private Renderer[] _renderers;
    private GameObject _prompt;
    private GameObject _dropPrompt;
    private ChildNPC _child;
    private Transform _homeParent;
    private Vector3 _homeLocalPos;
    private Quaternion _homeLocalRot;
    private Vector3 _homeLocalScale;
    private bool _homeRecorded;

    /// <summary>True while the player carries the cat (between snatching it and dropping it in the basket).
    /// The WaterBottle reads this to gate the S3 swap until the cat is put away.</summary>
    public static bool PlayerCarryingCat { get; private set; }

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
        if (_dropPrompt != null) _dropPrompt.SetActive(false);
    }

    private void Start()
    {
        _prompt = ProximityPrompt.Build("CatGrab_Prompt", InputHints.ResolvePrompt(promptText));
        _dropPrompt = ProximityPrompt.Build("CatDrop_Prompt", InputHints.ResolvePrompt(dropPromptText));
        _armed = forceArmed;

        // The cat's "home" is its bed (where the tool seated it). Dropping / restarting returns it here.
        _child = FindAnyObjectByType<ChildNPC>();
        _homeParent = transform.parent;
        _homeLocalPos = transform.localPosition;
        _homeLocalRot = transform.localRotation;
        _homeLocalScale = transform.localScale;
        _homeRecorded = true;
        PlayerCarryingCat = false;

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
        // We are the take-target when we're this scenario's carried OR picked-up item.
        bool isCatScenario = forceArmed || (cfg != null && (cfg.carriedItem == gameObject || cfg.pickupItem == gameObject));
        _armed = isCatScenario;
        if (_armed && cfg != null && cfg.hazardZone != null && targetHazard == null)
            targetHazard = cfg.hazardZone;

        // Reset the cat to its basket ONLY when the story is at/before the cat scenario (S1 restart or
        // S2 (re)activation). From S3 onward the PLAYER owns the cat (carry → drop), so we must NOT snap
        // it back — that was the "cat returns to its basket by itself" bug (the S2→S3 advance is also a
        // Playing transition, so resetting on every Playing undid the player's carry).
        int idx = ScenarioManager.Instance != null ? ScenarioManager.Instance.CurrentIndex : -1;
        if (idx <= 0 || isCatScenario)
        {
            _taken = false;
            _carrying = false;
            PlayerCarryingCat = false;
            RestoreToHome();
            SetVisible(true);
        }
    }

    private void OnGameStateChanged(GameManager.GameState state)
    {
        // The cat reset is handled in OnScenarioActivated (gated by scenario index) so the S2→S3
        // advance — which is ALSO a Playing transition — doesn't snap the carried cat back to its basket.
        if (state != GameManager.GameState.Playing)
        {
            if (_prompt != null) _prompt.SetActive(false);
            if (_dropPrompt != null) _dropPrompt.SetActive(false);
        }
    }

    private void Update()
    {
        var cam = Camera.main;
        if (cam == null) { if (_prompt) _prompt.SetActive(false); if (_dropPrompt) _dropPrompt.SetActive(false); return; }
        bool playing = GameManager.Instance != null && GameManager.Instance.State == GameManager.GameState.Playing;

        // Carrying the cat → "drop it in the basket" prompt, gated by distance to the basket.
        if (_carrying && _dropPrompt != null && basket != null)
        {
            bool showDrop = playing && Vector3.Distance(cam.transform.position, basket.position) <= basketDropRadius;
            _dropPrompt.SetActive(showDrop);
            if (showDrop) ProximityPrompt.Face(_dropPrompt, basket.position + promptOffset, cam.transform.position);
        }
        else if (_dropPrompt != null) _dropPrompt.SetActive(false);

        // Armed (cat still in the toddler's hands) → "take the cat" prompt near the cat.
        if (_prompt != null)
        {
            bool show = IsArmed && !_carrying && playing
                     && Vector3.Distance(cam.transform.position, transform.position) <= interactionRadius;
            _prompt.SetActive(show);
            if (show) ProximityPrompt.Face(_prompt, transform.position + promptOffset, cam.transform.position);
        }
    }

    // ── IProximityInteractable ───────────────────────────────────────────────
    public bool TryInteract(Vector3 cameraPosition)
    {
        // While carrying, the press drops the cat in the basket (only if close enough).
        if (_carrying)
        {
            if (basket == null) return false;
            if (Vector3.Distance(cameraPosition, basket.position) > basketDropRadius) return false;
            DropInBasket();
            return true;
        }
        // Otherwise the press snatches the cat from the toddler.
        if (!IsArmed) return false;
        if (Vector3.Distance(cameraPosition, transform.position) > interactionRadius) return false;
        TakeCat();
        return true;
    }

    private void TakeCat()
    {
        _taken = true;
        _carrying = true;
        PlayerCarryingCat = true;

        // Take ownership of the cat from the toddler WITHOUT auto-returning it to the bed —
        // the player must now carry it and drop it in the basket themselves.
        if (_child == null) _child = FindAnyObjectByType<ChildNPC>();
        if (_child != null) _child.ForgetCarriedItem();

        SetVisible(false); // "in the player's arms"
        if (_prompt != null) _prompt.SetActive(false);

        // Neutralise the microwave so a same-frame arrival can't flash a fail,
        // then report the save (which also stops the child via the state change).
        if (targetHazard != null) targetHazard.MarkNeutralised();

        Debug.Log("[CatGrab] Cat snatched — carry it to the basket. Scenario 2 won.", this);
        GameManager.Instance?.ReportSuccess();
    }

    private void DropInBasket()
    {
        _carrying = false;
        PlayerCarryingCat = false;
        RestoreToHome();   // the cat's home IS the basket (bed) — seats it back exactly
        SetVisible(true);
        if (_dropPrompt != null) _dropPrompt.SetActive(false);
        Debug.Log("[CatGrab] Cat dropped in its basket.", this);
    }

    private void RestoreToHome()
    {
        if (!_homeRecorded) return;
        transform.SetParent(_homeParent, false);
        transform.localPosition = _homeLocalPos;
        transform.localRotation = _homeLocalRot;
        transform.localScale = _homeLocalScale;
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
