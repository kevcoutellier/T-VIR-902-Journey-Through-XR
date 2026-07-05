using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// STOP IT! — CityBackdropBuilder
/// Generates a low-poly city skyline OUTSIDE the bedroom window so that, in game,
/// looking through the window reads as "we're in a city" instead of an empty void.
///
/// Why procedural boxes instead of a flat image: it matches the low-poly art style,
/// costs almost nothing (a few dozen boxes on shared materials), and — crucially for
/// VR — has real parallax when the player moves their head, which a billboard image
/// does not. The colleague's distance fog fades the far buildings into a city haze.
///
/// Placement auto-derives from the window ("HazardZone_Window") and the house centre,
/// so it always sits on the correct (outward) side. Re-running rebuilds "CityBackdrop"
/// — idempotent. Save the scene afterwards (Ctrl+S).
/// </summary>
public static class CityBackdropBuilder
{
    private const string RootName   = "CityBackdrop";
    private const string WindowName = "HazardZone_Window";

    // Distance band (m from the window) and lateral spread of the skyline.
    private const float NearDist = 11f;
    private const float FarDist  = 26f;
    private const float Spread   = 18f;
    private const int   Count    = 46;

    // Muted low-poly city palette (URP Lit base colours).
    private static readonly Color[] Palette =
    {
        new Color(0.56f, 0.58f, 0.62f), // concrete grey-blue
        new Color(0.62f, 0.58f, 0.50f), // warm beige
        new Color(0.48f, 0.52f, 0.58f), // slate
        new Color(0.66f, 0.67f, 0.70f), // light concrete
        new Color(0.42f, 0.45f, 0.50f), // dark slate
    };

    [MenuItem("Tools/STOP IT/Build City Backdrop Window")]
    public static void BuildCityBackdrop()
    {
        var window = GameObject.Find(WindowName);
        if (window == null)
        {
            Debug.LogWarning($"[CityBackdrop] '{WindowName}' not found in the open scene — cannot place the city.");
            return;
        }

        // Outward = from the house centre toward the window, flattened to the ground plane.
        Vector3 w = window.transform.position;
        Vector3 centre = HouseCentre(w);
        Vector3 outward = w - centre; outward.y = 0f;
        if (outward.sqrMagnitude < 0.01f) outward = Vector3.left;
        outward.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, outward); // lateral (tangent) axis
        Vector3 ground = new Vector3(w.x, 0f, w.z);

        // Rebuild cleanly.
        var previous = GameObject.Find(RootName);
        if (previous != null) Undo.DestroyObjectImmediate(previous);
        var root = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(root, "Build City Backdrop");

        var mats = BuildMaterials();

        // A muted ground slab under the skyline so buildings don't float over the green lawn.
        var slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
        slab.name = "CityGround";
        StripCollider(slab);
        slab.transform.SetParent(root.transform, false);
        slab.transform.position = ground + outward * ((NearDist + FarDist) * 0.5f);
        slab.transform.rotation = Quaternion.LookRotation(outward, Vector3.up);
        slab.transform.localScale = new Vector3(Spread * 3.2f, 0.2f, (FarDist - NearDist) + 24f);
        slab.transform.position += Vector3.up * -0.1f;
        slab.GetComponent<MeshRenderer>().sharedMaterial = GroundMaterial();

        // Deterministic skyline (fixed seed → same city every rebuild, no Random.* surprises).
        var rng = new System.Random(9021);
        for (int i = 0; i < Count; i++)
        {
            float dist    = NearDist + (float)rng.NextDouble() * (FarDist - NearDist);
            float lateral = ((float)rng.NextDouble() * 2f - 1f) * Spread;
            // Taller towers tend to sit further back, so the skyline reads with depth.
            float depth01 = Mathf.InverseLerp(NearDist, FarDist, dist);
            float h = Mathf.Lerp(3.5f, 9f, (float)rng.NextDouble()) + depth01 * Mathf.Lerp(0f, 9f, (float)rng.NextDouble());
            float bw = 2f + (float)rng.NextDouble() * 3.5f;
            float bd = 2f + (float)rng.NextDouble() * 3.5f;

            Vector3 pos = ground + outward * dist + right * lateral;
            pos.y = h * 0.5f; // base rests on the ground (y = 0)

            var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
            b.name = $"Building_{i:00}";
            StripCollider(b);
            b.transform.SetParent(root.transform, false);
            b.transform.position = pos;
            b.transform.rotation = Quaternion.LookRotation(outward, Vector3.up);
            b.transform.localScale = new Vector3(bw, h, bd);
            b.GetComponent<MeshRenderer>().sharedMaterial = mats[rng.Next(mats.Length)];
            Undo.RegisterCreatedObjectUndo(b, "City Building");
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Selection.activeGameObject = root;
        Debug.Log($"[CityBackdrop] Built {Count} low-poly buildings outside '{WindowName}' " +
                  $"(outward {outward}). Save the scene (Ctrl+S). The distance fog fades the far ones into haze.");
    }

    private static Vector3 HouseCentre(Vector3 fallbackNear)
    {
        var hazards = Object.FindObjectsByType<HazardZone>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (hazards.Length == 0) return fallbackNear + Vector3.right * 5f; // nudge so outward isn't zero
        Vector3 sum = Vector3.zero;
        foreach (var h in hazards) sum += h.transform.position;
        Vector3 c = sum / hazards.Length; c.y = 0f;
        return c;
    }

    private static Material[] BuildMaterials()
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var list = new List<Material>();
        foreach (var col in Palette)
        {
            var m = new Material(shader) { name = "CityMat" };
            m.SetColor("_BaseColor", col);
            m.SetColor("_Color", col); // Standard fallback
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.1f);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.1f);
            list.Add(m);
        }
        return list.ToArray();
    }

    private static Material GroundMaterial()
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var m = new Material(shader) { name = "CityGroundMat" };
        var col = new Color(0.34f, 0.36f, 0.38f); // asphalt-ish, sits under the towers
        m.SetColor("_BaseColor", col);
        m.SetColor("_Color", col);
        return m;
    }

    private static void StripCollider(GameObject go)
    {
        var col = go.GetComponent<Collider>();
        if (col != null) Object.DestroyImmediate(col); // purely visual — no physics / NavMesh impact
    }
}
