using UnityEngine;

/// <summary>
/// STOP IT! — SkateboardRide (Scenario 4 — playroom skateboard → stairs)
///
/// Scripted slide: the skateboard rolls along a list of waypoints (mount spot → top of the stairs →
/// bottom of the stairs) and CARRIES the toddler until the player catches it. If the toddler reaches
/// the bottom still aboard, it slams the wall and dies (ChildNPC.HitWallDeath).
///
/// Deliberately a SEPARATE component (not a ChildNPC coroutine): catching the baby flips the game
/// state to Success, which cancels ChildNPC's coroutines — but the board must keep rolling (now empty)
/// to the bottom. Running the slide here keeps it alive through that state change.
///
/// Reset: on every Playing transition the board snaps back to its home pose, ready for the next run.
/// </summary>
public class SkateboardRide : MonoBehaviour
{
    [Tooltip("Ordered slide path (XZ only — the height comes from the surface below): top of the stairs, then the bottom.")]
    public Transform[] waypoints;
    [Tooltip("Roll speed (m/s) of the board along the path.")]
    public float rideSpeed = 2.5f;
    [Tooltip("Vertical offset to seat the toddler on top of the board.")]
    public float riderYOffset = 0.05f;
    [Tooltip("Yaw (deg) added to the board so its rolling axis (the wheels) faces travel — the model's nose isn't +Z.")]
    public float modelYawOffset = 90f;

    [Header("Surface follow (rides ON the stairs ramp instead of trusting waypoint heights)")]
    [Tooltip("Ride on top of whatever collider is below the board (the stairs ramp), so it never clips through.")]
    public bool followSurface = true;
    [Tooltip("Pitch the board to match the slope it rides (so it lies flat on the ramp, not horizontal).")]
    public bool alignToSurface = true;
    [Tooltip("Colliders the board may ride on. Default (everything) is fine — the board and rider are skipped in code.")]
    public LayerMask surfaceMask = ~0;
    [Tooltip("Small lift above the detected surface so the deck rests on top.")]
    public float surfaceLift = 0.03f;
    [Tooltip("How far above the board the downward surface probe starts.")]
    public float probeUp = 2f;
    [Tooltip("How far below the board the surface probe reaches.")]
    public float probeDown = 6f;

    private ChildNPC _child;
    private bool _riding;
    private int _wp;
    private Vector3 _homePos;
    private Quaternion _homeRot;
    private bool _homeRecorded;
    private Quaternion _riderFacing = Quaternion.identity;
    private Vector3 _surfaceNormal = Vector3.up;
    private static readonly RaycastHit[] _probe = new RaycastHit[8];

    private void Start()
    {
        _homePos = transform.position;
        _homeRot = transform.rotation;
        _homeRecorded = true;
        if (GameManager.Instance != null)
            GameManager.Instance.OnStateChanged.AddListener(OnStateChanged);
    }

