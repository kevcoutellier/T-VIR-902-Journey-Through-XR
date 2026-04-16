using System.Collections;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// STOP IT! — ChildNPC
/// The mischievous child. Uses NavMeshAgent to walk toward a HazardZone.
/// The player must intercept them before they reach it.
///
/// UX:
///  • Re-targets dynamically if the hazard moves (editor or runtime).
///  • Plays a bobbing "toddler walk" animation while moving.
///  • Subtly looks toward the hazard.
///  • Reacts to player interception with a cartoon bounce + laugh.
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

    [Header("Dynamic Retargeting")]
    [Tooltip("How often (seconds) to refresh the NavMesh destination if the target moves.")]
    public float repathInterval = 0.15f;

    [Tooltip("Minimum distance the hazard must have moved before we recompute the path.")]
    public float repathThreshold = 0.05f;

    [Header("Animation")]
    [Tooltip("Toddler head-bob amplitude (metres)")]
    public float bobAmplitude = 0.06f;

    [Tooltip("Toddler head-bob frequency (Hz)")]
    public float bobFrequency = 4f;

    [Tooltip("Side-wobble tilt in degrees while walking")]
    public float wobbleAngle = 8f;

    [Tooltip("How fast the child turns toward the target")]
    public float turnSpeed = 6f;

    [Header("Feedback")]
    public AudioSource audioSource;
    public AudioClip laughClip;
    public AudioClip caughtClip;

    // ── Runtime ────────────────────────────────────────────────────────────
    private NavMeshAgent _agent;
    private bool _isMoving = false;
    private bool _isStopped = false;
    private Vector3 _lastTargetPos;
    private float _nextRepathTime;
    private Vector3 _meshStartLocalPos;
    private Transform _meshT; // first child renderer we find, for bob/tilt
    private Coroutine _walkCoroutine;

    public bool IsMoving => _isMoving && !_isStopped;

    // ── Unity ──────────────────────────────────────────────────────────────
    private void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        _agent.speed = walkSpeed;
        _agent.stoppingDistance = 0.3f;
        _agent.angularSpeed = 360f; // more snappy turns, we also hand-rotate

        // Ensure the "Child" tag is set so PlayerBlocker can detect us.
        try { if (!CompareTag("Child")) tag = "Child"; } catch { /* tag not defined yet — fine */ }

        // Pick a mesh child to animate (fallback to self if none).
        var rend = GetComponentInChildren<Renderer>();
        _meshT = rend != null ? rend.transform : transform;
        _meshStartLocalPos = _meshT.localPosition;
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
        {
            _lastTargetPos = targetHazard.transform.position;
            _walkCoroutine = StartCoroutine(BeginWalkAfterDelay());
        }
    }

    private void Update()
    {
        // Keep destination in sync with a potentially moving hazard.
        if (_isMoving && !_isStopped && targetHazard != null)
        {
            if (Time.time >= _nextRepathTime)
            {
                Vector3 tp = targetHazard.transform.position;
                if ((tp - _lastTargetPos).sqrMagnitude > repathThreshold * repathThreshold || !_agent.hasPath)
                {
                    _agent.SetDestination(tp);
                    _lastTargetPos = tp;
                }
                _nextRepathTime = Time.time + repathInterval;
            }

            // Hand-rotate to face the path direction smoothly.
            Vector3 flatVel = _agent.velocity;
            flatVel.y = 0f;
            if (flatVel.sqrMagnitude > 0.01f)
            {
                Quaternion target = Quaternion.LookRotation(flatVel, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, target, turnSpeed * Time.deltaTime);
            }

            // Reached destination
            if (!_agent.pathPending && _agent.remainingDistance <= _agent.stoppingDistance)
            {
                _isMoving = false;
                targetHazard?.TriggerHazard();
            }
        }

        AnimateWalk();
    }

    // ── Public API ─────────────────────────────────────────────────────────
    /// <summary>Called by PlayerBlocker to stop the child.</summary>
    public void Intercept()
    {
        if (_isStopped) return;
        _isStopped = true;
        _isMoving = false;
        if (_agent != null && _agent.isActiveAndEnabled && _agent.isOnNavMesh)
            _agent.isStopped = true;
        StartCoroutine(CartoonBounceAndFreeze());
        if (caughtClip != null && audioSource != null)
            audioSource.PlayOneShot(caughtClip);
        GameManager.Instance?.ReportSuccess();
    }

    /// <summary>How close (0 = far, 1 = touching) the child is to its target.</summary>
    public float GetHazardProximity01(float warningRadius)
    {
        if (targetHazard == null) return 0f;
        float d = Vector3.Distance(transform.position, targetHazard.transform.position);
        return 1f - Mathf.Clamp01(d / Mathf.Max(0.01f, warningRadius));
    }

    // ── Private ────────────────────────────────────────────────────────────
    private IEnumerator BeginWalkAfterDelay()
    {
        yield return new WaitForSeconds(startDelay);

        if (_isStopped || targetHazard == null) yield break;

        // Wait until the agent is on the NavMesh (can take a frame after a warp).
        float waitEnd = Time.time + 1f;
        while (Time.time < waitEnd && (!_agent.isActiveAndEnabled || !_agent.isOnNavMesh))
            yield return null;

        if (laughClip != null && audioSource != null)
            audioSource.PlayOneShot(laughClip);

        _lastTargetPos = targetHazard.transform.position;
        _agent.isStopped = false;
        _agent.SetDestination(_lastTargetPos);
        _isMoving = true;
    }

    private IEnumerator CartoonBounceAndFreeze()
    {
        Vector3 startPos = _meshT.localPosition;
        Quaternion startRot = _meshT.localRotation;
        float elapsed = 0f;
        const float duration = 0.6f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            float bounce = Mathf.Sin(t * Mathf.PI * 6f) * 0.12f * (1f - t);
            float spin = Mathf.Sin(t * Mathf.PI * 8f) * 15f * (1f - t);
            _meshT.localPosition = startPos + Vector3.up * bounce;
            _meshT.localRotation = startRot * Quaternion.Euler(0f, 0f, spin);
            yield return null;
        }
        _meshT.localPosition = startPos;
        _meshT.localRotation = startRot;
    }

    private void AnimateWalk()
    {
        if (_meshT == null) return;
        if (IsMoving)
        {
            float t = Time.time * bobFrequency;
            float bob = Mathf.Abs(Mathf.Sin(t * Mathf.PI)) * bobAmplitude;
            float wobble = Mathf.Sin(t * Mathf.PI) * wobbleAngle;
            _meshT.localPosition = _meshStartLocalPos + Vector3.up * bob;
            _meshT.localRotation = Quaternion.Euler(0f, 0f, wobble);
        }
        else
        {
            // Ease back to rest
            _meshT.localPosition = Vector3.Lerp(_meshT.localPosition, _meshStartLocalPos, Time.deltaTime * 8f);
            _meshT.localRotation = Quaternion.Slerp(_meshT.localRotation, Quaternion.identity, Time.deltaTime * 8f);
        }
    }

    private void OnGameStateChanged(GameManager.GameState state)
    {
        if (state != GameManager.GameState.Playing)
        {
            _isMoving = false;
            _isStopped = true;
            if (_agent != null && _agent.isOnNavMesh) _agent.isStopped = true;
        }
        else
        {
            // Reset for new scenario
            _isStopped = false;
            _isMoving = false;
            if (_agent != null && _agent.isOnNavMesh)
            {
                _agent.isStopped = false;
                _agent.ResetPath();
            }
            if (_walkCoroutine != null) StopCoroutine(_walkCoroutine);
            if (targetHazard != null)
                _walkCoroutine = StartCoroutine(BeginWalkAfterDelay());
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (targetHazard == null) return;
        Gizmos.color = new Color(1f, 0.4f, 0.2f, 0.9f);
        Gizmos.DrawLine(transform.position + Vector3.up * 0.8f, targetHazard.transform.position);
    }
#endif
}
