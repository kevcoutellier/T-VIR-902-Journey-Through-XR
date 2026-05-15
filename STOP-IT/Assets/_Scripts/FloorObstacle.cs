using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
public class FloorObstacle : MonoBehaviour
{
    [Range(0.05f, 0.95f)]
    public float speedMultiplier = 0.4f;

    public Vector3 size = new Vector3(1f, 0.08f, 1f);

    // ── Registre statique ─────────────────────────────────────────────────────
    private static readonly List<FloorObstacle> _all = new List<FloorObstacle>();

    public static float GetSpeedMultiplierAt(Vector3 worldPos)
    {
        float result = 1f;
        for (int i = 0; i < _all.Count; i++)
        {
            var obs = _all[i];
            if (obs == null || !obs.isActiveAndEnabled) continue;
            if (obs.ContainsPoint(worldPos))
                result = Mathf.Min(result, obs.speedMultiplier);
        }
        return result;
    }

    private void OnEnable()
    {
        if (!_all.Contains(this)) _all.Add(this);
    }

    private void OnDisable()
    {
        _all.Remove(this);
    }

    private void OnDestroy()
    {
        _all.Remove(this);
    }

    private void OnValidate()
    {
        transform.localScale = size;
    }

    private bool ContainsPoint(Vector3 worldPos)
    {
        Vector3 local = transform.InverseTransformPoint(worldPos);
        return Mathf.Abs(local.x) <= 0.5f && Mathf.Abs(local.z) <= 0.5f;
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.55f, 0f, 1f, 0.3f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawCube(Vector3.zero, Vector3.one);
        Gizmos.color = new Color(0.55f, 0f, 1f, 0.9f);
        Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
    }
}
