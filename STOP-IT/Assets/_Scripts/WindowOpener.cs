using System.Collections;
using UnityEngine;

/// <summary>
/// STOP IT! — WindowOpener (Scenario 6 — Pigeon Room)
///
/// Detects the child's proximity each frame (no Rigidbody required — NavMeshAgent
/// characters don't have one, so OnTriggerEnter is unreliable).
///
/// When the child walks within triggerRadius metres of this object:
///   1. ChildNPC.PauseWalk() — child stops at the window.
///   2. windowPanel rotates open over openDuration seconds (grace period for the player).
///   3. ChildNPC.ResumeWalk() — child resumes toward HazardZone_PigeonWindow.
///
/// Auto-resets on GameManager → Playing so Restart / LaunchAll work correctly.
/// </summary>
public class WindowOpener : MonoBehaviour
{
    [Header("Window Visual")]
    [Tooltip("The window panel transform. Rotates openAngleDeg around its local Y axis when opened.")]
    public Transform windowPanel;

    [Tooltip("Degrees the panel rotates to simulate opening.")]
    public float openAngleDeg = 80f;

    [Tooltip("Seconds the child spends opening the window. " +
             "This is the grace period for the player to run upstairs.")]
    public float openDuration = 10f;

    [Header("Detection")]
    [Tooltip("Distance (metres) at which the child triggers the window-open sequence.")]
    public float triggerRadius = 0.8f;

    // ── Runtime ────────────────────────────────────────────────────────────
    private bool      _triggered     = false;
    private ChildNPC  _child         = null;
    private Quaternion _panelClosedRot = Quaternion.identity;

    // ── Unity ──────────────────────────────────────────────────────────────
    private void Awake()
    {
        if (windowPanel != null)
            _panelClosedRot = windowPanel.localRotation;
    }

    private void Start()
    {
        _child = FindAnyObjectByType<ChildNPC>();

        if (GameManager.Instance != null)
            GameManager.Instance.OnStateChanged.AddListener(OnStateChanged);
        else
            Debug.LogWarning("[WindowOpener] GameManager.Instance null at Start — will not auto-reset.", this);
    }

    private void OnDestroy()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnStateChanged.RemoveListener(OnStateChanged);
    }

    private void OnStateChanged(GameManager.GameState state)
    {
        // Reset each time a new scenario round begins (covers Restart and LaunchAll).
        if (state == GameManager.GameState.Playing)
            ResetWindow();
    }

    // ── Proximity detection (frame-accurate, no Rigidbody dependency) ───────
    private void Update()
    {
        if (_triggered) return;
        if (_child == null) { _child = FindAnyObjectByType<ChildNPC>(); return; }

        // Only trigger while the child is actively walking (not idle, not already caught).
        if (!_child.IsMoving) return;

        if (Vector3.Distance(transform.position, _child.transform.position) <= triggerRadius)
        {
            _triggered = true;
            StartCoroutine(OpenSequence(_child));
        }
    }

    // ── Sequence ────────────────────────────────────────────────────────────
    private System.Collections.IEnumerator OpenSequence(ChildNPC child)
    {
        // 1. Stop the child at the window (they "struggle" to open it).
        child.PauseWalk();

        // 2. Animate the panel over the full openDuration — this IS the 10-second grace period.
        float elapsed = 0f;

        if (windowPanel != null)
        {
            Quaternion start = windowPanel.localRotation;
            Quaternion end   = Quaternion.Euler(0f, openAngleDeg, 0f) * start;

            while (elapsed < openDuration)
            {
                // If the player already caught the child, abort cleanly.
                if (child == null || child.IsHeld) yield break;

                elapsed += Time.deltaTime;
                windowPanel.localRotation = Quaternion.Slerp(start, end, elapsed / openDuration);
                yield return null;
            }
            windowPanel.localRotation = end;
        }
        else
        {
            // No panel assigned — still wait the full duration.
            while (elapsed < openDuration)
            {
                if (child == null || child.IsHeld) yield break;
                elapsed += Time.deltaTime;
                yield return null;
            }
        }

        // 3. Release the child — they now walk the last stretch to the hazard.
        if (child != null && !child.IsHeld)
            child.ResumeWalk();
    }

    // ── Helpers ────────────────────────────────────────────────────────────
    private void ResetWindow()
    {
        _triggered = false;
        StopAllCoroutines();
        if (windowPanel != null)
            windowPanel.localRotation = _panelClosedRot;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.25f);
        Gizmos.DrawSphere(transform.position, triggerRadius);
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, triggerRadius);
    }
#endif
}
