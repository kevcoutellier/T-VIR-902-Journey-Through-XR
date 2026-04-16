using System.Collections;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// STOP IT! — ChildNPC
/// The mischievous child. Uses NavMeshAgent to walk toward a HazardZone.
/// The player must intercept them before they reach it.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class ChildNPC : MonoBehaviour
{
    // ── Inspector ──────────────────────────────────────────────────────────
    [Header("Behaviour")]
    [Tooltip("Target hazard zone this child walks toward")]
    public HazardZone targetHazard;

    [Tooltip("Walking speed (m/s)")]
    [Range(0.5f, 5f)]
    public float walkSpeed = 1.5f;

    [Tooltip("Seconds of idle pause before the child starts moving")]
    public float startDelay = 1.5f;

    [Header("Feedback")]
    public AudioSource audioSource;
    public AudioClip laughClip;

    // ── Runtime ────────────────────────────────────────────────────────────
    private NavMeshAgent _agent;
    private bool _isMoving = false;
    private bool _isStopped = false;

    // ── Unity ──────────────────────────────────────────────────────────────
    private void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        _agent.speed = walkSpeed;
        _agent.stoppingDistance = 0.3f;
    }

    private void OnEnable()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnStateChanged.AddListener(OnGameStateChanged);
    }

    private void OnDisable()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnStateChanged.RemoveListener(OnGameStateChanged);
    }

    private void Start()
    {
        if (targetHazard != null)
            StartCoroutine(BeginWalkAfterDelay());
    }

    private void Update()
    {
        if (!_isMoving || _isStopped) return;

        // Reached destination
        if (!_agent.pathPending && _agent.remainingDistance <= _agent.stoppingDistance)
        {
            _isMoving = false;
            targetHazard?.TriggerHazard();
        }
    }

    // ── Public API ─────────────────────────────────────────────────────────
    /// <summary>Called by PlayerBlocker to stop the child.</summary>
    public void Intercept()
    {
        if (_isStopped) return;
        _isStopped = true;
        _agent.isStopped = true;
        StartCoroutine(PlayLaughAndFreeze());
        GameManager.Instance?.ReportSuccess();
    }

    // ── Private ────────────────────────────────────────────────────────────
    private IEnumerator BeginWalkAfterDelay()
    {
        yield return new WaitForSeconds(startDelay);

        if (_isStopped || targetHazard == null) yield break;

        if (laughClip != null && audioSource != null)
            audioSource.PlayOneShot(laughClip);

        if (_agent.isOnNavMesh)
        {
            _agent.SetDestination(targetHazard.transform.position);
            _isMoving = true;
        }
    }

    private IEnumerator PlayLaughAndFreeze()
    {
        // Simple visual feedback: bounce the child
        Vector3 startPos = transform.localPosition;
        float elapsed = 0f;
        while (elapsed < 0.5f)
        {
            elapsed += Time.deltaTime;
            float bounce = Mathf.Sin(elapsed * Mathf.PI * 8f) * 0.05f;
            transform.localPosition = startPos + Vector3.up * bounce;
            yield return null;
        }
        transform.localPosition = startPos;
    }

    private void OnGameStateChanged(GameManager.GameState state)
    {
        if (state != GameManager.GameState.Playing)
        {
            _agent.isStopped = true;
            _isStopped = true;
        }
        else
        {
            // Reset for new scenario
            _isStopped = false;
            _isMoving = false;
            _agent.isStopped = false;
            _agent.ResetPath();
            if (targetHazard != null)
                StartCoroutine(BeginWalkAfterDelay());
        }
    }
}
