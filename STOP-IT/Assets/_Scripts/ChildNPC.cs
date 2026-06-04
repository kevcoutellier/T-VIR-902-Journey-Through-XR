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
// Run AFTER ScenarioManager (-50) so targetHazard is already up-to-date when we react to state changes.
[DefaultExecutionOrder(0)]
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

    [Header("Scenario Visuals")]
    [Tooltip("Optional cosmetic skateboard mesh parented under the NPC. Toggled on for the stairs scenario only.")]
    public GameObject skateboardVisual;

    [Header("Save rules")]
    [Tooltip("If false, the child cannot be saved by directly grabbing OR touching them — the scenario " +
             "must be solved another way (e.g. take the cat, close the window). Set per-scenario by " +
             "ScenarioManager from ScenarioConfig.disableDirectChildSave.")]
    public bool canBeSavedDirectly = true;

    // ── Runtime ────────────────────────────────────────────────────────────
    private NavMeshAgent _agent;
    private bool _isMoving = false;
    private bool _isStopped = false;
    private bool _held = false;
    private bool _forceWalkNow = false;
    private Transform _originalParent;
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

        // Find (or create) a dedicated child "MeshHolder" that hosts the visual
        // mesh. We NEVER animate the root transform — that transform is driven
        // by NavMeshAgent and any position/rotation tweak we apply here would
        // fight the agent (and the child would appear stuck).
        _meshT = FindOrCreateMeshHolder();
        _meshStartLocalPos = _meshT.localPosition;

        // Auto-create the skateboard placeholder (parented under the NPC, disabled
        // by default — ScenarioManager toggles it via SetSkateboardVisible).
        if (skateboardVisual == null) skateboardVisual = CreateSkateboardVisual();
        if (skateboardVisual != null) skateboardVisual.SetActive(false);
    }

    private GameObject CreateSkateboardVisual()
    {
        var existing = transform.Find("SkateboardMount");
        if (existing != null) return existing.gameObject;

        var skate = GameObject.CreatePrimitive(PrimitiveType.Cube);
        skate.name = "SkateboardMount";
        var col = skate.GetComponent<Collider>();
        if (col != null) Destroy(col); // cosmetic only, no physics
        skate.transform.SetParent(transform, false);
        skate.transform.localPosition = new Vector3(0f, -0.45f, 0f); // just under the toddler's feet
        skate.transform.localRotation = Quaternion.identity;
        skate.transform.localScale = new Vector3(0.30f, 0.05f, 0.70f);
        return skate;
    }

    /// <summary>
    /// Ensures the visual mesh lives on a dedicated child transform that we own.
    /// If the root GameObject has MeshFilter/MeshRenderer, we move them onto
    /// a child "MeshHolder" so animating it doesn't fight the NavMeshAgent.
    /// </summary>
    private Transform FindOrCreateMeshHolder()
    {
        // Already a named holder?
        var existing = transform.Find("MeshHolder");
        if (existing != null) return existing;

        // Any existing child renderer will do.
        var childRend = GetComponentInChildren<Renderer>(true);
        if (childRend != null && childRend.transform != transform) return childRend.transform;

        // Nothing suitable — migrate the root mesh to a child.
        var holderGO = new GameObject("MeshHolder");
        holderGO.transform.SetParent(transform, false);
        holderGO.transform.localPosition = Vector3.zero;
        holderGO.transform.localRotation = Quaternion.identity;
        holderGO.transform.localScale = Vector3.one;

        var rootFilter = GetComponent<MeshFilter>();
        var rootRenderer = GetComponent<MeshRenderer>();
        if (rootFilter != null && rootRenderer != null)
        {
            var mesh = rootFilter.sharedMesh;
            var mats = rootRenderer.sharedMaterials;

            // Destroy originals and re-create on the holder so we own them cleanly.
            if (Application.isPlaying)
            {
                Destroy(rootRenderer);
                Destroy(rootFilter);
            }
            else
            {
                DestroyImmediate(rootRenderer);
                DestroyImmediate(rootFilter);
            }

            var newFilter = holderGO.AddComponent<MeshFilter>();
            newFilter.sharedMesh = mesh;
            var newRenderer = holderGO.AddComponent<MeshRenderer>();
            newRenderer.sharedMaterials = mats;
        }
        return holderGO.transform;
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

    /// <summary>
    /// Temporarily pauses movement (used by WindowOpener while the child "opens" the window).
    /// Does NOT set _isStopped, so Intercept/Grab still work, and ResumeWalk can restart movement.
    /// </summary>
    public void PauseWalk()
    {
        if (_isStopped) return;
        _isMoving = false;
        if (_agent != null && _agent.isActiveAndEnabled && _agent.isOnNavMesh)
            _agent.isStopped = true;
    }

    /// <summary>Resumes movement after PauseWalk(). Optionally retargets to a new hazard zone.</summary>
    public void ResumeWalk(HazardZone newTarget = null)
    {
        if (_isStopped) return;  // truly stopped (intercepted/grabbed) — don't restart
        if (newTarget != null)
        {
            targetHazard = newTarget;
            _lastTargetPos = Vector3.positiveInfinity; // force repath
        }
        if (_agent != null && _agent.isActiveAndEnabled && _agent.isOnNavMesh)
            _agent.isStopped = false;
        _isMoving = true;
    }

    /// <summary>Called by PlayerBlocker to stop the child.</summary>
    public void Intercept()
    {
        if (_isStopped) return;
        // Some scenarios (cat, window) forbid saving the child by touch/grab —
        // the player must solve them another way. Ignore the reflex slap there.
        if (!canBeSavedDirectly) return;
        _isStopped = true;
        _isMoving = false;
        if (_agent != null && _agent.isActiveAndEnabled && _agent.isOnNavMesh)
            _agent.isStopped = true;
        StartCoroutine(CartoonBounceAndFreeze());
        if (caughtClip != null && audioSource != null)
            audioSource.PlayOneShot(caughtClip);
        GameManager.Instance?.ReportSuccess();
    }

    /// <summary>
    /// Called by ChildGrabber when the player grips near the child.
    /// One-shot: grabbing the baby IS the win — the scenario succeeds immediately,
    /// the baby is parented to the player's hand for a satisfying "saved them" beat.
    /// Returns true if the grab actually took (false when grabbing is disabled for
    /// the current scenario, or the child is already held/stopped) — the caller uses
    /// this so it doesn't latch onto a child it isn't really holding.
    /// </summary>
    public bool Grab(Transform attachPoint)
    {
        if (_isStopped || _held || attachPoint == null) return false;
        // Cat / window scenarios forbid the direct grab — force the player to use
        // the scenario verb instead.
        if (!canBeSavedDirectly) return false;
        _isStopped = true;
        _isMoving = false;
        _held = true;

        // Stop pathing first so we can safely re-parent.
        if (_agent != null && _agent.isActiveAndEnabled)
        {
            if (_agent.isOnNavMesh) _agent.isStopped = true;
            _agent.enabled = false;
        }

        // Disable our colliders so the player's wall/ground sweeps don't catch
        // the toddler hanging from their hand (which used to push the player
        // through the floor when grabbed on the stairs).
        SetCollidersEnabled(false);

        _originalParent = transform.parent;
        transform.SetParent(attachPoint, worldPositionStays: false);
        transform.localPosition = new Vector3(0f, -0.4f, 0.15f); // dangle slightly below the hand
        transform.localRotation = Quaternion.identity;

        // Gentle left-right sway while held (slow, low amplitude). Replaces the
        // older cartoon bounce, which was a fast vertical bob + Z-axis spin and
        // read as "vibration" when E was held down.
        if (_heldSwayCoroutine != null) StopCoroutine(_heldSwayCoroutine);
        _heldSwayCoroutine = StartCoroutine(HeldSway());

        if (caughtClip != null && audioSource != null)
            audioSource.PlayOneShot(caughtClip);

        GameManager.Instance?.ReportSuccess();
        return true;
    }

    private Coroutine _heldSwayCoroutine;

    private IEnumerator HeldSway()
    {
        Vector3 startPos = _meshT.localPosition;
        Quaternion startRot = _meshT.localRotation;
        // Slow pendulum: ~3° peak amplitude, ~0.4 Hz (≈2.5s per cycle).
        const float amplitudeDeg = 3f;
        const float frequencyHz = 0.4f;
        while (_held)
        {
            float angle = Mathf.Sin(Time.time * Mathf.PI * 2f * frequencyHz) * amplitudeDeg;
            _meshT.localPosition = startPos;
            _meshT.localRotation = startRot * Quaternion.Euler(0f, 0f, angle);
            yield return null;
        }
        _meshT.localPosition = startPos;
        _meshT.localRotation = startRot;
    }

    private void SetCollidersEnabled(bool on)
    {
        foreach (var col in GetComponentsInChildren<Collider>(includeInactive: true))
            col.enabled = on;
    }

    /// <summary>
    /// Called by ChildGrabber when the player releases the grip.
    /// Detaches the baby but leaves it where it is — the scenario was already won on Grab.
    /// </summary>
    public void Release()
    {
        if (!_held) return;
        _held = false;
        transform.SetParent(_originalParent, worldPositionStays: true);
        SetCollidersEnabled(true);

        // Re-bind onto the NavMesh: Warp() puts the agent at the correct baseOffset
        // above the floor and keeps the NPC drivable if released mid-scenario.
        // The previous raycast snap could hit the NPC's own collider (mask=~0) or
        // ignore the agent's baseOffset, causing the body to sink into the floor.
        if (_agent != null)
        {
            _agent.enabled = true;
            if (NavMesh.SamplePosition(transform.position, out NavMeshHit navHit, 3f, NavMesh.AllAreas))
                _agent.Warp(navHit.position);
        }
        else
        {
            // Fallback: raycast from well above, excluding the NPC's own colliders.
            var pos = transform.position;
            int mask = ~(1 << gameObject.layer);
            if (Physics.Raycast(pos + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 10f, mask, QueryTriggerInteraction.Ignore))
                pos.y = hit.point.y;
            transform.position = pos;
        }
    }

    public bool IsHeld => _held;

    /// <summary>Toggles the cosmetic skateboard mesh (stairs scenario only).</summary>
    public void SetSkateboardVisible(bool on)
    {
        if (skateboardVisual != null) skateboardVisual.SetActive(on);
    }

    // ── Carried item ───────────────────────────────────────────────────────
    private GameObject _carriedItem;
    private Transform _carriedItemOriginalParent;
    private Vector3 _carriedItemOriginalLocalPos;
    private Quaternion _carriedItemOriginalLocalRot;
    private Vector3 _carriedItemOriginalLocalScale;
    private bool _carriedItemWasActive;

    /// <summary>
    /// Parents the given GameObject to the NPC at the given local pose, so it
    /// visually "follows" the toddler (fork in scenario 1, cat in scenario 2,
    /// skateboard under the feet in scenario 4…). Passing `null` releases the
    /// previously carried item back to its original parent / pose.
    /// </summary>
    public void SetCarriedItem(GameObject item, Vector3 localPos, Vector3 localEuler)
    {
        // Restore previously carried item to its original transform.
        if (_carriedItem != null)
        {
            _carriedItem.transform.SetParent(_carriedItemOriginalParent, worldPositionStays: false);
            _carriedItem.transform.localPosition = _carriedItemOriginalLocalPos;
            _carriedItem.transform.localRotation = _carriedItemOriginalLocalRot;
            _carriedItem.transform.localScale    = _carriedItemOriginalLocalScale;
            _carriedItem.SetActive(_carriedItemWasActive);
            _carriedItem = null;
        }

        if (item == null) return;

        _carriedItem = item;
        _carriedItemOriginalParent    = item.transform.parent;
        _carriedItemOriginalLocalPos  = item.transform.localPosition;
        _carriedItemOriginalLocalRot  = item.transform.localRotation;
        _carriedItemOriginalLocalScale = item.transform.localScale;
        _carriedItemWasActive         = item.activeSelf;

        item.transform.SetParent(transform, worldPositionStays: false);
        item.transform.localPosition = localPos;
        item.transform.localRotation = Quaternion.Euler(localEuler);
        item.SetActive(true);
    }

    /// <summary>
    /// Called by ScenarioManager BEFORE warping the child to a new spawn point.
    /// Forces detachment from any held parent and re-enables the NavMeshAgent.
    /// </summary>
    /// <summary>
    /// Called by WindowInteractable (scenario 6) to bypass the start delay and walk immediately.
    /// Safe to call even if the child hasn't started the delay countdown yet.
    /// </summary>
    public void ForceStartWalk() => _forceWalkNow = true;

    public void ResetForScenario()
    {
        _forceWalkNow = false;
        if (_held)
        {
            _held = false;
            transform.SetParent(_originalParent, worldPositionStays: false);
        }
        SetCollidersEnabled(true);
        if (_agent != null && !_agent.enabled) _agent.enabled = true;
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
        // Count down the start delay, but WindowInteractable can bypass it via ForceStartWalk().
        float elapsed = 0f;
        while (elapsed < startDelay && !_forceWalkNow)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }
        _forceWalkNow = false;

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
        Debug.Log($"[ChildNPC] Walking toward '{targetHazard.gameObject.name}' at {_lastTargetPos} " +
                  $"(agent.isOnNavMesh={_agent.isOnNavMesh}, hasPath={_agent.hasPath}, pathStatus={_agent.pathStatus})", this);
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
            // Reset for new scenario. We wait one frame before starting the walk
            // so that ScenarioManager (running earlier in the frame, but whose
            // listener registration order is undefined) has had time to update
            // targetHazard and warp us onto the right NavMesh location.

            // If we were being held (won previous scenario via grab), detach now
            // so ScenarioManager.Warp can put us back on the NavMesh.
            if (_held)
            {
                _held = false;
                transform.SetParent(_originalParent, worldPositionStays: false);
            }

            // Re-enable the agent if Grab() disabled it on the previous round.
            if (_agent != null && !_agent.enabled) _agent.enabled = true;

            _isStopped = false;
            _isMoving = false;
            if (_agent != null && _agent.isOnNavMesh)
            {
                _agent.isStopped = false;
                _agent.ResetPath();
            }
            if (_walkCoroutine != null) StopCoroutine(_walkCoroutine);
            _walkCoroutine = StartCoroutine(WaitOneFrameThenWalk());
        }
    }

    private IEnumerator WaitOneFrameThenWalk()
    {
        // Yield two frames: one for ScenarioManager.ActivateScenario, one for NavMeshAgent.Warp to settle.
        yield return null;
        yield return null;
        if (targetHazard == null) yield break;
        yield return BeginWalkAfterDelay();
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
