using UnityEngine;

/// <summary>
/// STOP IT! — PlayerBlocker
/// Attach to the player's hands (or body collider).
/// When the player physically touches the child NPC, it intercepts them.
/// Works with both XR controllers (via physics) and the device simulator.
/// </summary>
public class PlayerBlocker : MonoBehaviour
{
    [Tooltip("Tag used on the ChildNPC GameObject")]
    public string childTag = "Child";

    [Tooltip("Haptic feedback intensity (0–1) — only if XR Controller is found")]
    [Range(0f, 1f)]
    public float hapticAmplitude = 0.5f;

    [Tooltip("Haptic duration in seconds")]
    public float hapticDuration = 0.1f;

    private void OnTriggerEnter(Collider other)
    {
        TryIntercept(other);
    }

    private void OnCollisionEnter(Collision collision)
    {
        TryIntercept(collision.collider);
    }

    private void TryIntercept(Collider other)
    {
        if (!other.CompareTag(childTag)) return;

        var child = other.GetComponentInParent<ChildNPC>();
        if (child == null) child = other.GetComponent<ChildNPC>();
        if (child == null) return;

        child.Intercept();
        TriggerHaptics();
    }

    private void TriggerHaptics()
    {
#if UNITY_XR_INTERACTION_TOOLKIT
        var controller = GetComponentInParent<UnityEngine.XR.Interaction.Toolkit.XRBaseController>();
        if (controller != null)
            controller.SendHapticImpulse(hapticAmplitude, hapticDuration);
#endif
    }
}
