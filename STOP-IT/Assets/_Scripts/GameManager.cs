using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// STOP IT! — GameManager
/// Manages the game state machine: Idle → Playing → Success / Fail → NextScenario
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    // ── State ──────────────────────────────────────────────────────────────
    public enum GameState { Idle, Playing, Success, Fail, GameOver }
    public GameState State { get; private set; } = GameState.Idle;

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
    private int _currentScenario = 0;
    private int _score = 0;
    private Coroutine _timerCoroutine;

    // ── Unity ──────────────────────────────────────────────────────────────
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start() => StartScenario();

    // ── Public API ─────────────────────────────────────────────────────────
    /// <summary>Called by PlayerBlocker when child is intercepted in time.</summary>
    public void ReportSuccess()
    {
        if (State != GameState.Playing) return;
        StopTimer();
        _score++;
        SetState(GameState.Success);
        PlaySound(successClip);
        OnScoreUpdated?.Invoke(_score, totalScenarios);
        StartCoroutine(AdvanceAfterDelay(2f));
    }

    /// <summary>Called by HazardZone when child reaches the hazard.</summary>
    public void ReportFail()
    {
        if (State != GameState.Playing) return;
        StopTimer();
        SetState(GameState.Fail);
        PlaySound(failClip);
        StartCoroutine(AdvanceAfterDelay(2.5f));
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
        // Time's up → fail
        ReportFail();
    }

    private void StopTimer()
    {
        if (_timerCoroutine != null) { StopCoroutine(_timerCoroutine); _timerCoroutine = null; }
    }

    private IEnumerator AdvanceAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        _currentScenario++;
        if (_currentScenario >= totalScenarios)
        {
            SetState(GameState.GameOver);
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
