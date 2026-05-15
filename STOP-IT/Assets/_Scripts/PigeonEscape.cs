using System.Collections;
using UnityEngine;

/// <summary>
/// STOP IT! — PigeonEscape
/// Window scenario flourish: when the toddler gets close to the ledge, the pigeon
/// flutters away (placeholder = primitive Sphere translated upward + outward over
/// 0.8s). Purely cosmetic — does not affect win/fail logic.
///
/// Hiding strategy: at the end of the flight we disable the *renderers*, not the
/// whole GameObject. That keeps the component findable by FindAnyObjectByType
/// after a play (otherwise the next Apply Scenario Extras / restart couldn't
/// rewire it, and the bird would never come back).
/// </summary>
public class PigeonEscape : MonoBehaviour
{
    [Header("Detection")]
    [Tooltip("If null, the pigeon will find the active ChildNPC at Start.")]
    public ChildNPC childNPC;

    [Tooltip("Distance from child at which the pigeon takes off (metres). Pick a value > 2 m so the bird leaves clearly BEFORE the toddler reaches the ledge.")]
    public float triggerDistance = 3f;

    [Header("Flight")]
    [Tooltip("World-space delta added to start position during the flight.")]
    public Vector3 flightOffset = new Vector3(5f, 3f, -1f);

    [Tooltip("Flight duration in seconds. Longer = more readable take-off.")]
    public float flightDuration = 1.5f;

    [Tooltip("Wing-flap amplitude (degrees) for the Z-axis sinusoidal rotation.")]
    public float flapAngle = 45f;

    [Tooltip("Wing-flap frequency (Hz).")]
    public float flapFrequency = 7f;

    [Tooltip("Vertical bob (metres) added on each flap to mimic each wingbeat lifting the bird.")]
    public float flapBobHeight = 0.10f;

    [Tooltip("How much the bird squashes vertically on each flap (0 = none, 0.3 = obvious).")]
    public float flapSquash = 0.25f;

    private Vector3 _initialPos;
    private Quaternion _initialRot;
    private Renderer[] _renderers;
    private bool _flying;
    private Coroutine _flightRoutine;

    private void Awake()
    {
        _initialPos = transform.position;
        _initialRot = transform.rotation;
        _renderers = GetComponentsInChildren<Renderer>(includeInactive: true);
    }

    private void Start()
    {
        if (childNPC == null) childNPC = FindAnyObjectByType<ChildNPC>();

        // Fail-safe self-reset on every Playing state transition. Ensures the
        // bird reappears at its perch when the player retries the scenario,
        // even if the ScenarioManager config reference was lost.
        if (GameManager.Instance != null)
            GameManager.Instance.OnStateChanged.AddListener(OnGameStateChanged);
    }

    private void OnDestroy()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnStateChanged.RemoveListener(OnGameStateChanged);
    }

    private void OnGameStateChanged(GameManager.GameState state)
    {
        if (state == GameManager.GameState.Playing) ResetPigeon(null);
    }

    private void Update()
    {
        if (_flying || childNPC == null) return;
        float sqr = (childNPC.transform.position - transform.position).sqrMagnitude;
        if (sqr <= triggerDistance * triggerDistance)
            _flightRoutine = StartCoroutine(FlyAway());
    }

    private IEnumerator FlyAway()
    {
        _flying = true;
        Vector3 start = transform.position;
        Vector3 end = start + flightOffset;
        Vector3 startScale = transform.localScale;
        float t = 0f;
        while (t < flightDuration)
        {
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / flightDuration);
            float ease = 1f - Mathf.Pow(1f - u, 2f); // soft ease-out

            // Each wingbeat is a sinusoid. We sync the bob, the roll, and the
            // squash off this phase so the bird visibly "pulls itself up" on
            // every flap rather than translating like a glider.
            float wingPhase = t * Mathf.PI * 2f * flapFrequency;
            float wingSin = Mathf.Sin(wingPhase);
            float wingAbs = Mathf.Abs(wingSin);

            // Position: lerp toward the target + per-flap upward bob.
            Vector3 basePos = Vector3.Lerp(start, end, ease);
            basePos.y += wingAbs * flapBobHeight * (1f - u * 0.5f); // bob fades a bit
            transform.position = basePos;

            // Rotation: wing-flap roll + gradual yaw away from the room.
            float roll = wingSin * flapAngle;
            float yaw  = u * 60f;
            transform.rotation = _initialRot * Quaternion.Euler(0f, yaw, roll);

            // Scale: vertical squash on each wing extension, horizontal stretch
            // to preserve volume — reads as "wings extending".
            float squash = 1f + wingAbs * flapSquash;
            transform.localScale = new Vector3(
                startScale.x * squash,
                startScale.y / squash,
                startScale.z * squash);

            yield return null;
        }
        transform.localScale = startScale;
        // After the flight: keep the GameObject active (so it stays findable),
        // just hide its visuals.
        SetVisible(false);
    }

    /// <summary>Restore the pigeon to its perched position. Called when the window scenario (re)starts.</summary>
    public void ResetPigeon(ChildNPC activeChild)
    {
        if (_flightRoutine != null) { StopCoroutine(_flightRoutine); _flightRoutine = null; }
        _flying = false;
        transform.position = _initialPos;
        transform.rotation = _initialRot;
        if (activeChild != null) childNPC = activeChild;
        gameObject.SetActive(true);
        SetVisible(true);
    }

    private void SetVisible(bool on)
    {
        if (_renderers == null) return;
        for (int i = 0; i < _renderers.Length; i++)
            if (_renderers[i] != null) _renderers[i].enabled = on;
    }
}
