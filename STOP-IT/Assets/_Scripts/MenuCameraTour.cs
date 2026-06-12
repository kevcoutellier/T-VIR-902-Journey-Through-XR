using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// STOP IT! — MenuCameraTour
/// Cinematic menu background: a slow dolly camera that glides from room to room
/// through an ordered list of waypoint poses, pausing in each room. It is the
/// "attract mode" backdrop behind <see cref="MenuScenarioShowcase"/>, which shows
/// the matching scenario each time the camera settles in a room.
///
/// Two ways to drive it:
///   • Auto-advance (default): cycles through every stop on a loop, firing
///     <see cref="OnArrive"/> with the stop index each time it settles.
///   • <see cref="GoToStop"/>: the showcase calls this when the player picks a
///     scenario — the camera glides straight to that room and auto-advance pauses
///     for <see cref="autoResumeDelay"/> seconds before resuming the loop.
///
/// Motion is a Catmull-Rom spline through the waypoint positions for a continuous,
/// drift-free glide; rotation slerps between waypoint orientations (or toward an
/// optional <see cref="lookAtTarget"/>).
/// </summary>
[DefaultExecutionOrder(20)]
public class MenuCameraTour : MonoBehaviour
{
    [Header("Path")]
    [Tooltip("Ordered camera poses, one per room. If left empty, children named " +
             "'WP*' or 'Waypoint*' are collected automatically at Start.")]
    public List<Transform> waypoints = new List<Transform>();

    [Tooltip("Loop back to the first room after the last one (recommended for a menu).")]
    public bool loop = true;

    [Tooltip("Transform the tour drives. Defaults to this GameObject's transform " +
             "(put the script on the menu camera itself).")]
    public Transform tourTarget;

    [Header("Timing (slow + gentle for a menu)")]
    [Tooltip("Seconds to travel between two consecutive rooms.")]
    [Min(0.5f)] public float travelTime = 7f;

    [Tooltip("Seconds to dwell (hold still) in each room before moving on.")]
    [Min(0f)] public float dwellTime = 4f;

    [Tooltip("Ease curve applied to the 0..1 travel progress (smooth start/stop).")]
    public AnimationCurve travelEase = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Auto-advance")]
    [Tooltip("Automatically cycle to the next room after each dwell.")]
    public bool autoAdvance = true;

    [Tooltip("After the player jumps to a room via GoToStop, seconds to hold there " +
             "before the automatic cycle resumes.")]
    [Min(0f)] public float autoResumeDelay = 8f;

    [Header("Look")]
    [Tooltip("If set, the camera always faces this target instead of slerping " +
             "between waypoint rotations (e.g. a roaming NPC or the house centre).")]
    public Transform lookAtTarget;

    [Tooltip("How quickly the camera re-orients toward lookAtTarget (deg/s feel).")]
    [Min(0.1f)] public float lookLerpSpeed = 3f;

    [Header("Lifecycle")]
    [Tooltip("Only run the tour while GameManager is in the Menu state. The camera " +
             "is disabled when a scenario starts so the XR rig takes over, and " +
             "re-enabled on return to the menu.")]
    public bool onlyDuringMenu = true;

    [Tooltip("Start the camera exactly on the first waypoint pose on enable.")]
    public bool snapToFirstOnStart = true;

    [Header("Events")]
    [Tooltip("Fired with the stop index each time the camera settles in a room. " +
             "MenuScenarioShowcase listens to update the on-screen scenario.")]
    public UnityEvent<int> OnArrive = new UnityEvent<int>();

    /// <summary>Index of the room the camera is currently parked at (or heading from).</summary>
    public int CurrentStop => _fromIndex;
    public int StopCount => waypoints.Count;

    // ── Runtime ──────────────────────────────────────────────────────────────
    private enum Phase { Dwelling, Travelling }
    private Phase _phase = Phase.Dwelling;
    private int   _fromIndex;
    private float _phaseTimer;
    private bool  _running;
    private Camera _cam;
    private bool  _subscribed;

