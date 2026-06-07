using UnityEngine;
using UnityEditor;

/// <summary>
/// STOP-IT — collider generation for the imported StopItHouse.
/// - Box Collider on every wall / floor / slab / ceiling / railing / furniture mesh
///   (axis-aligned boxes -> Box Collider fits perfectly, cheapest option for Quest).
/// - Staircase steps get NO per-step collider; replaced by a single inclined ramp
///   so VR locomotion glides up smoothly.
/// - Glass panes, the sliding-window moving panels (scenario 5) and the roof shell
///   stay passable.
/// Run it from the menu  STOP-IT > Generate House Colliders , or it auto-runs once
/// after a script reload while HousePreview is open (guarded by the StairRamp child,
/// and limited to HousePreview so it never touches the locked LivingRoom scene).
/// Idempotent: skips objects that already have a collider, rebuilds the ramp.
/// </summary>
public static class StopItColliderTool
{
    const string ROOT = "StopItHouse";
    const string SCENE = "HousePreview";

    [MenuItem("STOP-IT/Generate House Colliders")]
    public static void Generate()
    {
        var root = GameObject.Find(ROOT);
        if (root == null)
        {
            Debug.LogError("[StopItColliders] '" + ROOT + "' not found in the active scene.");
            return;
        }
        Run(root);
    }

    // Auto-trigger after a domain reload (recompile), one-shot & HousePreview-only.
    [InitializeOnLoadMethod]
    static void AutoRun()
    {
        EditorApplication.delayCall += () =>
        {
            var root = GameObject.Find(ROOT);
            if (root == null) return;
            if (root.scene.name != SCENE) return;                 // never touch other scenes
            if (root.transform.Find("StairRamp") != null) return; // already generated
            Run(root);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(root.scene);
            Debug.Log("[StopItColliders] auto-run complete, scene saved.");
        };
    }

    static void Run(GameObject root)
    {
        int added = 0, skipped = 0;
        Bounds stair = new Bounds();
        bool stairInit = false;

        foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
        {
            var go = mf.gameObject;
            string n = go.name;
            var rend = go.GetComponent<Renderer>();

            if (n.StartsWith("G_Stair"))                 // steps -> ramp later, no collider
            {
                if (rend != null)
                {
                    if (!stairInit) { stair = rend.bounds; stairInit = true; }
                    else stair.Encapsulate(rend.bounds);
                }
                skipped++; continue;
            }
            if (n.StartsWith("WIN_Slide_panel") || n.EndsWith("_glass") || n == "Roof")
            {
                skipped++; continue;                     // stay passable
            }
            if (go.GetComponent<Collider>() != null) { skipped++; continue; }
            if (rend != null)
            {
                var s = rend.bounds.size;
                if (Mathf.Max(s.x, Mathf.Max(s.y, s.z)) < 0.05f) { skipped++; continue; }
            }

            go.AddComponent<BoxCollider>();              // auto-fits mesh bounds
            added++;
        }

        var old = root.transform.Find("StairRamp");
        if (old != null) Object.DestroyImmediate(old.gameObject);

        if (stairInit)
        {
            float cx = stair.center.x;
            float w = stair.size.x;
            Vector3 pBottom = new Vector3(cx, stair.min.y, stair.max.z + 0.20f);
            Vector3 pTop = new Vector3(cx, stair.max.y, stair.min.z);
            Vector3 dir = pTop - pBottom;
            float len = dir.magnitude;

            var ramp = new GameObject("StairRamp");
            ramp.transform.SetParent(root.transform, true);
            ramp.transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
            const float th = 0.30f;
            ramp.transform.position = (pBottom + pTop) * 0.5f - ramp.transform.up * (th * 0.5f);
            var bc = ramp.AddComponent<BoxCollider>();
            bc.size = new Vector3(Mathf.Max(w, 0.1f), th, Mathf.Max(len, 0.1f));
            added++;
        }

        EditorUtility.SetDirty(root);
        if (!Application.isPlaying)
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(root.scene);
        Debug.Log("[StopItColliders] BoxColliders added: " + added +
                  " | skipped: " + skipped + " | ramp: " + stairInit);
    }
}