    private void OnDestroy()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnStateChanged.RemoveListener(OnStateChanged);
    }

    private void OnStateChanged(GameManager.GameState state)
    {
        GameAudio.StopLoop("sfx_baby_skate"); // S4: stop the rolling loop on any state change (caught / crashed / next scenario)
        // Reset to home only on a (re)start — NOT on Success, so a mid-ride catch lets the empty board
        // finish rolling to the bottom before the next scenario resets it.
        if (state != GameManager.GameState.Playing) return;
        _riding = false;
        _wp = 0;
        _child = null;
        if (_homeRecorded)
        {
            transform.position = _homePos;
            transform.rotation = _homeRot;
        }
    }

    /// <summary>Called by ChildNPC once the toddler has mounted: start rolling down the path.</summary>
    public void Begin(ChildNPC child)
    {
        _child = child;
        _wp = 0;
        _riding = true;
        GameAudio.Loop("sfx_baby_skate"); // S4: rolling loop until the ride ends
    }

    private void Update()
    {
        if (!_riding || waypoints == null || waypoints.Length == 0) return;
        if (_wp >= waypoints.Length) { _riding = false; return; }

        var wp = waypoints[_wp];
        if (wp == null) { _wp++; return; }

        Vector3 cur = transform.position;

        // Advance horizontally toward the waypoint; the HEIGHT comes from the surface below (so we never clip the stairs).
        Vector2 flatCur  = new Vector2(cur.x, cur.z);
        Vector2 flatTgt  = new Vector2(wp.position.x, wp.position.z);
        Vector2 flatNext = Vector2.MoveTowards(flatCur, flatTgt, rideSpeed * Time.deltaTime);

        float y;
        if (followSurface && TrySampleSurface(flatNext.x, flatNext.y, cur.y, out float surfY, out Vector3 surfN))
        {
            y = surfY + surfaceLift;           // deck rests on the ramp/floor
            _surfaceNormal = surfN;
        }
        else
        {
            y = Mathf.MoveTowards(cur.y, wp.position.y, rideSpeed * 2f * Time.deltaTime); // bridge any gap toward the waypoint height
            _surfaceNormal = Vector3.up;
        }
        transform.position = new Vector3(flatNext.x, y, flatNext.y);

        // Orientation: the toddler stays upright facing travel; the board yaws toward travel and pitches to the slope.
        Vector2 d2 = flatTgt - flatCur;
        if (d2.sqrMagnitude > 0.0004f)
        {
            Vector3 travel = new Vector3(d2.x, 0f, d2.y).normalized;
            _riderFacing = Quaternion.LookRotation(travel, Vector3.up);
            Vector3 up = alignToSurface ? _surfaceNormal : Vector3.up;
            Vector3 fwd = Vector3.ProjectOnPlane(travel, up);
            if (fwd.sqrMagnitude < 1e-4f) fwd = travel;
            Quaternion boardFace = Quaternion.LookRotation(fwd.normalized, up) * Quaternion.Euler(0f, modelYawOffset, 0f);
            transform.rotation = Quaternion.Slerp(transform.rotation, boardFace, 1f - Mathf.Exp(-12f * Time.deltaTime)); // smooth pitch at the ramp lip
        }

        // Carry the toddler (upright, facing travel) until the player snatches it (then it rides the hand, not the board).
        if (_child != null && !_child.IsHeld)
        {
            _child.transform.position = transform.position + Vector3.up * riderYOffset;
            _child.transform.rotation = _riderFacing;
        }

        if (Vector2.Distance(flatNext, flatTgt) <= 0.15f)
        {
            _wp++;
            if (_wp >= waypoints.Length)
            {
                _riding = false;
                // Bottom reached. If the toddler is still aboard (not caught) → it hits the wall and dies.
                if (_child != null && !_child.IsHeld) _child.HitWallDeath();
            }
        }
    }

    /// <summary>Probe straight down for the highest collider below (the ramp), skipping the board and its rider.</summary>
    private bool TrySampleSurface(float x, float z, float curY, out float surfaceY, out Vector3 normal)
    {
        surfaceY = curY; normal = Vector3.up;
        Vector3 origin = new Vector3(x, curY + probeUp, z);
        int n = Physics.RaycastNonAlloc(origin, Vector3.down, _probe, probeUp + probeDown, surfaceMask, QueryTriggerInteraction.Ignore);
        bool found = false; float bestY = float.NegativeInfinity;
        for (int i = 0; i < n; i++)
        {
            Transform t = _probe[i].collider.transform;
            if (t == transform || t.IsChildOf(transform)) continue;                                  // the board itself (colliders are off anyway)
            if (_child != null && (t == _child.transform || t.IsChildOf(_child.transform))) continue; // the rider above the deck
            if (_probe[i].point.y > bestY) { bestY = _probe[i].point.y; normal = _probe[i].normal; found = true; }
        }
        if (found) surfaceY = bestY;
        return found;
    }
}
