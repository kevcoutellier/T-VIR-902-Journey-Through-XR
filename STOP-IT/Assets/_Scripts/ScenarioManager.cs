using System;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;
using Unity.XR.CoreUtils;

/// <summary>
/// STOP IT! — ScenarioManager
/// Manages multiple scenarios. Supports both single-scenario launch (from menu)
/// and sequential play (all scenarios in order).
/// Runs BEFORE ChildNPC so that targetHazard is updated before the child reads it.
/// </summary>
[DefaultExecutionOrder(-50)]
public class ScenarioManager : MonoBehaviour
{
    [Serializable]
    public class ScenarioConfig
    {
        public string scenarioName;
        [Tooltip("Short verb shown to the player at the start of the scenario, e.g. 'ATTRAPE LE BÉBÉ !'")]
        [TextArea(1, 2)]
        public string actionHint = "ATTRAPE LE BÉBÉ !";
        [Tooltip("Objectif générique affiché dans le bandeau HUD tant que le verbe d'action n'est pas " +
                 "encore révélé (scénarios à interception S1 fourchette / S4 skate : actionHint vide " +
                 "jusqu'à l'interception). Laisser vide pour l'objectif par défaut ('surveille l'enfant').")]
        [TextArea(1, 2)]
        public string objectiveHint = "";
        public Transform childSpawnPoint;
        public HazardZone hazardZone;
        public Transform playerSpawnPoint;
        public GameObject[] scenarioObjects;
        [Tooltip("Override the child NPC start delay (seconds). " +
                 "Negative = use ChildNPC.startDelay default. " +
                 "Set very high (e.g. 999) when a WindowInteractable triggers the walk.")]
        public float childStartDelayOverride = -1f;

        [Header("Scenario-specific")]
        [Tooltip("Show the cosmetic skateboard mesh under the NPC (stairs scenario). Deprecated — prefer carriedItem.")]
        public bool showSkateboard;
        [Tooltip("Water bottle to reset at scenario start (bathroom scenario).")]
        public WaterBottle waterBottle;
        [Tooltip("Pigeon to reset and bind to the active NPC (window scenario).")]
        public PigeonEscape pigeon;
        [Tooltip("Object parented to the NPC for the duration of this scenario (fork, cat, skateboard, …). Restored to its original parent when the scenario changes.")]
        public GameObject carriedItem;
        [Tooltip("Local position of the carried item relative to the NPC root.")]
        public Vector3 carriedItemLocalPosition = new Vector3(0f, 0.2f, 0.5f);
        [Tooltip("Local Euler angles of the carried item relative to the NPC root.")]
        public Vector3 carriedItemLocalEuler;

        [Header("Pickup waypoint (optional)")]
        [Tooltip("If set, the child first walks HERE, picks up pickupItem, THEN walks to the hazard " +
                 "(fork on the floor, cat in its bed, skateboard…). Leave null to walk straight to the hazard.")]
        public Transform pickupWaypoint;
        [Tooltip("Item attached to the child upon reaching the pickup waypoint.")]
        public GameObject pickupItem;
        [Tooltip("Local position of the picked-up item relative to the NPC root.")]
        public Vector3 pickupItemLocalPosition = new Vector3(0.12f, 0.48f, 0.22f);
        [Tooltip("Local Euler angles of the picked-up item relative to the NPC root.")]
        public Vector3 pickupItemLocalEuler;

        [Header("Cleaning products (scenario 3)")]
        [Tooltip("Cleaning-product objects; on arrival the child grabs the nearest visible one into its hand to drink it.")]
        public GameObject[] cleaningProducts;
        [Tooltip("Local position of the grabbed cleaning product in the child's hand.")]
        public Vector3 cleaningItemLocalPosition = new Vector3(0f, 0f, 0.04f);
        [Tooltip("Local Euler of the grabbed cleaning product in the child's hand.")]
        public Vector3 cleaningItemLocalEuler;

        [Header("Skateboard (scenario 4)")]
        [Tooltip("The skateboard the child mounts and rides down the stairs.")]
        public SkateboardRide skateboardRide;

        [Header("Lose screen")]
        [Tooltip("Home-safety prevention message shown on the lose screen when THIS scenario fails.")]
        [TextArea(2, 4)]
        public string loseMessage;
        [Tooltip("Seconds to wait after a fail (let the fail beat play out) before the lose screen appears. " +
                 "Electrocution ≈ 2.5; microwave ≈ 0.1 (the 2s run already happened inside the hazard, so the " +
                 "red explosion lands right as 'TROP TARD' fades in).")]
        public float failScreenDelay = 2.5f;

