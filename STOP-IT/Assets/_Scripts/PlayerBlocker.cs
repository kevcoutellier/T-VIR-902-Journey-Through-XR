using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

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

    [Tooltip("If no Collider is present at runtime, a SphereCollider (trigger) is added automatically.")]
    public bool autoCreateCollider = true;

    [Tooltip("Radius of the auto-created hand sphere collider")]
    public float autoColliderRadius = 0.08f;

    [Tooltip("Haptic feedback intensity (0–1) — only if XR Controller is found")]
    [Range(0f, 1f)]
    public float hapticAmplitude = 0.5f;

    [Tooltip("Haptic duration in seconds")]
    public float hapticDuration = 0.1f;

    private void Awake()
    {
        if (autoCreateCollider && GetComponent<Collider>() == null)
        {
            var sc = gameObject.AddComponent<SphereCollider>();
            sc.radius = autoColliderRadius;
            sc.isTrigger = true;
        }
    }

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
        // XRBaseController is the historical name used in XRI < 3.x; in 3.x it lives under
        // UnityEngine.XR.Interaction.Toolkit. Both ship as part of the package, so we can call
        // SendHapticImpulse via reflection-light: try the modern controller first, fall back to legacy.
        var controller = GetComponentInParent<XRBaseController>();
        if (controller != null)
            controller.SendHapticImpulse(hapticAmplitude, hapticDuration);
    }
}
