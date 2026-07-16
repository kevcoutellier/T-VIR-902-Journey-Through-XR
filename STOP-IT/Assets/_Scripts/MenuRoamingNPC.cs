using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// STOP IT! — MenuRoamingNPC
/// Makes an NPC wander idly around the house for the menu background: it walks to
/// a roam point, idles a moment, then picks the next one — forever (until a
/// scenario starts). Two GameObjects with this script = the two characters
/// strolling through the rooms behind the menu.
///
/// Deliberately independent from the gameplay <see cref="ChildNPC"/> so it never
/// interferes with the scenarios: this is purely a NavMeshAgent stroller. It
/// drives an optional Animator "Speed" float (same contract as ChildNPC) and, if
/// no Animator is present, applies a light procedural bob so the walk reads.
///
/// Roaming only happens in <see cref="GameManager.GameState.Menu"/>; when a
/// scenario starts the roamer freezes (and optionally hides), then resumes on the
/// return to menu.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class MenuRoamingNPC : MonoBehaviour
{
    [Header("Roaming")]
    [Tooltip("Points the NPC wanders between. If empty, the NPC samples random " +
             "reachable points on the NavMesh within wanderRadius of its start.")]
    public List<Transform> roamPoints = new List<Transform>();

    [Tooltip("Pick roam points in order (true) or at random (false).")]
    public bool sequential = false;

    [Tooltip("When no roamPoints are assigned, sample random destinations within " +
             "this radius (metres) of the NPC's start position.")]
    [Min(1f)] public float wanderRadius = 6f;

    [Header("Timing")]
    [Tooltip("Walking speed (m/s) while roaming.")]
    [Range(0.3f, 3f)] public float walkSpeed = 1.1f;

    [Tooltip("Min/max seconds to idle at each point before moving on.")]
    public Vector2 idleTime = new Vector2(1.5f, 4f);

    [Tooltip("Distance (m) from a target at which it counts as 'arrived'.")]
    [Min(0.05f)] public float arriveThreshold = 0.4f;

    [Header("Animation (optional)")]
    [Tooltip("Optional Animator on a rigged mesh; its 'Speed' float is driven 0..1. " +
             "Auto-found in children if left null.")]
    public Animator animator;

    [Tooltip("If no Animator drives the walk, apply a small procedural head-bob so " +
             "the stroll reads as movement.")]
    public bool proceduralBobFallback = true;

    [Header("Lifecycle")]
    [Tooltip("Only roam while in the Menu state; freeze during scenarios.")]
    public bool onlyDuringMenu = true;

    [Tooltip("Hide the whole NPC during scenarios (so the menu strollers don't " +
             "appear in-game). Re-shown on return to menu.")]
    public bool hideDuringGameplay = true;

    // ── Runtime ──────────────────────────────────────────────────────────────
    private enum State { Idle, Walking }
    private State _state = State.Idle;
    private NavMeshAgent _agent;
    private float _idleTimer;
    private float _idleDuration;
    private int   _seqIndex = -1;
    private bool  _roaming;
    private bool  _subscribed;
    private Vector3 _startPos;

    // Set by MenuCameraTour in Follow mode to hide all but the active NPC regardless of roam state.
    private bool _cameraForceHidden = false;

    // Procedural bob (only used when no Animator).
    private Transform _meshT;
    private Vector3   _meshStartLocal;
    private float     _bobPhase;
    private static readonly int AnimSpeed = Animator.StringToHash("Speed");

    // ── Unity ──────────────────────────────────────────────────────────────
    private void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        _agent.speed = walkSpeed;
        _agent.stoppingDistance = arriveThreshold;
        _startPos = transform.position;

        if (animator == null) animator = GetComponentInChildren<Animator>(true);

        // For the procedural fallback, animate the first child renderer (never the
        // root — that's NavMeshAgent-driven and would fight the agent).
        if (proceduralBobFallback && (animator == null || animator.runtimeAnimatorController == null))
        {
            var rend = GetComponentInChildren<Renderer>(true);
            if (rend != null && rend.transform != transform)
            {
                _meshT = rend.transform;
                _meshStartLocal = _meshT.localPosition;
            }
        }
    }

    private void Start()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnStateChanged.AddListener(OnStateChanged);
            _subscribed = true;
        }

        bool inMenu = GameManager.Instance == null ||
                      GameManager.Instance.State == GameManager.GameState.Menu;
        SetRoaming(!onlyDuringMenu || inMenu);
    }

    private void OnDestroy()
    {
        if (_subscribed && GameManager.Instance != null)
            GameManager.Instance.OnStateChanged.RemoveListener(OnStateChanged);
    }

    private void OnStateChanged(GameManager.GameState state)
    {
        if (!onlyDuringMenu) return;
        SetRoaming(state == GameManager.GameState.Menu);
    }

    // ── Roam control ────────────────────────────────────────────────────────
    private void SetRoaming(bool roam)
    {
        _roaming = roam;

        if (hideDuringGameplay)
        {
            // Toggle the visual children only; keep this component alive so it
            // keeps listening for the return-to-menu event.
            SetVisible(roam);
        }

        if (_agent != null && _agent.isActiveAndEnabled && _agent.isOnNavMesh)
            _agent.isStopped = !roam;

        if (roam)
        {
            _state = State.Idle;
            _idleTimer = 0f;
            _idleDuration = 0f; // expire immediately → pick a destination next frame
        }
        else if (animator != null)
        {
            animator.SetFloat(AnimSpeed, 0f);
        }
    }

    /// <summary>
    /// Called by MenuCameraTour (Follow mode) to force this NPC hidden or visible
    /// regardless of its roaming state. Takes priority over hideDuringGameplay.
    /// </summary>
    public void SetCameraForceHidden(bool hidden)
    {
        _cameraForceHidden = hidden;
        SetVisible(_roaming && !hidden);
        // Freeze the hidden stroller on the NavMesh so it doesn't keep walking off-screen
        // and appear to "teleport" across the house when the follow camera re-reveals it.
        // Resume exactly where it was when shown again (if still roaming).
        if (_agent != null && _agent.isActiveAndEnabled && _agent.isOnNavMesh)
            _agent.isStopped = hidden || !_roaming;
    }

    private void SetVisible(bool on)
    {
        bool show = on && !_cameraForceHidden;
        foreach (var r in GetComponentsInChildren<Renderer>(true)) r.enabled = show;
    }

    private void Update()
    {
        if (!_roaming || _cameraForceHidden || _agent == null || !_agent.isActiveAndEnabled || !_agent.isOnNavMesh)
            return;

        switch (_state)
        {
            case State.Idle:
                _idleTimer += Time.deltaTime;
                if (_idleTimer >= _idleDuration)
                    GoToNextPoint();
                break;

            case State.Walking:
                if (!_agent.pathPending && _agent.remainingDistance <= _agent.stoppingDistance)
                {
                    _state        = State.Idle;
                    _idleTimer    = 0f;
                    _idleDuration = Random.Range(idleTime.x, idleTime.y);
                }
                break;
        }

        DriveAnimation();
    }

    // ── Movement ──────────────────────────────────────────────────────────────
    private void GoToNextPoint()
    {
        Vector3 dest;
        if (roamPoints != null && roamPoints.Count > 0)
        {
            Transform t = PickRoamPoint();
            if (t == null) return;
            dest = t.position;
        }
        else if (!TryRandomNavPoint(out dest))
        {
            return; // couldn't find a reachable point this frame; retry next Idle tick
        }

        if (NavMesh.SamplePosition(dest, out NavMeshHit hit, 2f, NavMesh.AllAreas))
            dest = hit.position;

        _agent.isStopped = false;
        _agent.SetDestination(dest);
        _state = State.Walking;
    }

    private Transform PickRoamPoint()
    {
        if (sequential)
        {
            _seqIndex = (_seqIndex + 1) % roamPoints.Count;
            return roamPoints[_seqIndex];
        }
        // Random, avoiding an immediate repeat when possible.
        if (roamPoints.Count == 1) return roamPoints[0];
        int idx;
        int guard = 0;
        do { idx = Random.Range(0, roamPoints.Count); }
        while (idx == _seqIndex && ++guard < 8);
        _seqIndex = idx;
        return roamPoints[idx];
    }

    private bool TryRandomNavPoint(out Vector3 result)
    {
        for (int i = 0; i < 8; i++)
        {
            Vector3 candidate = _startPos + Random.insideUnitSphere * wanderRadius;
            candidate.y = _startPos.y;
            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, wanderRadius, NavMesh.AllAreas))
            {
                result = hit.position;
                return true;
            }
        }
        result = transform.position;
        return false;
    }

    // ── Animation ─────────────────────────────────────────────────────────────
    private void DriveAnimation()
    {
        float speed01 = walkSpeed > 0.01f
            ? Mathf.Clamp01(_agent.velocity.magnitude / walkSpeed)
            : 0f;

        if (animator != null && animator.runtimeAnimatorController != null)
        {
            animator.SetFloat(AnimSpeed, speed01);
            return;
        }

        // Procedural fallback bob — only the mesh child, never the agent-driven root.
        if (_meshT == null) return;
        if (speed01 > 0.05f)
        {
            _bobPhase += Time.deltaTime * 5f * speed01;
            float bob = Mathf.Abs(Mathf.Sin(_bobPhase)) * 0.04f;
            float roll = Mathf.Sin(_bobPhase * 0.5f) * 6f;
            _meshT.localPosition = _meshStartLocal + Vector3.up * bob;
            _meshT.localRotation = Quaternion.Euler(0f, 0f, roll);
        }
        else
        {
            _meshT.localPosition = Vector3.Lerp(_meshT.localPosition, _meshStartLocal, Time.deltaTime * 8f);
            _meshT.localRotation = Quaternion.Slerp(_meshT.localRotation, Quaternion.identity, Time.deltaTime * 8f);
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.7f, 0.2f, 0.9f);
        if (roamPoints != null && roamPoints.Count > 0)
        {
            foreach (var p in roamPoints)
                if (p != null) Gizmos.DrawWireSphere(p.position, 0.3f);
        }
        else
        {
            Vector3 c = Application.isPlaying ? _startPos : transform.position;
            Gizmos.DrawWireSphere(c, wanderRadius);
        }
    }
#endif
}
