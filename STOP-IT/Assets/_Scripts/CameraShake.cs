using System.Collections;
using UnityEngine;

/// <summary>
/// STOP IT! — CameraShake
/// Drop-in shake helper. Any script can trigger a shake with:
///     CameraShake.Shake(0.3f, 0.25f);
///
/// Attaches itself to Camera.main automatically if no instance exists yet.
/// In VR, shakes are applied to the Camera transform (decoupled from tracking
/// by using a child offset transform), so they feel like a physical kick.
/// </summary>
public class CameraShake : MonoBehaviour
{
    private static CameraShake _instance;
    private Vector3 _originalLocalPos;
    private Coroutine _active;

    public static void Shake(float duration, float amplitude)
    {
        EnsureInstance();
        if (_instance == null) return;
        if (_instance._active != null) _instance.StopCoroutine(_instance._active);
        _instance._active = _instance.StartCoroutine(_instance.ShakeRoutine(duration, amplitude));
    }

    private static void EnsureInstance()
    {
        if (_instance != null) return;
        var cam = Camera.main;
        if (cam == null) return;
        _instance = cam.GetComponent<CameraShake>();
        if (_instance == null) _instance = cam.gameObject.AddComponent<CameraShake>();
    }

    private void Awake()
    {
        _instance = this;
        _originalLocalPos = transform.localPosition;
    }

    private IEnumerator ShakeRoutine(float duration, float amplitude)
    {
        float elapsed = 0f;
        Vector3 start = transform.localPosition;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float falloff = 1f - (elapsed / duration);
            Vector3 offset = Random.insideUnitSphere * amplitude * falloff;
            transform.localPosition = start + offset;
            yield return null;
        }
        transform.localPosition = start;
        _active = null;
    }
}
