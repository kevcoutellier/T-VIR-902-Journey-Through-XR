using System.Collections;
using UnityEngine;

/// <summary>
/// STOP IT! — StoryModeDirector
/// Drives the continuous "story mode": the scenarios chain automatically, ANY failure
/// restarts at scenario 1, and the "catch the baby" scenarios (S1 fork, S4 skateboard)
/// advance to the next scenario only when the player RELEASES the child (drop), not on
/// the grab itself.
///
/// It reuses the existing state machine: it drives ONE scenario at a time through
/// GameManager.LaunchSingle() (so GameManager never auto-chains on its own) and listens
/// to OnStateChanged to decide what happens after each Success / Fail.
///
/// Incremental delivery: raise <see cref="activeScenarioCount"/> from 1 → 5 as each
/// scenario is finished and validated. Reaching the last active scenario = story complete
/// (for now it loops back to scenario 1; the end screen ships with scenario 5).
/// </summary>
[DefaultExecutionOrder(10)]
public class StoryModeDirector : MonoBehaviour
{
    [Header("Flow")]
    [Tooltip("Start the story automatically a couple of frames after entering Play.")]
    public bool autoStartOnPlay = true;

    [Tooltip("How many scenarios are currently playable. Raise 1 → 5 as each scenario is " +
             "finished. Reaching the last active scenario completes the story.")]
    public int activeScenarioCount = 1;

    [Tooltip("0-based indices of scenarios whose success advances on RELEASE (drop) instead of " +
             "immediately. Defaults to S1 (0) and S4 (3) — the 'catch the baby' scenarios.")]
    public int[] advanceOnReleaseIndices = { 0, 3 };

    [Header("Timing")]
    [Tooltip("Delay after a FAIL before restarting at scenario 1 (lets the flash/shake play).")]
    public float restartDelayOnFail = 2.5f;
    [Tooltip("Delay after a SUCCESS before activating the next scenario.")]
    public float advanceDelayOnSuccess = 1.2f;
    [Tooltip("Safety cap (seconds) on waiting for the player to release a grabbed child.")]
    public float maxReleaseWait = 6f;

    [Header("References")]
    [Tooltip("Optional 'you lost' screen shown on a fail before restarting at scenario 1.")]
    public StoryLoseScreen loseScreen;
    [Tooltip("Black end screen shown at the FINAL scenario: scrolling prevention on victory, held message on defeat. Then back to the menu.")]
    public StoryEndScreen endScreen;
    [Tooltip("Prevention message that scrolls on the victory end screen.")]
    [TextArea(4, 14)] public string victoryMessage = "Ne quitte jamais ton enfant des yeux.";

    private ChildNPC _child;
    private bool _busy;

    private void Start()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnStateChanged.AddListener(OnStateChanged);
        else
            Debug.LogError("[StoryModeDirector] GameManager.Instance is null at Start — story will not run.", this);

        _child = FindAnyObjectByType<ChildNPC>();
        if (autoStartOnPlay) StartCoroutine(AutoStartRoutine());
    }

    private void OnDisable()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnStateChanged.RemoveListener(OnStateChanged);
    }

    private IEnumerator AutoStartRoutine()
    {
        // Let every Awake/Start run (GameManager, ScenarioManager, ChildNPC) before launching.
        yield return null;
        yield return null;
        StartStoryMode();
    }

    /// <summary>(Re)start the story from scenario 1. Public so a future menu can call it.</summary>
    public void StartStoryMode()
    {
        var sm = ScenarioManager.Instance ?? FindAnyObjectByType<ScenarioManager>();
        if (sm == null || GameManager.Instance == null)
        {
            Debug.LogError("[StoryModeDirector] Missing ScenarioManager or GameManager — cannot start.", this);
            return;
        }
        Debug.Log("[StoryModeDirector] Starting story at scenario 1.");
        sm.SetNextScenario(0);
        GameManager.Instance.LaunchSingle();
    }

    private void OnStateChanged(GameManager.GameState state)
    {
        if (_busy) return;
        if (state == GameManager.GameState.Fail)
            StartCoroutine(RestartAfterFail());
        else if (state == GameManager.GameState.Success)
            StartCoroutine(AdvanceAfterSuccess());
    }

    private IEnumerator RestartAfterFail()
    {
        _busy = true;
        Debug.Log("[StoryModeDirector] Fail.");

        var sm = ScenarioManager.Instance ?? FindAnyObjectByType<ScenarioManager>();
        var cfg = sm != null ? sm.CurrentScenario : null;
        int idx = sm != null ? sm.CurrentIndex : -1;
        bool isLast = sm != null && idx >= activeScenarioCount - 1; // failed the FINAL scenario

        // Let the fail beat (electrocution clip / microwave explosion / fall) play out first.
        float delay = cfg != null ? cfg.failScreenDelay : restartDelayOnFail;
        yield return new WaitForSeconds(delay);

        string msg = (cfg != null && !string.IsNullOrEmpty(cfg.loseMessage))
            ? cfg.loseMessage
            : "Reste toujours attentif à ton enfant.";

        if (isLast && endScreen != null)
        {
            // Failed the LAST scenario → black defeat screen with its prevention message → back to the menu.
            bool doneD = false;
            endScreen.Show("TROP TARD…", new Color(1f, 0.3f, 0.25f), msg, false, () => doneD = true);
            while (!doneD) yield return null;
            GameManager.Instance?.ReturnToMenu();
            _busy = false;
            yield break;
        }

        // S1–S4 fail → retry screen → restart at scenario 1.
        if (loseScreen != null)
        {
            bool retry = false;
            loseScreen.Show("TROP TARD…", msg, () => retry = true);
            while (!retry) yield return null;
        }

        StartStoryMode();
        _busy = false;
    }

    private IEnumerator AdvanceAfterSuccess()
    {
        _busy = true;
        var sm = ScenarioManager.Instance ?? FindAnyObjectByType<ScenarioManager>();
        int idx = sm != null ? sm.CurrentIndex : -1;

        // Catch scenarios: wait until the player drops the child before moving on.
        if (idx >= 0 && AdvancesOnRelease(idx))
        {
            if (_child == null) _child = FindAnyObjectByType<ChildNPC>();
            float t = 0f;
            while (_child != null && _child.IsHeld && t < maxReleaseWait)
            {
                t += Time.deltaTime;
                yield return null;
            }
        }

        yield return new WaitForSeconds(advanceDelayOnSuccess);

        int next = idx + 1;
        bool lastActive = sm == null || next >= activeScenarioCount || next >= sm.ScenarioCount;
        if (lastActive)
        {
            // Final scenario saved → victory end screen (scrolling prevention message) → back to the menu.
            Debug.Log("[StoryModeDirector] Story complete — victory.");
            if (endScreen != null)
            {
                bool done = false;
                endScreen.Show("BRAVO !", new Color(0.35f, 0.95f, 0.45f), victoryMessage, true, () => done = true);
                while (!done) yield return null;
            }
            GameManager.Instance?.ReturnToMenu();
        }
        else
        {
            Debug.Log($"[StoryModeDirector] Success — advancing to scenario {next + 1}.");
            sm.SetNextScenario(next);
            GameManager.Instance.LaunchSingle();
        }
        _busy = false;
    }

    private bool AdvancesOnRelease(int idx)
    {
        if (advanceOnReleaseIndices == null) return false;
        foreach (int i in advanceOnReleaseIndices) if (i == idx) return true;
        return false;
    }
}
