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
    public enum ComfortMode { Glide, TeleportFade }

    [Header("Path")]
    [Tooltip("Ordered poses, one per room. If empty, children named 'WP*'/'Waypoint*' are collected.")]
    public List<Transform> waypoints = new List<Transform>();

    [Tooltip("Loop back to the first room after the last (recommended for a menu).")]
    public bool loop = true;

    [Tooltip("Transform the tour moves. The XR rig (XR Origin) for VR, or a dedicated camera for 2D.")]
    public Transform tourTarget;

    [Header("Comfort")]
    [Tooltip("Glide = smooth dolly (2D camera). TeleportFade = fade + snap (VR-comfortable, drives the rig).")]
    public ComfortMode comfortMode = ComfortMode.Glide;

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
            return;
        }

        _phase           = Phase.Dwelling;
        _phaseTimer      = 0f;
        _fromIndex       = 0;
        _jumpTarget      = -1;
        _autoPausedUntil = 0f;

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

        if (waypoints.Count == 1) { SetPose(0); return; }

        if (comfortMode == ComfortMode.TeleportFade) { TeleportUpdate(); return; }
        GlideUpdate();
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