        [Tooltip("If true, the child can't be saved by grabbing/touching them this scenario — " +
                 "the player must use the scenario verb instead (take the cat, close the window). " +
                 "Left false on every existing scenario (grab/touch stays allowed); set true for " +
                 "scenario 2 (cat) and scenario 5 (window). " +
                 "NOTE: phrased as 'disable' so scenarios serialized before this field existed " +
                 "deserialize to false = allowed.")]
        public bool disableDirectChildSave = false;
    }

    [Header("References")]
    public ChildNPC childNPC;
    public ScenarioUI scenarioUI;

    [Header("Scenarios")]
    public ScenarioConfig[] scenarios;

    [Header("Events")]
    [Tooltip("Fired when a scenario becomes active. UI/VFX/Vignette can hook in to retarget.")]
    public UnityEvent<ScenarioConfig> OnScenarioActivated;

    private int _currentIndex = -1;
    private int _queuedIndex = -1;
    private XROrigin _xrOrigin;
    private float _originalChildStartDelay = -1f;

    public static ScenarioManager Instance { get; private set; }

    public int ScenarioCount => scenarios != null ? scenarios.Length : 0;
    public int CurrentIndex => _currentIndex;
    public ScenarioConfig CurrentScenario =>
        (scenarios != null && _currentIndex >= 0 && _currentIndex < scenarios.Length)
            ? scenarios[_currentIndex]
            : null;

    public string GetScenarioName(int index)
    {
        if (scenarios != null && index >= 0 && index < scenarios.Length)
            return scenarios[index].scenarioName;
        return "";
    }

    /// <summary>
    /// Objectif à afficher dans le bandeau HUD tant qu'aucun verbe d'action n'est révélé
    /// (scénarios à interception : actionHint vide au départ, dévoilé plus tard par ChildNPC.ArmCatch).
    /// Additif — n'altère aucune signature/comportement existant. Retombe sur un objectif générique
    /// si le scénario courant n'en définit pas.
    /// </summary>
    public string CurrentObjectiveFallback
    {
        get
        {
            var cfg = CurrentScenario;
            if (cfg != null && !string.IsNullOrEmpty(cfg.objectiveHint))
                return cfg.objectiveHint;
            return "Objectif : surveille l'enfant";
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        _xrOrigin = FindAnyObjectByType<XROrigin>();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // We register the GameManager listener in Start rather than OnEnable.
    // Reason: this component has [DefaultExecutionOrder(-50)] so its Awake/OnEnable
    // run BEFORE GameManager.Awake (ordre 0). At OnEnable time, GameManager.Instance
    // is still null and the listener would silently never be added — which is
    // exactly the bug that made every scenario behave like scenario 1 (the
    // child kept the inspector-default targetHazard because ActivateScenario
    // was never reached on state changes). Start always runs after every Awake
    // in the scene, so Instance is guaranteed to be set here.
    private void Start()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnStateChanged.AddListener(OnStateChanged);
        else
            Debug.LogError("[ScenarioManager] GameManager.Instance is null at Start — " +
                           "state events will not reach this manager. Check scene setup.", this);
    }

    private void OnDisable()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnStateChanged.RemoveListener(OnStateChanged);
    }

    /// <summary>Queue a specific scenario index. Next time GameManager enters Playing, this activates.</summary>
    public void SetNextScenario(int index)
    {
        _queuedIndex = index;
        _currentIndex = index - 1; // will be incremented in OnStateChanged
    }

    private void OnStateChanged(GameManager.GameState state)
    {
        if (state == GameManager.GameState.Playing)
        {
            if (_queuedIndex >= 0)
            {
                _currentIndex = _queuedIndex;
                _queuedIndex = -1;
            }
            else
            {
                _currentIndex++;
            }

            if (_currentIndex < scenarios.Length)
                ActivateScenario(_currentIndex);
        }
    }

