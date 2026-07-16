using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// STOP IT! — MenuCameraTour
/// Drives the menu "attract mode" room tour. It moves a target transform between
/// per-room waypoint poses and fires <see cref="OnArrive"/> each time it settles,
/// so <see cref="MenuScenarioShowcase"/> can show the matching scenario.
///
/// Two comfort modes:
///   • Glide        — smooth Catmull-Rom dolly. Good for a dedicated 2D camera.
///   • TeleportFade — fade to black, snap the target to the next room, fade in.
///                    The only motion happens while the view is black, so it is
///                    VR-comfortable. Use this when the target is the XR rig
///                    (the HMD renders the rig, not a separate camera).
///
/// Auto-advance cycles the rooms; <see cref="GoToStop"/> jumps to a chosen room
/// (player picked a scenario) and pauses auto-advance for a while.
/// </summary>
[DefaultExecutionOrder(20)]
public class MenuCameraTour : MonoBehaviour
{
    public enum ComfortMode { Glide, TeleportFade, Follow }

    [Header("Path")]
    [Tooltip("Ordered poses, one per room. If empty, children named 'WP*'/'Waypoint*' are collected.")]
    public List<Transform> waypoints = new List<Transform>();

    [Tooltip("Loop back to the first room after the last (recommended for a menu).")]
    public bool loop = true;

    [Tooltip("Transform the tour moves. The XR rig (XR Origin) for VR, or a dedicated camera for 2D.")]
    public Transform tourTarget;

    [Header("Comfort")]
    [Tooltip("Glide = smooth dolly through waypoints. TeleportFade = fade + snap (VR, drives the rig). " +
             "Follow = camera trails the roaming NPCs around the house.")]
    public ComfortMode comfortMode = ComfortMode.Glide;

    [Header("Follow mode")]
    [Tooltip("NPCs the camera follows in Follow mode. Auto-found (MenuRoamingNPC) if empty.")]
    public List<Transform> followTargets = new List<Transform>();
    [Tooltip("How far behind the followed NPC the camera trails (m).")]
    public float followBackDistance = 3f;
    [Tooltip("Camera height above the NPC's feet while following (m).")]
    public float followHeight = 1.8f;
    [Tooltip("Height on the NPC the camera aims at (m).")]
    public float followLookHeight = 0.9f;
    [Tooltip("Position smoothing time (s). Higher = lazier, smoother follow.")]
    public float followSmoothTime = 0.5f;
    [Tooltip("How fast the camera re-aims at the NPC (deg/s feel).")]
    public float followAimLerp = 4f;
    [Tooltip("Seconds before the camera switches to the other NPC.")]
    public float followSwitchInterval = 10f;

    [Header("Follow wall collision")]
    [Tooltip("Prevent the follow camera from passing through walls/ceilings.")]
    public bool followWallCollision = true;
    public LayerMask followCollisionMask = ~0;
    [Min(0.05f)] public float followCollisionRadius = 0.15f;
    [Min(0.05f)] public float followCollisionSkin   = 0.05f;

    [Tooltip("Fader used by TeleportFade. Auto-found if left null.")]
    public MenuFader fader;

    [Tooltip("Seconds for each half of the fade (out, then in) in TeleportFade mode.")]
    [Min(0.05f)] public float fadeDuration = 0.4f;

    [Header("Timing")]
    [Tooltip("Seconds to travel between two rooms (Glide mode only).")]
    [Min(0.5f)] public float travelTime = 7f;

    [Tooltip("Seconds to dwell (hold) in each room before moving on.")]
    [Min(0f)] public float dwellTime = 4f;

    [Tooltip("Ease curve applied to the 0..1 travel progress (Glide mode).")]
    public AnimationCurve travelEase = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Auto-advance")]
    public bool autoAdvance = true;
    [Tooltip("After the player jumps to a room, seconds to hold before auto-advance resumes.")]
    [Min(0f)] public float autoResumeDelay = 8f;

    [Header("Look (Glide only)")]
    [Tooltip("If set, the camera faces this target instead of slerping waypoint rotations.")]
    public Transform lookAtTarget;
    [Min(0.1f)] public float lookLerpSpeed = 3f;

    [Header("Lifecycle")]
    [Tooltip("Only run while GameManager is in the Menu state.")]
    public bool onlyDuringMenu = true;
    [Tooltip("Snap the target onto the first waypoint pose when the tour starts.")]
    public bool snapToFirstOnStart = true;

    [Header("Events")]
    [Tooltip("Fired with the stop index each time the target settles in a room.")]
    public UnityEvent<int> OnArrive = new UnityEvent<int>();

    public int CurrentStop => _fromIndex;
    public int StopCount => waypoints.Count;

