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

    private void OnEnable()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnStateChanged.AddListener(OnStateChanged);
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

        // Teleport child NPC
        if (childNPC != null && config.childSpawnPoint != null)
        {
            // If a previous scenario ended via Grab(), the child is parented to a hand
            // and its NavMeshAgent is disabled. Reset before warping.
            childNPC.ResetForScenario();

            var agent = childNPC.GetComponent<NavMeshAgent>();
            if (agent != null && agent.isOnNavMesh)
                agent.Warp(config.childSpawnPoint.position);
            else
                childNPC.transform.position = config.childSpawnPoint.position;

            childNPC.transform.rotation = config.childSpawnPoint.rotation;
        }

        // Set the child's target hazard
        if (childNPC != null && config.hazardZone != null)
            childNPC.targetHazard = config.hazardZone;

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
