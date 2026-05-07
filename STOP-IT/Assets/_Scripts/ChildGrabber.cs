using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

/// <summary>
/// STOP IT! — ChildGrabber
/// Attach next to PlayerBlocker on each hand. Listens for "trigger" calls from
/// XRLocomotionBinder (XR grip press) or DesktopTestRig (E key). When activated
/// near the baby, it picks the baby up — which counts as a save (one-shot win).
///
/// Block (touch) and Grab are two valid verbs for the same outcome:
///   • Block = reflex slap → handled by PlayerBlocker
///   • Grab  = deliberate save → handled here
/// </summary>
[RequireComponent(typeof(Transform))]
public class ChildGrabber : MonoBehaviour
{
    [Header("Detection")]
    [Tooltip("Sphere radius around the hand used to detect the baby")]
    public float grabRadius = 0.18f;

    [Tooltip("Tag used on the ChildNPC GameObject")]
    public string childTag = "Child";

    [Header("Haptics")]
    [Range(0f, 1f)] public float hapticAmplitude = 0.7f;
    public float hapticDuration = 0.18f;

    [Header("Debug")]
    public bool drawGizmo = true;

    private ChildNPC _heldChild;
#pragma warning disable CS0618
    private XRBaseController _controller;
#pragma warning restore CS0618
    private static readonly Collider[] _overlapBuffer = new Collider[8];

    private void Awake()
    {
#pragma warning disable CS0618
        _controller = GetComponentInParent<XRBaseController>();
#pragma warning restore CS0618
    }

    /// <summary>
    /// Called by XRLocomotionBinder (grip press) or DesktopTestRig (E key).
    /// Picks up the nearest ChildNPC inside the grab radius.
    /// </summary>
    public void Trigger()
    {
        if (_heldChild != null) return;

        int hitCount = Physics.OverlapSphereNonAlloc(transform.position, grabRadius, _overlapBuffer,
            ~0, QueryTriggerInteraction.Collide);

        ChildNPC closest = null;
        float closestSqr = float.PositiveInfinity;
        for (int i = 0; i < hitCount; i++)
        {
            var col = _overlapBuffer[i];
            if (col == null || !col.CompareTag(childTag)) continue;
            var npc = col.GetComponentInParent<ChildNPC>();
            if (npc == null) continue;
            if (npc.IsHeld) continue;
            float sqr = (col.transform.position - transform.position).sqrMagnitude;
            if (sqr < closestSqr) { closestSqr = sqr; closest = npc; }
        }

        if (closest == null) return;

        _heldChild = closest;
        _heldChild.Grab(transform);

        if (_controller != null)
            _controller.SendHapticImpulse(hapticAmplitude, hapticDuration);
    }

    /// <summary>Called when the grip / key is released.</summary>
    public void Release()
    {
        if (_heldChild == null) return;
        _heldChild.Release();
        _heldChild = null;
    }

    private void OnDisable()
    {
        // Safety: if the rig is torn down while holding, drop the baby cleanly.
        if (_heldChild != null) Release();
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!drawGizmo) return;
        Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.4f);
        Gizmos.DrawWireSphere(transform.position, grabRadius);
    }
#endif
}