    private void ActivateScenario(int index)
    {
        var config = scenarios[index];
        string label = string.IsNullOrEmpty(config.scenarioName) ? $"#{index}" : config.scenarioName;

        Debug.Log($"[ScenarioManager] ActivateScenario({index}) — name='{label}' " +
                  $"childSpawn={(config.childSpawnPoint!=null?config.childSpawnPoint.name:"<NULL>")} " +
                  $"hazard={(config.hazardZone!=null?config.hazardZone.gameObject.name:"<NULL>")}", this);

        // Teleport child NPC
        if (childNPC != null && config.childSpawnPoint != null)
        {
            // If a previous scenario ended via Grab(), the child is parented to a hand
            // and its NavMeshAgent is disabled. Reset before warping.
            childNPC.ResetForScenario();

            var agent = childNPC.GetComponent<NavMeshAgent>();
            // NavMeshAgent.Warp() is the canonical way to (re-)bind an agent onto the NavMesh.
            // Calling it unconditionally is correct even when the agent was just re-enabled
            // after a Grab() (isOnNavMesh = false in that state). Checking isOnNavMesh first
            // would skip the Warp and leave the agent floating off-mesh, so SetDestination
            // later in BeginWalkAfterDelay silently no-ops — that's why scenarios after a
            // grabbed-win never re-pathed.
            bool warped = false;
            if (agent != null && agent.enabled)
                warped = agent.Warp(config.childSpawnPoint.position);
            if (!warped)
                childNPC.transform.position = config.childSpawnPoint.position;

            childNPC.transform.rotation = config.childSpawnPoint.rotation;
        }
        else if (childNPC != null && config.childSpawnPoint == null)
        {
            // Without a spawn point we'd leave the NPC where the previous scenario
            // dropped it — making every round look like the first. Surface this loudly.
            Debug.LogWarning($"[ScenarioManager] '{label}' has no childSpawnPoint — NPC stays in place. " +
                             "Run Tools → STOP IT → Reposition Spawns, then Wire Scenarios.", this);
        }

        // Apply per-scenario child start delay (save original on first call so we can restore it)
        if (childNPC != null)
        {
            if (_originalChildStartDelay < 0f)
                _originalChildStartDelay = childNPC.startDelay;
            childNPC.startDelay = config.childStartDelayOverride >= 0f
                ? config.childStartDelayOverride
                : _originalChildStartDelay;
        }

        // Per-scenario save rule: cat / window scenarios forbid saving the child by
        // directly grabbing or touching them — the player must use the scenario verb
        // (CatGrab / WindowCloser). Gated at the source inside ChildNPC so it covers
        // both ChildGrabber (grab) and PlayerBlocker (touch).
        if (childNPC != null)
            childNPC.canBeSavedDirectly = !config.disableDirectChildSave;

        // Set the child's target hazard
        if (childNPC != null && config.hazardZone != null)
        {
            childNPC.targetHazard = config.hazardZone;
            Debug.Log($"[ScenarioManager] childNPC.targetHazard ← {config.hazardZone.gameObject.name} " +
                      $"at {config.hazardZone.transform.position}", this);
        }
        else if (childNPC != null && config.hazardZone == null)
        {
            Debug.LogWarning($"[ScenarioManager] '{label}' has no hazardZone — NPC keeps the previous target. " +
                             "Run Tools → STOP IT → Wire Scenarios.", this);
        }

        // Teleport player
        if (_xrOrigin != null && config.playerSpawnPoint != null)
        {
            _xrOrigin.transform.position = config.playerSpawnPoint.position;
            _xrOrigin.transform.rotation = config.playerSpawnPoint.rotation;
        }

        // Scenario-specific visuals & state.
        if (childNPC != null)
        {
            childNPC.SetSkateboardVisible(config.showSkateboard);
            Debug.Log($"[ScenarioManager] SetCarriedItem → " +
                      $"{(config.carriedItem ? config.carriedItem.name : "<null>")} " +
                      $"at local {config.carriedItemLocalPosition}", this);
            childNPC.SetCarriedItem(config.carriedItem,
                                    config.carriedItemLocalPosition,
                                    config.carriedItemLocalEuler);
            childNPC.SetPickup(config.pickupWaypoint, config.pickupItem,
                               config.pickupItemLocalPosition, config.pickupItemLocalEuler);
            childNPC.SetCleaningProducts(config.cleaningProducts,
                                         config.cleaningItemLocalPosition, config.cleaningItemLocalEuler);
            childNPC.SetSkateboard(config.skateboardRide);
        }
        if (config.waterBottle != null)
            config.waterBottle.ResetBottle();
        if (config.pigeon != null)
            config.pigeon.ResetPigeon(childNPC);

        // Catch scenarios (fork S1, skate S4) only let the player catch the child once it commits
        // (picks up the fork / mounts the skate); ChildNPC.ArmCatch reveals the hint at that moment.
        // Every direct-catch scenario (S1 fork, S4 skate) commits via a pickup/mount that calls
        // ChildNPC.ArmCatch — so gate on the save rule alone (robust even if a prop ref is missing).
        bool gateCatch = childNPC != null && !config.disableDirectChildSave;
        if (childNPC != null)
            childNPC.ConfigureCatchGate(gateCatch, config.actionHint);

        // Update UI
        if (scenarioUI != null)
        {
            scenarioUI.SetScenarioName(config.scenarioName);
            scenarioUI.SetActionHint(gateCatch ? "" : config.actionHint);
        }

        // Story progress in the score slot: "(current scenario)/(total)".
        GameManager.Instance?.OnScoreUpdated?.Invoke(index + 1, ScenarioCount);

        // Notify subscribers (DangerVignette, HazardIndicator, custom listeners)
        OnScenarioActivated?.Invoke(config);
    }
}
