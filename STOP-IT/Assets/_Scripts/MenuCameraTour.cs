using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// STOP IT! — MenuCameraTour
/// Cinematic menu background: a dolly camera that glides smoothly from room to
/// room through an ordered list of waypoint poses, pausing briefly in each room,
/// then looping. Designed to run behind the floating <see cref="ScenarioMenu"/>
/// UI while the game is in <see cref="GameManager.GameState.Menu"/>.
///
/// Wiring (no scene-lock-free script — see CLAUDE.md for the LivingRoom lock):
///   1. Add an empty "MenuCamera" GameObject with a Camera component.
///   2. Attach this script to it (tourTarget defaults to its own transform).
///   3. Drop one empty Transform per room into <see cref="waypoints"/>, posed
///      exactly where/how you want the camera to sit (position + rotation).
///      Leave the list empty to auto-collect children named "WP*"/"Waypoint*".
///   4. Optionally tick <see cref="onlyDuringMenu"/> so the tour (and this
///      camera) only run in the Menu state and hand control back to the XR rig
///      when a scenario starts.
///
/// The motion uses a Catmull-Rom spline through the waypoint positions for a
/// continuous, drift-free glide, with an ease-in/out dwell at each room. Rotation
/// slerps between consecutive waypoint orientations (or toward an optional global
/// <see cref="lookAtTarget"/>).
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

    [Header("Timing")]
    [Tooltip("Seconds to travel between two consecutive rooms.")]
    [Min(0.1f)] public float travelTime = 4f;

    [Tooltip("Seconds to dwell (hold still) in each room before moving on.")]
    [Min(0f)] public float dwellTime = 2.5f;

    [Tooltip("Ease curve applied to the 0..1 travel progress (smooth start/stop).")]
    public AnimationCurve travelEase = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Look")]
    [Tooltip("If set, the camera always faces this target instead of slerping " +
             "between waypoint rotations (e.g. a roaming NPC or the house centre).")]
    public Transform lookAtTarget;

    [Tooltip("How quickly the camera re-orients toward lookAtTarget (deg/s feel).")]
    [Min(0.1f)] public float lookLerpSpeed = 3f;

    [Header("Lifecycle")]
    [Tooltip("Only run the tour while GameManager is in the Menu state. The camera " +
             "(and any AudioListener on it) is disabled when a scenario starts so " +
             "the XR rig takes over, and re-enabled on return to the menu.")]
    public bool onlyDuringMenu = true;

    [Tooltip("Start the camera exactly on the first waypoint pose on enable.")]
    public bool snapToFirstOnStart = true;

    // ── Runtime ──────────────────────────────────────────────────────────────
    private enum Phase { Dwelling, Travelling }
    private Phase _phase = Phase.Dwelling;
    private int   _fromIndex;          // current room we're leaving
    private float _phaseTimer;
    private bool  _running;
    private Camera _cam;
    private bool  _subscribed;

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

        if (snapToFirstOnStart && waypoints.Count > 0 && waypoints[0] != null)
        {
            tourTarget.SetPositionAndRotation(waypoints[0].position, waypoints[0].rotation);
        }

        // Decide the initial running state from the current game state.
        bool inMenu = GameManager.Instance == null ||
                      GameManager.Instance.State == GameManager.GameState.Menu;
        SetRunning(!onlyDuringMenu || inMenu);
    }

    private void OnDestroy()
    {
        if (_subscribed && GameManager.Instance != null)
            GameManager.Instance.OnStateChanged.RemoveListener(OnStateChanged);
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
        // Toggle the menu camera so the XR rig owns the view during gameplay.
        if (_cam != null) _cam.enabled = run;

        if (run)
        {
            _phase      = Phase.Dwelling;
            _phaseTimer = 0f;
            _fromIndex  = 0;
            if (snapToFirstOnStart && waypoints.Count > 0 && waypoints[0] != null)
                tourTarget.SetPositionAndRotation(waypoints[0].position, waypoints[0].rotation);
        }
    }

    private void Update()
    {
        if (!_running || waypoints.Count == 0 || tourTarget == null) return;

        // Single-waypoint degenerate case: just sit there (optionally look at target).
        if (waypoints.Count == 1)
        {
            if (waypoints[0] != null)
                tourTarget.position = waypoints[0].position;
            ApplyLook(waypoints[0] != null ? waypoints[0].rotation : tourTarget.rotation);
            return;
        }

        _phaseTimer += Time.deltaTime;

        if (_phase == Phase.Dwelling)
        {
            // Hold on the current waypoint pose.
            var wp = waypoints[_fromIndex];
            if (wp != null) tourTarget.position = wp.position;
            ApplyLook(wp != null ? wp.rotation : tourTarget.rotation);

            if (_phaseTimer >= dwellTime)
            {
                _phaseTimer = 0f;
                _phase = Phase.Travelling;
            }
            return;
        }

        // ── Travelling ─────────────────────────────────────────────────────
        int toIndex = NextIndex(_fromIndex);
        float raw   = Mathf.Clamp01(_phaseTimer / travelTime);
        float t     = travelEase != null ? travelEase.Evaluate(raw) : raw;

        tourTarget.position = SplinePosition(_fromIndex, toIndex, t);

        // Orientation: face the global target if set, else slerp between waypoint rotations.
        Quaternion fromRot = waypoints[_fromIndex] != null ? waypoints[_fromIndex].rotation : tourTarget.rotation;
        Quaternion toRot   = waypoints[toIndex]   != null ? waypoints[toIndex].rotation   : tourTarget.rotation;
        ApplyLook(Quaternion.Slerp(fromRot, toRot, t));

        if (raw >= 1f)
        {
            _fromIndex  = toIndex;
            _phaseTimer = 0f;
            _phase      = Phase.Dwelling;

            // Non-looping tour ends when it reaches the last room.
            if (!loop && _fromIndex == waypoints.Count - 1)
                _running = false;
        }
    }

    /// <summary>
    /// Either snaps toward the supplied "spline" rotation, or — when a global
    /// lookAtTarget is set — smoothly re-orients the camera to face it.
    /// </summary>
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
        if (loop) return (i + 1) % n;
        return Mathf.Min(i + 1, n - 1);
    }

    private int PrevIndex(int i)
    {
        int n = waypoints.Count;
        if (loop) return (i - 1 + n) % n;
        return Mathf.Max(i - 1, 0);
    }

    /// <summary>
    /// Catmull-Rom interpolation between waypoint[from] and waypoint[to], using the
    /// neighbouring waypoints as tangents for a continuous, natural glide.
    /// </summary>
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
        var pts = (waypoints != null && waypoints.Count > 0) ? waypoints : null;
        if (pts == null) return;

        Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.9f);
        for (int i = 0; i < pts.Count; i++)
        {
            if (pts[i] == null) continue;
            Gizmos.DrawWireSphere(pts[i].position, 0.25f);
            // Draw the camera facing direction.
            Gizmos.DrawRay(pts[i].position, pts[i].forward * 0.6f);

            int next = (i + 1 < pts.Count) ? i + 1 : (loop ? 0 : -1);
            if (next >= 0 && pts[next] != null)
                Gizmos.DrawLine(pts[i].position, pts[next].position);
        }
    }
#endif
}
