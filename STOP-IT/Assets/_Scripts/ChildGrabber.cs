using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

/// <summary>
/// STOP IT! — ChildGrabber
/// Attach next to PlayerBlocker on each hand. Listens for "trigger" calls from
/// XRLocomotionBinder (VR: the 4 triggers held together) or DesktopTestRig (E key).
/// When activated near the baby, it picks the baby up — which counts as a save
/// (one-shot win), unless the active scenario disabled the direct save.
///
/// Block (touch) and Grab are two valid verbs for the same outcome:
///   • Block = reflex slap → handled by PlayerBlocker
///   • Grab  = deliberate save → handled here
///
/// Bathroom scenario extension: the same trigger picks up a WaterBottle, and a
/// second press near the cleaning-product hazard performs the swap (success).
/// The same input path works for VR (4 triggers) and desktop (E key) — both call
/// Trigger() through their respective rig.
/// </summary>
[RequireComponent(typeof(Transform))]
public class ChildGrabber : MonoBehaviour
{
    [Header("Detection")]
    [Tooltip("Sphere radius around the hand used to detect the baby / bottle / hazard zone")]
    public float grabRadius = 0.30f;

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
    private static readonly Collider[] _overlapBuffer = new Collider[16];

    private void Awake()
    {
#pragma warning disable CS0618
        _controller = GetComponentInParent<XRBaseController>();
#pragma warning restore CS0618
    }

    /// <summary>
    /// Called by XRLocomotionBinder (VR: the 4 triggers held together) or
    /// DesktopTestRig (E key).
    /// Priority:
    ///   1. If a WaterBottle is held → try to drop it on its hazard zone (camera distance).
    ///   2. Else if a WaterBottle is near the player camera → pick it up.
    ///   3. Else if a scenario interactable (cat, window, …) consumes the press → done.
    ///   4. Else pick up the nearest ChildNPC via hand overlap-sphere (one-shot win) —
    ///      unless that scenario disabled the direct save, in which case Grab() refuses
    ///      and nothing happens (player must use the scenario verb instead).
    ///
    /// WaterBottle / interactable paths use the CAMERA position (not the hand) because
    /// the desktop rig spawns a temporary "hand" GameObject 1.6m in front of the camera,
    /// which would never sit close enough to a 0.9m-high bottle for an overlap-sphere hit.
    /// </summary>
    public void Trigger()
    {
        if (_heldChild != null) return;

        // --- WaterBottle paths: use camera distance (rig-agnostic) ---
        var cam = Camera.main;
        if (cam != null)
        {
            Vector3 camPos = cam.transform.position;
            var bottles = Object.FindObjectsByType<WaterBottle>(FindObjectsInactive.Exclude);

            // 1. Held bottle → try drop on hazard.
            foreach (var wb in bottles)
            {
                if (!wb.IsHeld || wb.targetHazardZone == null) continue;
                float d = Vector3.Distance(camPos, wb.targetHazardZone.transform.position);
                if (d <= wb.interactionRadius && wb.TryDropAt(wb.targetHazardZone))
                {
                    if (_controller != null)
                        _controller.SendHapticImpulse(hapticAmplitude, hapticDuration);
                    return;
                }
            }

            // 2. Not held + player close → pickup.
            foreach (var wb in bottles)
            {
                if (wb.IsHeld) continue;
                float d = Vector3.Distance(camPos, wb.transform.position);
                if (d <= wb.interactionRadius && wb.TryPickup(transform))
                {
                    if (_controller != null)
                        _controller.SendHapticImpulse(hapticAmplitude, hapticDuration);
                    return;
                }
            }
        }

        // --- Scenario interactables (cat, window, …) via the proximity registry ---
        // Same input as the baby grab; each interactable decides whether the player
        // is close enough. First one to consume the press wins (and stops us from
        // also trying to grab the baby).
        if (cam != null)
        {
            Vector3 camPos = cam.transform.position;
            var interactables = ProximityInteractables.All;
            for (int i = 0; i < interactables.Count; i++)
            {
                if (interactables[i] != null && interactables[i].TryInteract(camPos))
                {
                    if (_controller != null)
                        _controller.SendHapticImpulse(hapticAmplitude, hapticDuration);
                    return;
                }
            }
        }

        // --- ChildNPC path: hand overlap-sphere (unchanged) ---
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

        // Only latch onto the child if the grab actually took. Grab() refuses when
        // the current scenario disables the direct save (cat / window) — without this
        // check the grabber would think it holds a child it isn't, and stay stuck.
        if (closest.Grab(transform))
        {
            _heldChild = closest;
            if (_controller != null)
                _controller.SendHapticImpulse(hapticAmplitude, hapticDuration);
        }
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