    // Direct "fly to this stop" jump (player selection). -1 = no pending jump.
    private int        _jumpTarget = -1;
    private Vector3    _jumpStartPos;
    private Quaternion _jumpStartRot;
    private float      _autoPausedUntil;

    // Other cameras we switched off while the menu tour owns the view (restored on exit).
    private readonly List<Camera> _suppressedCams = new List<Camera>();

    // ── Unity ──────────────────────────────────────────────────────────────
    private void Awake()
    {
        if (tourTarget == null) tourTarget = transform;
        _cam = GetComponent<Camera>();
    }

    private void Start()
    {
        if (waypoints.Count == 0) AutoCollectWaypoints();

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
        SuppressOtherCameras(false); // never leave the XR camera switched off
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
        if (_cam != null) _cam.enabled = run;

        SuppressOtherCameras(run);

        if (!run) return;

        _phase           = Phase.Dwelling;
        _phaseTimer      = 0f;
        _fromIndex       = 0;
        _jumpTarget      = -1;
        _autoPausedUntil = 0f;

        if (snapToFirstOnStart && waypoints.Count > 0 && waypoints[0] != null)
            tourTarget.SetPositionAndRotation(waypoints[0].position, waypoints[0].rotation);

        Arrive(_fromIndex); // announce the first room immediately
    }

