using UnityEngine;

/// <summary>
/// STOP IT! — LocomotionCasts
/// Shared physics-cast helpers used by BOTH player rigs — <see cref="XRLocomotionBinder"/> (VR)
/// and <see cref="DesktopTestRig"/> (desktop). The single rule that lives here: a LOCOMOTION cast
/// (ground-follow / wall-slide / step probe / jump ground check) must NEVER latch onto a gameplay
/// NPC — the toddler's solid CapsuleCollider. Otherwise walking up to the baby makes the ground ray
/// snap the rig's Y onto the child's head and the wall cast treat the child as a wall, lurching the
/// whole viewpoint (which reads in-headset as the "baby teleporting").
///
/// Putting this in ONE place is deliberate: the "ignore the toddler" rule can never again be fixed in
/// one mode and forgotten in the other — which is exactly what happened when only the VR rig was patched.
/// Both rigs are mutually exclusive at runtime, so the static scratch buffers are safe to share.
/// </summary>
public static class LocomotionCasts
{
    private static readonly RaycastHit[] _rayBuf = new RaycastHit[8];
    private static readonly RaycastHit[] _capBuf = new RaycastHit[8];

    /// <summary>True if the collider belongs to a gameplay NPC (the toddler) — a locomotion cast must skip it.</summary>
    public static bool IsNpc(Collider c) => c != null && c.GetComponentInParent<ChildNPC>() != null;

    /// <summary>Downward ground ray returning the nearest NON-NPC hit.</summary>
    public static bool Ground(Vector3 origin, float maxDistance, LayerMask mask, out RaycastHit best)
        => Ray(origin, Vector3.down, maxDistance, mask, out best);

    /// <summary>Ray (any direction) returning the nearest NON-NPC hit.</summary>
    public static bool Ray(Vector3 origin, Vector3 dir, float maxDistance, LayerMask mask, out RaycastHit best)
    {
        best = default;
        if (dir.sqrMagnitude < 1e-8f) return false;
        int n = Physics.RaycastNonAlloc(origin, dir.normalized, _rayBuf, maxDistance, mask, QueryTriggerInteraction.Ignore);
        return Nearest(_rayBuf, n, out best);
    }

    /// <summary>Capsule cast returning the nearest NON-NPC hit.</summary>
    public static bool Capsule(Vector3 point1, Vector3 point2, float radius, Vector3 dir, float maxDistance, LayerMask mask, out RaycastHit best)
    {
        best = default;
        if (dir.sqrMagnitude < 1e-8f) return false;
        int n = Physics.CapsuleCastNonAlloc(point1, point2, radius, dir.normalized, _capBuf, maxDistance, mask, QueryTriggerInteraction.Ignore);
        return Nearest(_capBuf, n, out best);
    }

    private static bool Nearest(RaycastHit[] buf, int count, out RaycastHit best)
    {
        best = default;
        float bestDist = float.PositiveInfinity;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            if (IsNpc(buf[i].collider)) continue;   // player walks right up to the toddler; never a wall
            // Skip INITIAL OVERLAPS (distance ~0). The single-hit Physics.CapsuleCast/Raycast the rigs
            // used before reported the first surface the sweep ENTERS, not colliders already touching the
            // capsule; a *NonAlloc cast returns those as distance-0 hits with a degenerate normal. Counting
            // one as a "wall ahead" wrongly blocks ALL movement — this froze the DESKTOP rig, whose capsule
            // bottom sits right on the floor (the VR capsule starts higher, so it never tripped this).
            if (buf[i].distance <= 1e-4f) continue;
            if (buf[i].distance < bestDist)
            {
                bestDist = buf[i].distance;
                best     = buf[i];
                found    = true;
            }
        }
        return found;
    }
}
