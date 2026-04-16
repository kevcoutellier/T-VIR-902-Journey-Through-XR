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
    public int totalScenarios = 5;

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
        _scenariosCompleted++;

        if (!_autoAdvance)
        {
            // Single mode: return to menu
            SetState(GameState.Menu);
        }
        else if (_scenariosCompleted >= totalScenarios)
        {
            SetState(GameState.GameOver);
            yield return new WaitForSeconds(3f);
            SetState(GameState.Menu);
        }
        else
        {
            StartScenario();
        }
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