    // ── Runtime ──────────────────────────────────────────────────────────────
    private enum Phase { Dwelling, Travelling }
    private Phase _phase = Phase.Dwelling;
    private int   _fromIndex;
    private float _phaseTimer;
    private bool  _running;
    private bool  _transitioning; // a TeleportFade coroutine owns movement
    private Camera _cam;
    private bool  _subscribed;

    private int        _jumpTarget = -1;   // Glide jump
    private Vector3    _jumpStartPos;
    private Quaternion _jumpStartRot;
    private float      _autoPausedUntil;

    // Follow mode.
    private int     _followIndex;
    private float   _followTimer;
    private Vector3 _followVel;

    private readonly List<Camera> _suppressedCams = new List<Camera>();

    // ── Unity ──────────────────────────────────────────────────────────────
    private void Awake()
    {
        if (tourTarget == null) tourTarget = transform;
        _cam = GetComponent<Camera>();
        if (fader == null) fader = FindAnyObjectByType<MenuFader>();
    }

    private void Start()
    {
        if (waypoints.Count == 0) AutoCollectWaypoints();

        if (comfortMode == ComfortMode.Follow && followTargets.Count == 0)
        {
            foreach (var npc in FindObjectsByType<MenuRoamingNPC>(FindObjectsSortMode.None))
                followTargets.Add(npc.transform);
        }

        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnStateChanged.AddListener(OnStateChanged);
            _subscribed = true;
        }