    /// <summary>
    /// While the tour owns the view, switch off every other enabled camera (the XR
    /// rig camera, etc.) so the menu camera is the single, stable source — otherwise
    /// two full-screen cameras fight each frame and the image flickers/judders.
    /// Restores them when the tour stops (scenario start / leaving the menu).
    /// </summary>
    private void SuppressOtherCameras(bool suppress)
    {
        if (suppress)
        {
            if (_cam == null) return;
            _suppressedCams.Clear();
            foreach (var c in Camera.allCameras) // allCameras = currently enabled cameras
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

    /// <summary>
    /// Player selected a scenario: glide straight to <paramref name="index"/> and
    /// pause auto-advance for a while. If we're already parked there, just re-announce.
    /// </summary>
    public void GoToStop(int index)
    {
        if (index < 0 || index >= waypoints.Count) return;
        _autoPausedUntil = Time.time + autoResumeDelay;

        if (index == _fromIndex && _jumpTarget < 0 && _phase == Phase.Dwelling)
        {
            Arrive(index); // refresh selection without moving
            return;
        }

        _jumpStartPos = tourTarget.position;
        _jumpStartRot = tourTarget.rotation;
        _jumpTarget   = index;
        _phaseTimer   = 0f;
    }

    private void Arrive(int index)
    {
        OnArrive?.Invoke(index);
    }

    private void Update()
    {
        if (!_running || waypoints.Count == 0 || tourTarget == null) return;

        if (waypoints.Count == 1)
        {
            if (waypoints[0] != null) tourTarget.position = waypoints[0].position;
            ApplyLook(waypoints[0] != null ? waypoints[0].rotation : tourTarget.rotation);
            return;
        }

        _phaseTimer += Time.deltaTime;

        // ── Direct jump to a selected room ───────────────────────────────────
        if (_jumpTarget >= 0)
        {
            float jr = Mathf.Clamp01(_phaseTimer / travelTime);
            float jt = travelEase != null ? travelEase.Evaluate(jr) : jr;

            Vector3 toPos = waypoints[_jumpTarget] != null ? waypoints[_jumpTarget].position : tourTarget.position;
            tourTarget.position = Vector3.Lerp(_jumpStartPos, toPos, jt);

            Quaternion toRot = waypoints[_jumpTarget] != null ? waypoints[_jumpTarget].rotation : tourTarget.rotation;
            ApplyLook(Quaternion.Slerp(_jumpStartRot, toRot, jt));

            if (jr >= 1f)
            {
                _fromIndex  = _jumpTarget;
                _jumpTarget = -1;
                _phase      = Phase.Dwelling;
                _phaseTimer = 0f;
                Arrive(_fromIndex);
            }
            return;
        }

        // ── Dwelling ─────────────────────────────────────────────────────────
        if (_phase == Phase.Dwelling)
        {
            var wp = waypoints[_fromIndex];
            if (wp != null) tourTarget.position = wp.position;
            ApplyLook(wp != null ? wp.rotation : tourTarget.rotation);

            if (_phaseTimer >= dwellTime)
            {
                bool mayAdvance = autoAdvance && Time.time >= _autoPausedUntil;
                if (mayAdvance)
                {
                    _phaseTimer = 0f;
                    _phase = Phase.Travelling;
                }
                else
                {
                    _phaseTimer = dwellTime; // hold; don't let the timer run away
                }
            }
            return;
        }

        // ── Travelling (spline to the next room) ─────────────────────────────
        int toIndex = NextIndex(_fromIndex);
        float raw   = Mathf.Clamp01(_phaseTimer / travelTime);
        float t     = travelEase != null ? travelEase.Evaluate(raw) : raw;

        tourTarget.position = SplinePosition(_fromIndex, toIndex, t);

        Quaternion fromRot = waypoints[_fromIndex] != null ? waypoints[_fromIndex].rotation : tourTarget.rotation;
        Quaternion toRot2  = waypoints[toIndex]   != null ? waypoints[toIndex].rotation   : tourTarget.rotation;
        ApplyLook(Quaternion.Slerp(fromRot, toRot2, t));

        if (raw >= 1f)
        {
            _fromIndex  = toIndex;
            _phaseTimer = 0f;
            _phase      = Phase.Dwelling;
            Arrive(_fromIndex);

            if (!loop && _fromIndex == waypoints.Count - 1)
                _running = false;
        }
    }

    private void ApplyLook(Quaternion splineRot)
    {
        if (lookAtTarget != null)
        {
            Vector3 dir = lookAtTarget.position - tourTarget.position;
            if (dir.sqrMagnitude > 0.0001f)
            {
                Quaternion want = Quaternion.LookRotation(dir, Vector3.up);
                tourTarget.rotation = Quaternion.Slerp(tourTarget.rotation, want, lookLerpSpeed * Time.deltaTime);
            }
        }
        else
        {
            tourTarget.rotation = splineRot;
        }
    }

    // ── Spline ────────────────────────────────────────────────────────────────
    private int NextIndex(int i)
    {
        int n = waypoints.Count;
        return loop ? (i + 1) % n : Mathf.Min(i + 1, n - 1);
    }

    private int PrevIndex(int i)
    {
        int n = waypoints.Count;
        return loop ? (i - 1 + n) % n : Mathf.Max(i - 1, 0);
    }

    private Vector3 SplinePosition(int from, int to, float t)
    {
        Vector3 p1 = SafePos(from);
        Vector3 p2 = SafePos(to);
        Vector3 p0 = SafePos(PrevIndex(from));
        Vector3 p3 = SafePos(NextIndex(to));

        float t2 = t * t;
        float t3 = t2 * t;
        return 0.5f * (
            (2f * p1) +
            (-p0 + p2) * t +
            (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
            (-p0 + 3f * p1 - 3f * p2 + p3) * t3
        );
    }

    private Vector3 SafePos(int i)
    {
        if (i < 0 || i >= waypoints.Count || waypoints[i] == null) return tourTarget.position;
        return waypoints[i].position;
    }

    private void AutoCollectWaypoints()
    {
        waypoints.Clear();
        foreach (Transform child in transform)
        {
            string n = child.name;
            if (n.StartsWith("WP") || n.StartsWith("Waypoint"))
                waypoints.Add(child);
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (waypoints == null || waypoints.Count == 0) return;
        Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.9f);
        for (int i = 0; i < waypoints.Count; i++)
        {
            if (waypoints[i] == null) continue;
            Gizmos.DrawWireSphere(waypoints[i].position, 0.25f);
            Gizmos.DrawRay(waypoints[i].position, waypoints[i].forward * 0.6f);
            int next = (i + 1 < waypoints.Count) ? i + 1 : (loop ? 0 : -1);
            if (next >= 0 && waypoints[next] != null)
                Gizmos.DrawLine(waypoints[i].position, waypoints[next].position);
        }
    }
#endif
}
