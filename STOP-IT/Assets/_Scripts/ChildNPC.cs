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
    // ── Reactions ──────────────────────────────────────────────────────────
    /// <summary>Procedural reactions the child can play. Extend as scenarios grow.</summary>
    public enum ChildReaction
    {
        Electrocuted,  // scenario 1 — fork in the outlet (fully implemented)
        SkateWipeout,  // scenario 3 — skateboard down the stairs (stub)
        Stumble        // generic trip (stub)
    }

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
    [Tooltip("Toddler head-bob amplitude (metres). Legacy fallback used when stepBobHeight is left at 0.")]
    public float bobAmplitude = 0.06f;

    [Tooltip("Toddler head-bob frequency (Hz). Legacy fallback used when stepRate is left at 0.")]
    public float bobFrequency = 4f;

    [Tooltip("Side-wobble tilt in degrees while walking. Legacy fallback used when waddleRollDeg is left at 0.")]
    public float wobbleAngle = 8f;

    [Tooltip("How fast the child turns toward the target")]
    public float turnSpeed = 6f;

    [Header("Toddler Walk")]
    [Tooltip("Steps per second of the accumulated walk phase. Drives both bob and waddle cadence.")]
    [SerializeField] private float stepRate = 2.2f;

    [Tooltip("Peak vertical bob per step (metres). Two bounces per phase cycle.")]
    [SerializeField] private float stepBobHeight = 0.05f;

    [Tooltip("Lateral waddle translation amplitude (metres), at half the bob cadence.")]
    [SerializeField] private float waddleSideAmount = 0.025f;

    [Tooltip("Lateral waddle roll (Z) amplitude in degrees, at half the bob cadence.")]
    [SerializeField] private float waddleRollDeg = 9f;

    [Tooltip("Forward lean (X tilt) in degrees at full speed.")]
    [SerializeField] private float forwardLeanDeg = 6f;

    [Tooltip("How fast the walk animation weight eases in/out (units per second).")]
    [SerializeField] private float walkWeightLerp = 6f;

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

    [Header("Reactions")]
    [Tooltip("Duration of the Electrocuted reaction. Match the HazardZone fail beat (~0.6s red-flash + shake).")]
    [SerializeField] private float electrocuteDuration = 0.6f;

    [Tooltip("High-frequency rigid jitter amplitude during electrocution (metres).")]
    [SerializeField] private float electrocuteShakeAmp = 0.02f;

    [Tooltip("Stiff twitchy roll/pitch amplitude during electrocution (degrees).")]
    [SerializeField] private float electrocuteTwistDeg = 4f;

    [Tooltip("If a Renderer is found on the mesh holder, pulse its emissive white during electrocution.")]
    [SerializeField] private bool electrocuteEmissiveFlash = true;

    [Header("Animator (optional)")]
    [Tooltip("Optional Animator on a rigged mesh. Left null → purely procedural; assigning one drives " +
             "Speed/Electrocute/React in parallel without disabling the procedural fallback.")]
    [SerializeField] private Animator animator;

    // ── Runtime ────────────────────────────────────────────────────────────
    private NavMeshAgent _agent;
    private bool _isMoving = false;
    private Renderer[] _bodyRenderers;
    private MaterialPropertyBlock _tintMpb;
    private Coroutine _arrivalRoutine; // current pickup / hazard-arrival beat; cancelled on any scenario change
    public static bool S3ProductGrabbed; // true once the toddler has the poison bottle in hand → swap deadline passed
    private bool _isStopped = false;
    private bool _held = false;
    private bool _catchArmed = true;   // is the catch live right now? Gated until pickup/mount for the catch scenarios (S1/S4).
    private string _pendingCatchHint;  // action hint revealed the moment the catch arms.
    private bool _forceWalkNow = false;
    private Transform _originalParent;
    private Vector3 _lastTargetPos;
    private float _nextRepathTime;
    private Vector3 _meshStartLocalPos;
    private Transform _meshT; // first child renderer we find, for bob/tilt
    private Coroutine _walkCoroutine;
    private Coroutine _reactionCoroutine;

    // Toddler-walk procedural state.
    private float _walkPhase;   // accumulated step phase (NOT global Time → no snap on start/stop)
    private float _walkWeight;  // 0..1 eased blend so the gait fades in/out smoothly

    // Optional emissive flash during electrocution (reuses a shared property block — no alloc).
    private Renderer _meshRenderer;
    private MaterialPropertyBlock _meshMpb;
    private static readonly int EmissionColorProp = Shader.PropertyToID("_EmissionColor");

    // Animator parameter hashes. Names are part of the contract a future controller binds to.
    private static readonly int AnimSpeed = Animator.StringToHash("Speed");
    private static readonly int AnimElectrocute = Animator.StringToHash("Electrocute");
    private static readonly int AnimReact = Animator.StringToHash("React");
    private static readonly int AnimPickUp = Animator.StringToHash("PickUp");
    private static readonly int AnimPutFork = Animator.StringToHash("PutFork");
    private static readonly int AnimPutCat = Animator.StringToHash("PutCat");
    private static readonly int AnimDrink = Animator.StringToHash("Drink");
    private static readonly int AnimDie = Animator.StringToHash("Die");
    private static readonly int AnimJumpSkate = Animator.StringToHash("JumpSkate");
    private static readonly int AnimFallDeath = Animator.StringToHash("FallDeath");
    private static readonly int AnimClimb = Animator.StringToHash("Climb");
    private static readonly int AnimFallFlat = Animator.StringToHash("FallFlat");
    private static readonly int AnimFall = Animator.StringToHash("Fall");
    private static readonly int AnimSurprised = Animator.StringToHash("Surprised");
    private static readonly int AnimCarried = Animator.StringToHash("Carried");

    [Header("Story animation timing")]
    [Tooltip("Pause (s) at the pickup waypoint while the PickUp animation plays before the item attaches.")]
    [SerializeField] private float pickupAnimDuration = 1.3f;
    [Tooltip("Pause (s) at the hazard while the PutFork animation plays before the electrocution/fail.")]
    [SerializeField] private float putForkAnimDuration = 1.0f;
    [Tooltip("Pause (s) at the microwave while the PutCat animation plays before the cat is placed inside.")]
    [SerializeField] private float putCatAnimDuration = 1.0f;
    [Tooltip("Pause (s) at the cleaning products while the drink animation plays (the swap window).")]
    [SerializeField] private float drinkAnimDuration = 3.0f;
    [Tooltip("Pause (s) for the poisoned death animation before the loss is reported.")]
    [SerializeField] private float dieAnimDuration = 2.0f;
    [Tooltip("Pause (s) for the jump-onto-skateboard animation before the ride starts.")]
    [SerializeField] private float jumpSkateDuration = 1.0f;
    [Tooltip("Pause (s) for the wall-hit death animation before the loss is reported.")]
    [SerializeField] private float fallDeathDuration = 2.0f;
    [Tooltip("Time (s) the toddler spends climbing the window — the player must close it before this elapses.")]
    [SerializeField] private float climbAnimDuration = 3.0f;
    [Tooltip("Pause (s) for the ground-crash animation before the loss is reported.")]
    [SerializeField] private float fallFlatDuration = 2.0f;
    [Tooltip("Air time (s) of the fall OUT the window (mid-air FallingBaby) before the ground crash.")]
    [SerializeField] private float fallTravelDuration = 1.2f;
    [Tooltip("How far past the window (m) the toddler lands outside.")]
    [SerializeField] private float windowFallOutDistance = 1.5f;
    [Tooltip("Fallback drop height (m) if no ground is found below the window.")]
    [SerializeField] private float windowFallDropHeight = 5.0f;
    private SkateboardRide _skateboardRide;
    private GameObject[] _s3Products;
    private Vector3 _s3ItemLocalPos = new Vector3(0f, 0f, 0.04f);
    private Vector3 _s3ItemLocalEuler;
    [Tooltip("When (s into the pickup) the item transfers from the floor to the hand — the 'grab' moment (~half the clip).")]
    [SerializeField] private float pickupAttachDelay = 0.65f;

    public bool IsMoving => _isMoving && !_isStopped;

    // ── Unity ──────────────────────────────────────────────────────────────
    private void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        _agent.speed = walkSpeed;
        _agent.stoppingDistance = 0.3f;
        _agent.angularSpeed = 360f; // more snappy turns, we also hand-rotate
        // ChildNPC fully owns rotation: hand-rotate toward velocity while walking (Update) and
        // FaceToward the fork/cat/outlet/microwave at pickup & hazard. If the agent kept
        // updateRotation on, it would overwrite FaceToward every frame and the child would
        // face its arrival direction instead of the appliance (the "not facing the microwave" bug).
        _agent.updateRotation = false;

        // Ensure the "Child" tag is set so PlayerBlocker can detect us.
        try { if (!CompareTag("Child")) tag = "Child"; } catch { /* tag not defined yet — fine */ }

        // Find (or create) a dedicated child "MeshHolder" that hosts the visual
        // mesh. We NEVER animate the root transform — that transform is driven
        // by NavMeshAgent and any position/rotation tweak we apply here would
        // fight the agent (and the child would appear stuck).
        _meshT = FindOrCreateMeshHolder();
        _meshStartLocalPos = _meshT.localPosition;

        // Cache a renderer + property block for the optional electrocution flash.
        // No material instancing — we only push an emissive colour via the block.
        _meshRenderer = _meshT.GetComponent<Renderer>();
        if (_meshRenderer == null) _meshRenderer = GetComponentInChildren<Renderer>(true);
        _meshMpb = new MaterialPropertyBlock();

        // Optional rigged Animator — purely additive. When absent (current art),
        // nothing changes and the procedural animation is the sole driver.
        if (animator == null) animator = GetComponentInChildren<Animator>(true);

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

    private bool _subscribedToGm;

    private void OnEnable()
    {
        TrySubscribeToGameManager();
    }

    private void OnDisable()
    {
        if (_subscribedToGm && GameManager.Instance != null)
            GameManager.Instance.OnStateChanged.RemoveListener(OnGameStateChanged);
        _subscribedToGm = false;
    }

    /// <summary>
    /// Subscribe to GameManager state changes — called from BOTH OnEnable and Start. At OnEnable
    /// time GameManager.Instance may not exist yet (Awake order between same-execution-order objects
    /// is undefined), so the OnEnable attempt can silently no-op. Without the Start fallback the
    /// child would NEVER receive the Playing event on restart and would stay planted after a fail.
    /// Guarded so it only subscribes once.
    /// </summary>
    private void TrySubscribeToGameManager()
    {
        if (_subscribedToGm || GameManager.Instance == null) return;
        GameManager.Instance.OnStateChanged.AddListener(OnGameStateChanged);
        _subscribedToGm = true;
    }

    private void Start()
    {
        TrySubscribeToGameManager(); // fallback in case GameManager wasn't ready at OnEnable
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
                Vector3 tp = CurrentDestination();
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

            // Reached the current leg target.
            if (!_agent.pathPending && _agent.remainingDistance <= _agent.stoppingDistance)
            {
                if (_walkingToPickup)
                {
                    // Arrived at the pickup waypoint: play the PickUp animation, then attach & continue.
                    _walkingToPickup = false;
                    _isMoving = false;
                    _arrivalRoutine = StartCoroutine(PickupRoutine());
                }
                else
                {
                    // Arrived at the hazard: play the PutFork beat, then the zap / fail.
                    _isMoving = false;
                    _arrivalRoutine = StartCoroutine(HazardArrivalRoutine());
                }
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
        if (!_catchArmed) return; // not catchable until the child has committed (fork / skate)
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
        // The catch scenarios (fork / skateboard) arm the catch only once the child commits.
        if (!_catchArmed) return false;
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
        // Rigged: play the Surprised reaction then the Carried loop. Otherwise procedural sway.
        if (animator != null) { animator.SetTrigger(AnimSurprised); animator.SetBool(AnimCarried, true); }
        if (animator == null || animator.runtimeAnimatorController == null)
        {
            if (_heldSwayCoroutine != null) StopCoroutine(_heldSwayCoroutine);
            _heldSwayCoroutine = StartCoroutine(HeldSway());
        }

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
        if (animator != null) animator.SetBool(AnimCarried, false);
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
    public bool IsCatchable => _catchArmed; // false until the child commits (picks up the fork / mounts the skate).

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
    public void SetCarriedItem(GameObject item, Vector3 localPos, Vector3 localEuler, bool useLeftHand = false)
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
        Vector3 worldScale = item.transform.lossyScale;

        Transform attach = GetCarryAttach(useLeftHand);
        item.transform.SetParent(attach, worldPositionStays: false);
        item.transform.localPosition = localPos;
        item.transform.localRotation = Quaternion.Euler(localEuler);
        // Keep the item's authored world size regardless of the (scaled) hand bone it hangs off.
        Vector3 pls = attach.lossyScale;
        item.transform.localScale = new Vector3(
            worldScale.x / Mathf.Max(1e-4f, pls.x),
            worldScale.y / Mathf.Max(1e-4f, pls.y),
            worldScale.z / Mathf.Max(1e-4f, pls.z));
        item.SetActive(true);
    }

    /// <summary>Hands the currently-carried item to an external owner (e.g. CatGrab) WITHOUT restoring it
    /// to its bed. Detaches it from the hand keeping world pose and clears tracking, so the next scenario
    /// activation's SetCarriedItem(null) won't auto-return it. The new owner manages it from now on.</summary>
    public void ForgetCarriedItem()
    {
        if (_carriedItem != null) _carriedItem.transform.SetParent(null, worldPositionStays: true);
        _carriedItem = null;
    }

    /// <summary>The transform a carried item attaches to: the right-hand bone (so the item follows
    /// the arm during walk/pickup), falling back to the NPC root if no Humanoid hand is available.</summary>
    private Transform GetCarryAttach(bool useLeftHand = false)
    {
        if (animator != null && animator.isHuman)
        {
            var hand = animator.GetBoneTransform(useLeftHand ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            if (hand != null) return hand;
        }
        return transform;
    }

    // ── Pickup waypoint (two-leg walk: fetch an item, then head to the hazard) ─
    private Transform _pickupWaypoint;
    private GameObject _pickupItem;
    private Vector3 _pickupItemLocalPos = new Vector3(0.12f, 0.48f, 0.22f);
    private Vector3 _pickupItemLocalEuler;
    private bool _walkingToPickup;

    /// <summary>
    /// Arms a two-leg walk: the child first walks to <paramref name="waypoint"/>, picks up
    /// <paramref name="item"/> there, then continues to the hazard. Pass a null waypoint or
    /// item to disable the pickup leg (walk straight to the hazard — legacy behaviour).
    /// Called by ScenarioManager.ActivateScenario from ScenarioConfig.
    /// </summary>
    public void SetPickup(Transform waypoint, GameObject item, Vector3 localPos, Vector3 localEuler)
    {
        _pickupWaypoint = waypoint;
        _pickupItem = item;
        _pickupItemLocalPos = localPos;
        _pickupItemLocalEuler = localEuler;
        _walkingToPickup = waypoint != null && item != null;
    }

    /// <summary>Scenario 3: the cleaning products the child can grab + drink. On arrival it grabs the
    /// nearest VISIBLE one into its hand (so the drink reads as drinking the product).</summary>
    public void SetCleaningProducts(GameObject[] products, Vector3 localPos, Vector3 localEuler)
    {
        _s3Products = products;
        _s3ItemLocalPos = localPos;
        _s3ItemLocalEuler = localEuler;
    }

    /// <summary>Scenario 4: the skateboard the child rides down the stairs (set on activation).</summary>
    public void SetSkateboard(SkateboardRide ride) { _skateboardRide = ride; }

    /// <summary>Scenario setup: for catch-after-progress scenarios (S1 fork, S4 skate), keep the catch
    /// (and its action hint) disabled until the child commits. Other scenarios are catchable at once.</summary>
    public void ConfigureCatchGate(bool gateUntilProgress, string hintWhenArmed)
    {
        _catchArmed = !gateUntilProgress;
        _pendingCatchHint = hintWhenArmed;
    }

    /// <summary>Arms the catch (and reveals its hint) the moment the child picks up the fork / mounts the skate.</summary>
    private void ArmCatch()
    {
        if (_catchArmed) return; // already live (non-gated scenario)
        _catchArmed = true;
        if (!string.IsNullOrEmpty(_pendingCatchHint))
        {
            var ui = FindAnyObjectByType<ScenarioUI>();
            if (ui != null) ui.SetActionHint(_pendingCatchHint);
        }
    }

    /// <summary>Called by SkateboardRide when the toddler reaches the bottom of the stairs UNCAUGHT:
    /// it slams the wall, falls backwards, and we lose.</summary>
    public void HitWallDeath()
    {
        if (_isStopped) return;
        StartCoroutine(HitWallDeathRoutine());
    }
    private IEnumerator HitWallDeathRoutine()
    {
        _isStopped = true;
        _isMoving = false;
        if (animator != null) { animator.SetTrigger(AnimFallDeath); animator.Play("FallDeath", 0, 0f); }
        yield return new WaitForSeconds(fallDeathDuration);
        GameManager.Instance?.ReportFail();
    }

    private GameObject NearestVisibleProduct()
    {
        if (_s3Products == null) return null;
        GameObject best = null; float bestSqr = float.MaxValue;
        foreach (var p in _s3Products)
        {
            if (p == null || !p.activeInHierarchy) continue;
            float sqr = (p.transform.position - transform.position).sqrMagnitude;
            if (sqr < bestSqr) { bestSqr = sqr; best = p; }
        }
        return best;
    }

    /// <summary>Parents a product's cap (B_product*_body → B_product*_cap) under its body so the cap stays
    /// attached to the bottle as the child grabs + drinks it (it keeps its world pose on the neck and
    /// follows the bottle into the hand). The bottle reads as a real, closed product bottle.</summary>
    private void AttachProductCap(GameObject body)
    {
        if (body == null || !body.name.EndsWith("_body")) return;
        var cap = GameObject.Find(body.name.Substring(0, body.name.Length - 5) + "_cap");
        if (cap != null) cap.transform.SetParent(body.transform, worldPositionStays: true);
    }

    /// <summary>Current navigation leg target: the pickup waypoint until reached, then the hazard.</summary>
    private Vector3 CurrentDestination()
    {
        if (_walkingToPickup && _pickupWaypoint != null) return _pickupWaypoint.position;
        return targetHazard != null ? targetHazard.transform.position : transform.position;
    }

    /// <summary>At the pickup waypoint: pause, play the PickUp animation, attach the item, then resume to the hazard.</summary>
    /// <summary>Instantly turns the child to face a world position (XZ only).</summary>
    private void FaceToward(Vector3 worldPos)
    {
        Vector3 dir = worldPos - transform.position; dir.y = 0f;
        if (dir.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
    }

    private IEnumerator PickupRoutine()
    {
        if (_agent != null && _agent.isActiveAndEnabled && _agent.isOnNavMesh) _agent.isStopped = true;
        if (_pickupItem != null) FaceToward(_pickupItem.transform.position); // face the fork/cat
        if (animator != null) animator.SetTrigger(AnimPickUp);

        // The fork stays on the floor while the child bends down, then transfers to the hand at the
        // "grab" moment (~half the clip); the child finishes standing and walks off with it.
        float attach = Mathf.Clamp(pickupAttachDelay, 0f, pickupAnimDuration);
        yield return new WaitForSeconds(attach);
        if (_held || _isStopped) yield break;
        if (_pickupItem != null)
            SetCarriedItem(_pickupItem, _pickupItemLocalPos, _pickupItemLocalEuler);
        yield return new WaitForSeconds(pickupAnimDuration - attach);
        if (_held || _isStopped) yield break;   // player caught the child during the pickup
        ArmCatch(); // fork is in hand → the catch (and its hint) goes live for the walk to the hazard

        if (targetHazard != null)
        {
            _lastTargetPos = targetHazard.transform.position;
            if (_agent != null && _agent.isActiveAndEnabled && _agent.isOnNavMesh)
            {
                _agent.isStopped = false;
                _agent.SetDestination(_lastTargetPos);
            }
        }
        _isMoving = true;
    }

    /// <summary>At the hazard: (outlet) play the PutFork beat with a last-chance save window, then zap + fail.</summary>
    private IEnumerator HazardArrivalRoutine()
    {
        if (_agent != null && _agent.isActiveAndEnabled && _agent.isOnNavMesh) _agent.isStopped = true;
        if (targetHazard != null) FaceToward(targetHazard.transform.position); // face the outlet/microwave

        string hzName = targetHazard != null ? targetHazard.hazardName : "";
        bool isOutlet = hzName == "Electrical Outlet";
        bool isMicrowave = hzName == "Microwave";
        bool isCleaningProduct = hzName == "Cleaning Product";
        bool isSkateboard = hzName == "Skateboard";
        bool isWindow = hzName == "Window";

        if (isOutlet)
        {
            if (animator != null)
            {
                animator.SetTrigger(AnimPutFork);
                yield return new WaitForSeconds(putForkAnimDuration);
                if (_held || _isStopped) yield break; // saved during the fork-insertion beat
            }
            InsertCarriedItemIntoHazard(); // snap the fork into the outlet exactly at the zap
            PlayReaction(ChildReaction.Electrocuted);
        }
        else if (isMicrowave)
        {
            if (animator != null)
            {
                animator.SetTrigger(AnimPutCat);
                yield return new WaitForSeconds(putCatAnimDuration);
                if (_held || _isStopped) yield break; // saved during the put-cat beat
            }
            InsertCarriedItemIntoHazard(); // place the cat inside the microwave
            // Freeze the child; the microwave (HazardZone.MicrowaveSequence) runs then explodes.
            _isStopped = true;
            _isMoving = false;
            if (_agent != null && _agent.isActiveAndEnabled && _agent.isOnNavMesh) _agent.isStopped = true;
        }
        else if (isCleaningProduct)
        {
            Debug.Log($"[ChildNPC] S3 arrival at {transform.position}; hazard '{(targetHazard!=null?targetHazard.hazardName:"<null>")}' at {(targetHazard!=null?targetHazard.transform.position.ToString():"<null>")}", this);
            // Reach down to grab whatever is there (PickUp/“ramasse” anim). The DEADLINE to swap is the grab
            // moment (~35% in, pickupAttachDelay): swap before that and the child picks up the WATER instead
            // of the poison, into the LEFT hand (the drink animation drinks with the left hand).
            if (animator != null) animator.SetTrigger(AnimPickUp);
            yield return new WaitForSeconds(pickupAttachDelay);
            if (_held || _isStopped) yield break;

            bool swapped = targetHazard != null && targetHazard.IsNeutralised; // checked NOW (the grab), not at arrival
            if (swapped)
            {
                var wb = FindAnyObjectByType<WaterBottle>();
                if (wb != null)
                {
                    AttachProductCap(wb.gameObject); // carry the water cap along (B_waterbottle_body → _cap)
                    SetCarriedItem(wb.gameObject, _s3ItemLocalPos, _s3ItemLocalEuler, useLeftHand: true);
                }
            }
            else
            {
                var prod = NearestVisibleProduct();
                if (prod != null)
                {
                    AttachProductCap(prod);
                    SetCarriedItem(prod, _s3ItemLocalPos, _s3ItemLocalEuler, useLeftHand: true);
                    S3ProductGrabbed = true; // swap window now closed — a later swap is too late
                }
            }
            yield return new WaitForSeconds(Mathf.Max(0f, pickupAnimDuration - pickupAttachDelay));
            if (_held || _isStopped) yield break;

            // Drink it FULLY (water or poison).
            if (animator != null) animator.SetTrigger(AnimDrink);
            yield return new WaitForSeconds(drinkAnimDuration);
            if (_held || _isStopped) yield break;

            if (swapped)
            {
                // Drank WATER → safe. Release the bottle (its own ResetBottle restores it next round), then win.
                ForgetCarriedItem();
                if (targetHazard != null) targetHazard.TriggerHazard(); // → NeutralisedSuccessSequence → ReportSuccess
                yield break;
            }

            // Poison: the toddler turns green and collapses, then we lose. Force the Die state directly
            // (animator.Play) so a lingering Drink/PickUp transition can't keep the drinking pose on screen.
            SetPoisonTint(true);
            if (animator != null)
            {
                animator.ResetTrigger(AnimPickUp);
                animator.ResetTrigger(AnimDrink);
                animator.SetTrigger(AnimDie);
                animator.Play("Die", 0, 0f);
            }
            Debug.Log("[ChildNPC] S3 death — forcing Die (DyingBackwards), then game over.", this);
            _isStopped = true;
            _isMoving = false;
            if (_agent != null && _agent.isActiveAndEnabled && _agent.isOnNavMesh) _agent.isStopped = true;
            yield return new WaitForSeconds(dieAnimDuration);
            GameManager.Instance?.ReportFail();
            yield break;
        }
        else if (isSkateboard)
        {
            // Mount the skateboard, then hand off to the (separate) SkateboardRide for the slide.
            if (animator != null) animator.SetTrigger(AnimJumpSkate);
            yield return new WaitForSeconds(jumpSkateDuration);
            if (_held || _isStopped) yield break; // caught during the mount
            if (_agent != null && _agent.enabled)
            {
                if (_agent.isOnNavMesh) _agent.isStopped = true;
                _agent.enabled = false; // scripted slide from here (SkateboardRide drives position)
            }
            // Robust: if the scenario config reference didn't reach us, find the ride in the scene anyway.
            if (_skateboardRide == null)
            {
                _skateboardRide = FindAnyObjectByType<SkateboardRide>();
                Debug.LogWarning($"[ChildNPC] S4: _skateboardRide was null (config wiring missing) — scene fallback found one: {_skateboardRide != null}", this);
            }
            if (_skateboardRide != null) _skateboardRide.Begin(this);
            else Debug.LogError("[ChildNPC] S4: no SkateboardRide in the scene — the baby can't ride.", this);
            ArmCatch(); // on the skate and rolling → the catch (and its hint) goes live
            yield break;
        }
        else if (isWindow)
        {
            var windowPigeon = FindAnyObjectByType<PigeonEscape>();

            // Closed BEFORE the toddler even starts climbing → no climb, no backward fall; the bird leaves too. Success.
            if (targetHazard != null && targetHazard.IsNeutralised)
            {
                if (windowPigeon != null) windowPigeon.TakeOff();
                _isStopped = true;
                _isMoving = false;
                if (_agent != null && _agent.isActiveAndEnabled && _agent.isOnNavMesh) _agent.isStopped = true;
                targetHazard.TriggerHazard(); // already neutralised → success sequence → ReportSuccess
                yield break;
            }

            // Climb the OPEN window after the bird. The player must close it before the climb finishes:
            //   • closed MID-climb (hazard neutralised) → topples BACK inside (reuse FallDeath/FallingBack) → success.
            //   • climb completes uninterrupted          → crests the ledge and falls OUT (FallFlat) → fail.
            if (animator != null) animator.SetTrigger(AnimClimb);
            bool pigeonFlown = false;
            float climbed = 0f;
            bool closedInTime = false;
            while (climbed < climbAnimDuration)
            {
                if (_held || _isStopped) yield break;
                if (targetHazard != null && targetHazard.IsNeutralised) { closedInTime = true; break; }
                if (!pigeonFlown && climbed >= climbAnimDuration * 0.8f) // the bird startles at 80% of the climb
                {
                    pigeonFlown = true;
                    if (windowPigeon != null) windowPigeon.TakeOff();
                }
                climbed += Time.deltaTime;
                yield return null;
            }

            // Stop for the fall; disable the agent so the fall clip's root motion can play.
            _isStopped = true;
            _isMoving = false;
            if (_agent != null && _agent.enabled)
            {
                if (_agent.isOnNavMesh) _agent.isStopped = true;
                _agent.enabled = false;
            }

            if (closedInTime)
            {
                // Window shut before the toddler crested → the bird flies off too (don't shut it on the bird),
                // and the toddler topples backward, safe inside.
                if (windowPigeon != null) windowPigeon.TakeOff();
                if (animator != null) { animator.ResetTrigger(AnimClimb); animator.SetTrigger(AnimFallDeath); animator.Play("FallDeath", 0, 0f); }
                yield return new WaitForSeconds(0.5f); // let the backward fall read before the success banner
                if (targetHazard != null) targetHazard.TriggerHazard(); // neutralised → success sequence → ReportSuccess
                yield break;
            }

            // Too late / never closed → the toddler crests the ledge and falls OUT the window to the ground below:
            // mid-air fall (FallingBaby, loops) during the drop, then the crash (FallFlat) on the OUTSIDE ground.
            Vector3 startPos = transform.position;
            Vector3 hazardPos = targetHazard != null ? targetHazard.transform.position : startPos;
            Vector3 outDir = new Vector3(hazardPos.x - startPos.x, 0f, hazardPos.z - startPos.z);
            outDir = outDir.sqrMagnitude > 0.01f ? outDir.normalized : transform.forward;
            Vector3 endXZ = new Vector3(hazardPos.x, 0f, hazardPos.z) + outDir * windowFallOutDistance; // past the window = outside
            float groundY = startPos.y - windowFallDropHeight; // fallback if the raycast misses
            if (Physics.Raycast(new Vector3(endXZ.x, startPos.y + 1f, endXZ.z), Vector3.down, out RaycastHit groundHit, 60f, ~0, QueryTriggerInteraction.Ignore))
                groundY = groundHit.point.y;
            Vector3 landPos = new Vector3(endXZ.x, groundY, endXZ.z);

            if (animator != null) { animator.ResetTrigger(AnimClimb); animator.SetTrigger(AnimFall); animator.Play("Fall", 0, 0f); }
            float ft = 0f;
            while (ft < fallTravelDuration)
            {
                float u = ft / fallTravelDuration;
                Vector3 p = Vector3.Lerp(startPos, landPos, u);
                p.y = Mathf.Lerp(startPos.y, landPos.y, u * u); // accelerate downward (gravity-ish)
                transform.position = p;
                ft += Time.deltaTime;
                yield return null;
            }
            transform.position = landPos;

            if (animator != null) { animator.ResetTrigger(AnimFall); animator.SetTrigger(AnimFallFlat); animator.Play("FallFlat", 0, 0f); }
            yield return new WaitForSeconds(fallFlatDuration);
            GameManager.Instance?.ReportFail();
            yield break;
        }

        if (targetHazard != null) targetHazard.TriggerHazard();
    }

    /// <summary>
    /// At the electrocution beat, detaches the carried item (fork) from the hand and snaps it into
    /// the hazard (outlet) so it reads as "inserted" exactly when the child is zapped — not before.
    /// </summary>
    private void InsertCarriedItemIntoHazard()
    {
        if (_carriedItem == null || targetHazard == null) return;
        _carriedItem.transform.SetParent(null, worldPositionStays: true);
        _carriedItem.transform.position = targetHazard.transform.position;
        _carriedItem.transform.rotation = targetHazard.transform.rotation; // fixed outlet orientation
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
        _walkingToPickup = false;
        _isStopped = false;
        _isMoving = false;
        StopActiveReaction();
        _walkPhase = 0f;
        _walkWeight = 0f;
        if (animator != null) animator.SetBool(AnimCarried, false);
        if (_held)
        {
            _held = false;
            transform.SetParent(_originalParent, worldPositionStays: false);
        }
        SetCollidersEnabled(true);
        SetPoisonTint(false);
        if (_agent != null && !_agent.enabled) _agent.enabled = true;
    }

    /// <summary>Tints the whole toddler green (poisoned) or clears it. Uses a MaterialPropertyBlock so
    /// it creates no material instances and is trivially reversible on restart.</summary>
    public void SetPoisonTint(bool on)
    {
        if (_bodyRenderers == null) _bodyRenderers = GetComponentsInChildren<Renderer>(true);
        if (_tintMpb == null) _tintMpb = new MaterialPropertyBlock();
        var green = new Color(0.35f, 0.8f, 0.2f);
        foreach (var r in _bodyRenderers)
        {
            if (r == null) continue;
            if (on)
            {
                r.GetPropertyBlock(_tintMpb);
                _tintMpb.SetColor("_BaseColor", green);
                _tintMpb.SetColor("_Color", green);
                r.SetPropertyBlock(_tintMpb);
            }
            else r.SetPropertyBlock(null);
        }
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

        // Make sure we're actually ON the NavMesh before driving the agent. A NavMeshAgent.Warp
        // during ActivateScenario can silently fail when re-activating after a previous round,
        // leaving the agent off-mesh (isOnNavMesh=false) — it then never moves, and any isStopped
        // call throws. Re-bind by re-acquiring the NavMesh.
        RebindAgentToNavMesh();
        float waitEnd = Time.time + 1f;
        while (Time.time < waitEnd && (_agent == null || !_agent.isActiveAndEnabled || !_agent.isOnNavMesh))
        {
            RebindAgentToNavMesh();
            yield return null;
        }
        if (_agent == null || !_agent.isOnNavMesh)
        {
            Debug.LogWarning("[ChildNPC] Couldn't bind to the NavMesh — child can't walk. " +
                             "Check the spawn point sits on the baked NavMesh.", this);
            yield break;
        }

        if (laughClip != null && audioSource != null)
            audioSource.PlayOneShot(laughClip);

        Vector3 firstDest = CurrentDestination();
        _lastTargetPos = firstDest;
        _agent.isStopped = false;
        _agent.SetDestination(firstDest);
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

    /// <summary>
    /// Procedural "toddler waddle". Driven by a single accumulated step phase so the
    /// gait never jumps when movement starts/stops, with a smoothly eased 0..1 weight.
    ///  • Bob   : |sin(phase·π)| → two bounces per cycle (cadence).
    ///  • Waddle: lateral slide + Z-roll at HALF the bob cadence (one sway per step pair).
    ///  • Lean  : small forward X-tilt scaled by normalised speed.
    /// Runs alongside the optional Animator (which gets the same Speed value) — never
    /// blocked by it, so a missing/!rigged Animator changes nothing.
    /// </summary>
    private void AnimateWalk()
    {
        if (_meshT == null) return;

        // A reaction coroutine owns _meshT while it runs; don't fight it.
        if (_reactionCoroutine != null) return;

        // Speed factor 0..1 (used both for the procedural lean and the Animator).
        float speed01 = walkSpeed > 0.001f
            ? Mathf.Clamp01(_agent != null ? _agent.velocity.magnitude / walkSpeed : 0f)
            : 0f;

        bool walking = IsMoving;

        // Legacy-aware tunables: fall back to the old public fields when the new
        // ones are left at 0, so existing inspector setups keep working.
        float rate     = stepRate       > 0.001f ? stepRate       : bobFrequency * 0.5f;
        float bobAmp   = stepBobHeight  > 0.0001f ? stepBobHeight : bobAmplitude;
        float rollAmp  = waddleRollDeg  > 0.001f ? waddleRollDeg  : wobbleAngle;

        // Smoothly blend the whole gait in/out — no snap.
        float targetWeight = walking ? 1f : 0f;
        _walkWeight = Mathf.MoveTowards(_walkWeight, targetWeight, walkWeightLerp * Time.deltaTime);

        // Only advance the step phase while actually moving (freezes mid-stride on stop).
        if (walking)
        {
            _walkPhase += rate * Time.deltaTime;
            if (_walkPhase > 1000f) _walkPhase -= 1000f; // keep the float small & precise
        }

        if (_walkWeight > 0.0001f)
        {
            float bobArg    = _walkPhase * Mathf.PI;        // full cadence
            float waddleArg = _walkPhase * Mathf.PI * 0.5f; // half cadence (per step pair)

            float bob   = Mathf.Abs(Mathf.Sin(bobArg)) * bobAmp;
            float side  = Mathf.Sin(waddleArg) * waddleSideAmount;
            float roll  = Mathf.Sin(waddleArg) * rollAmp;
            float lean  = forwardLeanDeg * speed01;

            Vector3 offset = new Vector3(side, bob, 0f) * _walkWeight;
            _meshT.localPosition = _meshStartLocalPos + offset;
            _meshT.localRotation = Quaternion.Euler(lean * _walkWeight, 0f, roll * _walkWeight);
        }
        else
        {
            // Fully at rest — settle exactly onto the rest pose (no lingering drift).
            _meshT.localPosition = Vector3.Lerp(_meshT.localPosition, _meshStartLocalPos, Time.deltaTime * 8f);
            _meshT.localRotation = Quaternion.Slerp(_meshT.localRotation, Quaternion.identity, Time.deltaTime * 8f);
        }

        // Optional Animator bridge — parallel, never a replacement.
        if (animator != null) animator.SetFloat(AnimSpeed, walking ? speed01 : 0f);
    }

    // ── Reactions ──────────────────────────────────────────────────────────
    /// <summary>
    /// Plays a procedural reaction and notifies the optional Animator bridge.
    /// Electrocuted is a failure beat: it sets _isStopped and freezes the child.
    /// Stubs (SkateWipeout/Stumble) keep the API ready without committing visuals.
    /// </summary>
    public void PlayReaction(ChildReaction reaction)
    {
        // Never react while held (player saved them) or already mid-reaction.
        if (_held || _reactionCoroutine != null) return;

        switch (reaction)
        {
            case ChildReaction.Electrocuted:
                // The zap is a fail: freeze the toddler in place.
                _isStopped = true;
                _isMoving = false;
                if (_agent != null && _agent.isActiveAndEnabled && _agent.isOnNavMesh)
                    _agent.isStopped = true;
                if (animator != null) animator.SetTrigger(AnimElectrocute);
                // With a rigged animator, the BeingElectrocuted clip handles the visual —
                // skip the procedural jitter so the two don't fight.
                if (animator == null || animator.runtimeAnimatorController == null)
                    _reactionCoroutine = StartCoroutine(ElectrocutedRoutine());
                break;

            case ChildReaction.SkateWipeout:
            case ChildReaction.Stumble:
                if (animator != null) animator.SetTrigger(AnimReact);
                Debug.LogWarning($"[ChildNPC] Reaction '{reaction}' not yet implemented (stub).", this);
                break;
        }
    }

    /// <summary>
    /// Stiff, high-frequency electrocution twitch on the mesh holder only.
    /// Uses summed Mathf.Sin harmonics (deterministic, zero per-frame allocation)
    /// for the jitter, twitchy low-amplitude roll/pitch, and an optional emissive
    /// white pulse via a MaterialPropertyBlock. Restores the rest pose at the end;
    /// the child stays frozen (failure). Does NOT report success/fail — HazardZone does.
    /// </summary>
    private IEnumerator ElectrocutedRoutine()
    {
        Vector3 startPos = _meshT.localPosition;
        Quaternion startRot = _meshT.localRotation;

        // Capture the renderer's current emissive so we can restore it after the flash.
        bool canFlash = electrocuteEmissiveFlash && _meshRenderer != null;
        Color emissiveStart = Color.black;
        if (canFlash)
        {
            _meshRenderer.GetPropertyBlock(_meshMpb);
            emissiveStart = _meshMpb.GetColor(EmissionColorProp);
        }

        float elapsed = 0f;
        float duration = Mathf.Max(0.05f, electrocuteDuration);
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            float decay = 1f - t; // ease the violence out toward the end

            // ~25-30 Hz rigid jitter from high harmonics of the elapsed time.
            // Two incommensurate frequencies per axis → non-repeating, deterministic.
            float jx = Mathf.Sin(elapsed * 168f) + 0.5f * Mathf.Sin(elapsed * 271f);
            float jy = Mathf.Sin(elapsed * 191f) + 0.5f * Mathf.Sin(elapsed * 233f);
            float jz = Mathf.Sin(elapsed * 209f) + 0.5f * Mathf.Sin(elapsed * 311f);
            Vector3 jitter = new Vector3(jx, jy, jz) * (electrocuteShakeAmp * 0.6667f * decay);

            // Stiff twitchy roll/pitch (different harmonics so it doesn't mirror the jitter).
            float twX = Mathf.Sin(elapsed * 154f) * electrocuteTwistDeg * decay;
            float twZ = Mathf.Sin(elapsed * 187f) * electrocuteTwistDeg * decay;

            _meshT.localPosition = startPos + jitter;
            _meshT.localRotation = startRot * Quaternion.Euler(twX, 0f, twZ);

            // Emissive white pulse in phase with the jitter.
            if (canFlash)
            {
                float pulse = (0.5f + 0.5f * Mathf.Sin(elapsed * 168f)) * decay;
                _meshRenderer.GetPropertyBlock(_meshMpb);
                _meshMpb.SetColor(EmissionColorProp, Color.white * (pulse * 2f));
                _meshRenderer.SetPropertyBlock(_meshMpb);
            }
            yield return null;
        }

        // Restore pose; the child remains frozen (failure state owned by HazardZone).
        _meshT.localPosition = startPos;
        _meshT.localRotation = startRot;
        if (canFlash)
        {
            _meshRenderer.GetPropertyBlock(_meshMpb);
            _meshMpb.SetColor(EmissionColorProp, emissiveStart);
            _meshRenderer.SetPropertyBlock(_meshMpb);
        }
        _reactionCoroutine = null;
    }

    /// <summary>
    /// Cancels any running reaction and restores the mesh holder's rest pose &amp; emissive.
    /// Called when a scenario resets so a child frozen mid-electrocution starts clean.
    /// </summary>
    private void StopActiveReaction()
    {
        if (_reactionCoroutine != null)
        {
            StopCoroutine(_reactionCoroutine);
            _reactionCoroutine = null;
        }
        if (_meshT != null)
        {
            _meshT.localPosition = _meshStartLocalPos;
            _meshT.localRotation = Quaternion.identity;
        }
        if (electrocuteEmissiveFlash && _meshRenderer != null && _meshMpb != null)
        {
            _meshRenderer.GetPropertyBlock(_meshMpb);
            _meshMpb.SetColor(EmissionColorProp, Color.black);
            _meshRenderer.SetPropertyBlock(_meshMpb);
        }
    }

    /// <summary>
    /// Snaps the rigged animator back to Idle and clears pending triggers, so a scenario restart
    /// (e.g. the player retrying before the electrocution clip finished) doesn't keep playing the
    /// old reaction animation.
    /// </summary>
    private void ResetAnimatorToIdle()
    {
        if (animator == null || animator.runtimeAnimatorController == null) return;
        animator.ResetTrigger(AnimElectrocute);
        animator.ResetTrigger(AnimPutFork);
        animator.ResetTrigger(AnimPutCat);
        animator.ResetTrigger(AnimPickUp);
        animator.ResetTrigger(AnimSurprised);
        animator.ResetTrigger(AnimDrink);
        animator.ResetTrigger(AnimDie);
        animator.ResetTrigger(AnimJumpSkate);
        animator.ResetTrigger(AnimFallDeath);
        animator.ResetTrigger(AnimClimb);
        animator.ResetTrigger(AnimFallFlat);
        animator.ResetTrigger(AnimFall);
        animator.SetBool(AnimCarried, false);
        animator.SetFloat(AnimSpeed, 0f);
        animator.Play("Idle", 0, 0f);
    }

    private void OnGameStateChanged(GameManager.GameState state)
    {
        // Cancel any in-flight arrival beat (interrupted pickup / PutCat / PutFork…). Without this, a
        // coroutine from the PREVIOUS scenario can finish its wait and call TriggerHazard() on the NOW-CURRENT
        // targetHazard — e.g. grabbing the cat mid-PutCat won S2 but then triggered the S3 hazard (phantom loss).
        if (_arrivalRoutine != null) { StopCoroutine(_arrivalRoutine); _arrivalRoutine = null; }

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
            if (animator != null) animator.SetBool(AnimCarried, false);

            // Re-enable the agent if Grab() disabled it on the previous round.
            if (_agent != null && !_agent.enabled) _agent.enabled = true;

            // Clear any electrocution freeze/twitch left over from the previous round.
            StopActiveReaction();
            ResetAnimatorToIdle();   // snap out of Electrocute/etc. if the player retried mid-animation
            SetPoisonTint(false);    // clear the green poison tint from a previous S3 death
            S3ProductGrabbed = false; // re-open the swap window for the new round
            _walkPhase = 0f;
            _walkWeight = 0f;

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
        RebindAgentToNavMesh();
        yield return BeginWalkAfterDelay();
    }

    /// <summary>
    /// Re-binds the NavMeshAgent onto the NavMesh if it has fallen off (isOnNavMesh=false).
    /// ScenarioManager warps the child on (re)activation, but Warp can silently fail when
    /// re-activating after a previous round — sampling the nearest point and warping is reliable.
    /// </summary>
    private void RebindAgentToNavMesh()
    {
        if (_agent == null || !_agent.enabled || _agent.isOnNavMesh) return;
        Vector3 pos = transform.position;
        if (NavMesh.SamplePosition(pos, out NavMeshHit hit, 5f, NavMesh.AllAreas)) pos = hit.position;
        // Disable → reposition → enable forces the agent to re-acquire the NavMesh at this point.
        // This is more reliable than NavMeshAgent.Warp, which silently fails when re-activating.
        _agent.enabled = false;
        transform.position = pos;
        _agent.enabled = true;
        Debug.Log($"[ChildNPC] Re-bound agent to NavMesh at {pos} (onNavMesh now {_agent.isOnNavMesh})", this);
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
