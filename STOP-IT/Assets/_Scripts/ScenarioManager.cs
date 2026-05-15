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
        public Transform childSpawnPoint;
        public HazardZone hazardZone;
        public Transform playerSpawnPoint;
        public GameObject[] scenarioObjects;
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

        // Update UI
        if (scenarioUI != null)
        {
            scenarioUI.SetScenarioName(config.scenarioName);
            scenarioUI.SetActionHint(config.actionHint);
        }

        // Notify subscribers (DangerVignette, HazardIndicator, custom listeners)
        OnScenarioActivated?.Invoke(config);
    }
}