        bool inMenu = GameManager.Instance == null ||
                      GameManager.Instance.State == GameManager.GameState.Menu;
        SetRunning(!onlyDuringMenu || inMenu);
    }

    private void OnDestroy()
    {
        if (_subscribed && GameManager.Instance != null)
            GameManager.Instance.OnStateChanged.RemoveListener(OnStateChanged);
        SuppressOtherCameras(false);
    }

    private void OnStateChanged(GameManager.GameState state)
    {
        if (!onlyDuringMenu) return;
        SetRunning(state == GameManager.GameState.Menu);
    }

    // ── Tour control ─────────────────────────────────────────────────────────
    private void SetRunning(bool run)
    {
        _running = run;
        if (_cam != null) _cam.enabled = run;        // only when this IS a dedicated camera
        SuppressOtherCameras(run && _cam != null);    // never suppress when driving the rig

        StopAllCoroutines();
        _transitioning = false;

        if (!run)
        {
            if (fader != null) fader.SetAlphaImmediate(0f);
            // Restore all follow-target NPCs when the tour stops (returning to gameplay).
            if (comfortMode == ComfortMode.Follow)
                for (int i = 0; i < followTargets.Count; i++) SetFollowNpcVisible(i, true);
            return;
        }

        _phase           = Phase.Dwelling;
        _phaseTimer      = 0f;
        _fromIndex       = 0;
        _jumpTarget      = -1;
        _autoPausedUntil = 0f;
        _followIndex     = 0;
        _followTimer     = 0f;

        // In Follow mode, show only the first NPC so the camera starts with a clean cut.
        if (comfortMode == ComfortMode.Follow)
            for (int i = 0; i < followTargets.Count; i++) SetFollowNpcVisible(i, i == 0);

        if (snapToFirstOnStart) SetPose(0);
        if (fader != null) fader.SetAlphaImmediate(0f);
        Arrive(_fromIndex);
    }

    private void SuppressOtherCameras(bool suppress)
    {
        if (suppress)
        {
            if (_cam == null) return;
            _suppressedCams.Clear();
            foreach (var c in Camera.allCameras)
            {
                if (c == null || c == _cam) continue;
                c.enabled = false;
                _suppressedCams.Add(c);
            }
        }
        else
        {
            foreach (var c in _suppressedCams)
                if (c != null) c.enabled = true;
            _suppressedCams.Clear();
        }
    }

    /// <summary>Player selected a scenario room: go there and pause auto-advance.</summary>
    public void GoToStop(int index)
    {
        if (index < 0 || index >= waypoints.Count || tourTarget == null) return;
        _autoPausedUntil = Time.time + autoResumeDelay;

        if (comfortMode == ComfortMode.TeleportFade)
        {
            if (_transitioning) return;
            if (index == _fromIndex) { Arrive(index); return; }
            StartCoroutine(TeleportTransition(index));
            return;
        }

        // Glide jump.
        if (index == _fromIndex && _jumpTarget < 0 && _phase == Phase.Dwelling) { Arrive(index); return; }
        _jumpStartPos = tourTarget.position;
        _jumpStartRot = tourTarget.rotation;
        _jumpTarget   = index;
        _phaseTimer   = 0f;
    }

    private void Arrive(int index) => OnArrive?.Invoke(index);

    // ── Update ─────────────────────────────────────────────────────────────
    private void Update()
    {
        if (!_running || waypoints.Count == 0 || tourTarget == null) return;
        if (_transitioning) return;

        if (comfortMode == ComfortMode.Follow) { FollowUpdate(); return; }

        if (waypoints.Count == 1) { SetPose(0); return; }

        if (comfortMode == ComfortMode.TeleportFade) { TeleportUpdate(); return; }
        GlideUpdate();
    }

    // ── Follow ───────────────────────────────────────────────────────────────
    private void FollowUpdate()
    {
        // Drop any destroyed targets.
        for (int i = followTargets.Count - 1; i >= 0; i--)
            if (followTargets[i] == null) followTargets.RemoveAt(i);
        if (followTargets.Count == 0) return;

        // Cycle which NPC we follow so the camera tours the whole house.
        _followTimer += Time.deltaTime;
        if (followTargets.Count > 1 && _followTimer >= followSwitchInterval)
        {
            SetFollowNpcVisible(_followIndex, false); // hide old
            _followIndex = (_followIndex + 1) % followTargets.Count;
            _followTimer = 0f;
            SetFollowNpcVisible(_followIndex, true);  // show new
        }
        if (_followIndex >= followTargets.Count) _followIndex = 0;

        Transform tgt = followTargets[_followIndex];
        if (tgt == null) return;

        // Trail behind the NPC (use its facing; fall back to the current camera→NPC line).
        Vector3 fwd = tgt.forward; fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.01f) { fwd = tgt.position - tourTarget.position; fwd.y = 0f; }
        if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
        fwd.Normalize();

        Vector3 desired = tgt.position - fwd * followBackDistance + Vector3.up * followHeight;
        ClampInsideWalls(tgt, ref desired);
        tourTarget.position = Vector3.SmoothDamp(tourTarget.position, desired, ref _followVel, followSmoothTime);

        Vector3 lookDir = (tgt.position + Vector3.up * followLookHeight) - tourTarget.position;
        if (lookDir.sqrMagnitude > 0.0001f)
            tourTarget.rotation = Quaternion.Slerp(tourTarget.rotation,
                Quaternion.LookRotation(lookDir, Vector3.up), followAimLerp * Time.deltaTime);

        // Keep the showcase in sync with whichever scenario room the NPC is in.
        int near = NearestStop(tgt.position);
        if (near >= 0 && near != _fromIndex) { _fromIndex = near; Arrive(near); }
    }

    /// <summary>
    /// In Follow mode, only the active follow target is visible. Uses MenuRoamingNPC's
    /// force-hidden flag so the NPC's own roaming-show logic can't override us.
    /// Falls back to direct Renderer toggling for non-MenuRoamingNPC targets.
    /// </summary>
    private void SetFollowNpcVisible(int idx, bool on)
    {
        if (idx < 0 || idx >= followTargets.Count || followTargets[idx] == null) return;
        var npc = followTargets[idx].GetComponent<MenuRoamingNPC>();
        if (npc != null) npc.SetCameraForceHidden(!on);
        else foreach (var r in followTargets[idx].GetComponentsInChildren<Renderer>(true)) r.enabled = on;
    }

    private void ClampInsideWalls(Transform npc, ref Vector3 desired)
    {
        if (!followWallCollision) return;
        Vector3 from = npc.position + Vector3.up * followLookHeight;
        Vector3 dir  = desired - from;
        float dist   = dir.magnitude;
        if (dist < 0.01f) return;
        if (Physics.SphereCast(from, followCollisionRadius, dir.normalized, out RaycastHit hit,
                dist - followCollisionSkin, followCollisionMask, QueryTriggerInteraction.Ignore))
            desired = hit.point - dir.normalized * followCollisionSkin;
    }

    private int NearestStop(Vector3 pos)
    {
        int best = -1; float bestSqr = float.MaxValue;
        for (int i = 0; i < waypoints.Count; i++)
        {
            if (waypoints[i] == null) continue;
            float d = (waypoints[i].position - pos).sqrMagnitude;
            if (d < bestSqr) { bestSqr = d; best = i; }
        }
        return best;
    }

    // ── TeleportFade ───────────────────────────────────────────────────────
    private void TeleportUpdate()
    {
        SetPose(_fromIndex); // hold the current room pose
        _phaseTimer += Time.deltaTime;
        if (_phaseTimer < dwellTime) return;

        if (autoAdvance && Time.time >= _autoPausedUntil)
            StartCoroutine(TeleportTransition(NextIndex(_fromIndex)));
        else
            _phaseTimer = dwellTime;
    }

    private IEnumerator TeleportTransition(int toIndex)
    {
        _transitioning = true;
        if (fader != null) yield return fader.FadeTo(1f, fadeDuration);

        SetPose(toIndex);
        _fromIndex  = toIndex;
        _phase      = Phase.Dwelling;
        _phaseTimer = 0f;
        Arrive(toIndex);

        if (fader != null) yield return fader.FadeTo(0f, fadeDuration);
        _transitioning = false;
    }

    // ── Glide ──────────────────────────────────────────────────────────────
    private void GlideUpdate()
    {
        _phaseTimer += Time.deltaTime;

        if (_jumpTarget >= 0)
        {
            float jr = Mathf.Clamp01(_phaseTimer / travelTime);
            float jt = travelEase != null ? travelEase.Evaluate(jr) : jr;
            Vector3 toPos = WpPos(_jumpTarget);
            tourTarget.position = Vector3.Lerp(_jumpStartPos, toPos, jt);
            ApplyLook(Quaternion.Slerp(_jumpStartRot, WpRot(_jumpTarget), jt));
            if (jr >= 1f) { _fromIndex = _jumpTarget; _jumpTarget = -1; _phase = Phase.Dwelling; _phaseTimer = 0f; Arrive(_fromIndex); }
            return;
        }

        if (_phase == Phase.Dwelling)
        {
            SetPose(_fromIndex);
            if (_phaseTimer >= dwellTime)
            {
                if (autoAdvance && Time.time >= _autoPausedUntil) { _phaseTimer = 0f; _phase = Phase.Travelling; }
                else _phaseTimer = dwellTime;
            }
            return;
        }

        int toIndex = NextIndex(_fromIndex);
        float raw = Mathf.Clamp01(_phaseTimer / travelTime);
        float t   = travelEase != null ? travelEase.Evaluate(raw) : raw;
        tourTarget.position = SplinePosition(_fromIndex, toIndex, t);
        ApplyLook(Quaternion.Slerp(WpRot(_fromIndex), WpRot(toIndex), t));
        if (raw >= 1f)
        {
            _fromIndex = toIndex; _phaseTimer = 0f; _phase = Phase.Dwelling; Arrive(_fromIndex);
            if (!loop && _fromIndex == waypoints.Count - 1) _running = false;
        }
    }

    private void ApplyLook(Quaternion splineRot)
    {
        if (lookAtTarget != null)
        {
            Vector3 dir = lookAtTarget.position - tourTarget.position;
            if (dir.sqrMagnitude > 0.0001f)
                tourTarget.rotation = Quaternion.Slerp(tourTarget.rotation,
                    Quaternion.LookRotation(dir, Vector3.up), lookLerpSpeed * Time.deltaTime);
        }
        else tourTarget.rotation = splineRot;
    }

    private void SetPose(int i)
    {
        tourTarget.position = WpPos(i);
        tourTarget.rotation = WpRot(i);
    }

    // ── Helpers ────────────────────────────────────────────────────────────
    private int NextIndex(int i) { int n = waypoints.Count; return loop ? (i + 1) % n : Mathf.Min(i + 1, n - 1); }
    private int PrevIndex(int i) { int n = waypoints.Count; return loop ? (i - 1 + n) % n : Mathf.Max(i - 1, 0); }

    private Vector3 WpPos(int i) => (i >= 0 && i < waypoints.Count && waypoints[i] != null) ? waypoints[i].position : tourTarget.position;
    private Quaternion WpRot(int i) => (i >= 0 && i < waypoints.Count && waypoints[i] != null) ? waypoints[i].rotation : tourTarget.rotation;

    private Vector3 SplinePosition(int from, int to, float t)
    {
        Vector3 p1 = WpPos(from), p2 = WpPos(to), p0 = WpPos(PrevIndex(from)), p3 = WpPos(NextIndex(to));
        float t2 = t * t, t3 = t2 * t;
        return 0.5f * ((2f * p1) + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }

    private void AutoCollectWaypoints()
    {
        waypoints.Clear();
        foreach (Transform child in transform)
            if (child.name.StartsWith("WP") || child.name.StartsWith("Waypoint")) waypoints.Add(child);
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (waypoints == null) return;
        Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.9f);
        for (int i = 0; i < waypoints.Count; i++)
        {
            if (waypoints[i] == null) continue;
            Gizmos.DrawWireSphere(waypoints[i].position, 0.25f);
            Gizmos.DrawRay(waypoints[i].position, waypoints[i].forward * 0.6f);
            int next = (i + 1 < waypoints.Count) ? i + 1 : (loop ? 0 : -1);
            if (next >= 0 && waypoints[next] != null) Gizmos.DrawLine(waypoints[i].position, waypoints[next].position);
        }
    }
#endif
}
