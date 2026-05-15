using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// STOP IT! — GameManager
/// State machine: Menu → Playing → Success / Fail → (next or Menu)
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    // ── State ──────────────────────────────────────────────────────────────
    public enum GameState { Menu, Playing, Success, Fail, GameOver }
    public GameState State { get; private set; } = GameState.Menu;

    // ── Inspector ──────────────────────────────────────────────────────────
    [Header("Scenarios")]
    [Tooltip("Time allowed per scenario (seconds)")]
    public float scenarioDuration = 30f;

    [Tooltip("How many scenarios before game over")]
    public int totalScenarios = 6;

    [Header("Feedback")]
    public AudioSource audioSource;
    public AudioClip successClip;
    public AudioClip failClip;

    // ── Events ─────────────────────────────────────────────────────────────
    public UnityEvent<GameState> OnStateChanged;
    public UnityEvent<float> OnTimerUpdated;   // remaining seconds
    public UnityEvent<int, int> OnScoreUpdated; // (current, total)

    // ── Runtime ────────────────────────────────────────────────────────────
    private float _timeRemaining;
    private int _scenariosCompleted = 0;
    private int _score = 0;
    private Coroutine _timerCoroutine;
    private bool _autoAdvance = false;

    // ── Public read-only state ─────────────────────────────────────────────
    public bool IsAutoAdvance => _autoAdvance;
    public int Score => _score;
    public int ScenariosCompleted => _scenariosCompleted;
    public int TotalScenarios => _autoAdvance ? totalScenarios : 1;

    // ── Unity ──────────────────────────────────────────────────────────────
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        // Don't auto-start — wait for menu selection
        SetState(GameState.Menu);
    }

    // ── Public API ─────────────────────────────────────────────────────────

    /// <summary>Launch a single scenario (from menu). Does NOT auto-advance.</summary>
    public void LaunchSingle()
    {
        _autoAdvance = false;
        _score = 0;
        _scenariosCompleted = 0;
        OnScoreUpdated?.Invoke(_score, 1);
        StartScenario();
    }

    /// <summary>Launch all scenarios in sequence.</summary>
    public void LaunchAll()
    {
        _autoAdvance = true;
        _score = 0;
        _scenariosCompleted = 0;
        OnScoreUpdated?.Invoke(_score, totalScenarios);
        StartScenario();
    }

    /// <summary>Go back to menu state.</summary>
    public void ReturnToMenu()
    {
        StopTimer();
        StopAllCoroutines();
        _scenariosCompleted = 0;
        _score = 0;
        SetState(GameState.Menu);
    }

    /// <summary>
    /// Restart the scenario the player just played (after Success or Fail).
    /// Preserves the LaunchSingle vs LaunchAll mode and rolls back any score
    /// added for the round being retried so it isn't double-counted.
    /// </summary>
    public void RestartCurrentScenario()
    {
        var sm = ScenarioManager.Instance;
        if (sm == null || sm.CurrentIndex < 0) return;

        StopTimer();
        StopAllCoroutines();

        // The just-played round was NOT counted in _scenariosCompleted (AfterResult
        // bails out for non-auto-success cases — see below), so no rollback needed there.
        // But ReportSuccess() did increment _score — undo so retry doesn't double-count.
        if (State == GameState.Success && _score > 0)
        {
            _score--;
            OnScoreUpdated?.Invoke(_score, TotalScenarios);
        }

        sm.SetNextScenario(sm.CurrentIndex);
        StartScenario();
    }

    /// <summary>
    /// Move on to the next scenario after a Fail in LaunchAll mode (player chose to skip).
    /// In single mode, this is equivalent to ReturnToMenu().
    /// </summary>
    public void ContinueToNext()
    {
        if (!_autoAdvance) { ReturnToMenu(); return; }

        StopTimer();
        StopAllCoroutines();

        // Count the failed round that we paused on.
        _scenariosCompleted++;

        var sm = ScenarioManager.Instance;
        if (sm == null) return;

        int next = sm.CurrentIndex + 1;
        if (_scenariosCompleted >= totalScenarios || next >= sm.ScenarioCount)
        {
            SetState(GameState.GameOver);
            StartCoroutine(GameOverToMenu());
            return;
        }

        sm.SetNextScenario(next);
        StartScenario();
    }

    /// <summary>Called by PlayerBlocker when child is intercepted in time.</summary>
    public void ReportSuccess()
    {
        if (State != GameState.Playing) return;
        StopTimer();
        _score++;
        int total = _autoAdvance ? totalScenarios : 1;
        SetState(GameState.Success);
        PlaySound(successClip);
        OnScoreUpdated?.Invoke(_score, total);
        StartCoroutine(AfterResult(2f));
    }

    /// <summary>Called by HazardZone when child reaches the hazard.</summary>
    public void ReportFail()
    {
        if (State != GameState.Playing) return;
        StopTimer();
        SetState(GameState.Fail);
        PlaySound(failClip);
        StartCoroutine(AfterResult(2.5f));
    }

    // ── Private helpers ────────────────────────────────────────────────────
    private void StartScenario()
    {
        _timeRemaining = scenarioDuration;
        SetState(GameState.Playing);
        _timerCoroutine = StartCoroutine(TimerRoutine());
    }

    private IEnumerator TimerRoutine()
    {
        while (_timeRemaining > 0f)
        {
            _timeRemaining -= Time.deltaTime;
            OnTimerUpdated?.Invoke(Mathf.Max(_timeRemaining, 0f));
            yield return null;
        }
        ReportFail();
    }

    private void StopTimer()
    {
        if (_timerCoroutine != null) { StopCoroutine(_timerCoroutine); _timerCoroutine = null; }
    }

    private IEnumerator AfterResult(float delay)
    {
        yield return new WaitForSeconds(delay);

        // LaunchAll + Success → consume the round and chain to next (legacy behavior).
        if (_autoAdvance && State == GameState.Success)
        {
            _scenariosCompleted++;
            if (_scenariosCompleted >= totalScenarios)
            {
                SetState(GameState.GameOver);
                yield return new WaitForSeconds(3f);
                SetState(GameState.Menu);
            }
            else
            {
                StartScenario();
            }
            yield break;
        }

        // Otherwise (single mode, or LaunchAll+Fail) → pause and let the player choose
        // Restart / Continue / Menu via the ScenarioMenu retry panel.
        // Stay in Success or Fail. _scenariosCompleted is incremented in ContinueToNext()
        // when the player explicitly skips ahead.
    }

    private IEnumerator GameOverToMenu()
    {
        yield return new WaitForSeconds(3f);
        SetState(GameState.Menu);
    }

    private void SetState(GameState newState)
    {
        State = newState;
        OnStateChanged?.Invoke(newState);
    }

    private void PlaySound(AudioClip clip)
    {
        if (audioSource != null && clip != null) audioSource.PlayOneShot(clip);
    }
}
