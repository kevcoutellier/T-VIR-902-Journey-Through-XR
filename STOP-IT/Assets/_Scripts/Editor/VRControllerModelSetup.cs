using UnityEditor;
using UnityEngine;
using Unity.XR.CoreUtils;

/// <summary>
/// STOP IT! — VR Controller Model Setup
/// Replaces the blue-cube fallback with the actual XRI Starter Assets Quest controller models.
/// Run once before building: Tools → STOP IT → Wire Controller Models
/// </summary>
public static class VRControllerModelSetup
{
    private const string LeftPrefabPath =
        "Assets/Samples/XR Interaction Toolkit/3.4.0/Starter Assets/Prefabs/Controllers/XR Controller Left.prefab";
    private const string RightPrefabPath =
        "Assets/Samples/XR Interaction Toolkit/3.4.0/Starter Assets/Prefabs/Controllers/XR Controller Right.prefab";

    [MenuItem("Tools/STOP IT/Wire Controller Models")]
    public static void WireControllerModels()
    {
        var leftPrefab  = AssetDatabase.LoadAssetAtPath<GameObject>(LeftPrefabPath);
        var rightPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RightPrefabPath);

        if (leftPrefab == null || rightPrefab == null)
        {
            Debug.LogError("[STOP IT] XRI Starter Assets controller prefabs not found. " +
                           "Import them via Window → Package Manager → XR Interaction Toolkit → Samples → Starter Assets.");
            return;
        }

        var xr = Object.FindAnyObjectByType<XROrigin>();
        if (xr == null) { Debug.LogError("[STOP IT] No XROrigin found in the open scene."); return; }

        int count = 0;
        count += Wire(xr, "Left Controller",  leftPrefab);
        count += Wire(xr, "Right Controller", rightPrefab);

        if (count > 0)
        {
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
            Debug.Log($"[STOP IT] Controller models wired ({count}/2). Scene saved.");
        }
        else
        {
            Debug.LogWarning("[STOP IT] No Left Controller / Right Controller found under XROrigin.");
        }
    }

    private static int Wire(XROrigin xr, string controllerName, GameObject prefab)
    {
        Transform ctrl = FindController(xr, controllerName);
        if (ctrl == null) return 0;

        // Remove any existing placeholder cube we added at runtime.
        var old = ctrl.Find("VR Controller Visual");
        if (old != null) Object.DestroyImmediate(old.gameObject);

        // Remove any previously wired model to avoid duplicates.
        var existing = ctrl.Find(prefab.name);
        if (existing != null) Object.DestroyImmediate(existing.gameObject);

        // Instantiate and parent.
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, ctrl);
        instance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        instance.transform.localScale = Vector3.one;
        EditorUtility.SetDirty(ctrl.gameObject);
        Debug.Log($"[STOP IT] Wired '{prefab.name}' under '{ctrl.name}'.");
        return 1;
    }

    private static Transform FindController(XROrigin xr, string name)
    {
        var offset = xr.CameraFloorOffsetObject;
        Transform t = offset != null ? offset.transform.Find(name) : null;
        if (t == null) t = xr.transform.Find(name);
        if (t == null)
            foreach (var child in xr.GetComponentsInChildren<Transform>(true))
                if (child.name == name) { t = child; break; }
        return t;
    }
}
