using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Unity.AI.Navigation;

/// <summary>
/// STOP IT! — Editor Build Tools
/// Menu: Tools/STOP IT/...
/// </summary>
public static class StopItBuildTools
{
    [MenuItem("Tools/STOP IT/Bake NavMesh")]
    public static void BakeNavMesh()
    {
        var surface = Object.FindAnyObjectByType<NavMeshSurface>();
        if (surface == null) { Debug.LogError("[STOP IT] No NavMeshSurface found. Add one to the Floor."); return; }
        surface.BuildNavMesh();
        EditorUtility.SetDirty(surface);
        if (!Application.isPlaying) UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
        Debug.Log("[STOP IT] NavMesh baked.");
    }

    [MenuItem("Tools/STOP IT/Setup Scene")]
    public static void SetupScene()
    {
        var child = Object.FindAnyObjectByType<ChildNPC>();
        var hazard = Object.FindAnyObjectByType<HazardZone>();

        if (child != null && hazard != null && child.targetHazard == null)
        {
            child.targetHazard = hazard;
            EditorUtility.SetDirty(child);
            Debug.Log("[STOP IT] Wired ChildNPC.targetHazard → " + hazard.gameObject.name);
        }

        if (hazard != null && hazard.hazardRenderer == null)
        {
            hazard.hazardRenderer = hazard.GetComponent<Renderer>();
            EditorUtility.SetDirty(hazard);
            Debug.Log("[STOP IT] Wired HazardZone.hazardRenderer.");
        }

        var gm = Object.FindAnyObjectByType<GameManager>();
        if (gm != null && gm.audioSource == null)
        {
            var audio = gm.GetComponent<AudioSource>();
            if (audio != null) { gm.audioSource = audio; EditorUtility.SetDirty(gm); }
        }

        if (!Application.isPlaying) UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
        Debug.Log("[STOP IT] Scene setup complete!");
    }

    [MenuItem("Tools/STOP IT/Setup UI")]
    public static void SetupUI()
    {
        var canvasGO = GameObject.Find("ScenarioCanvas");
        if (canvasGO == null) { Debug.LogError("[STOP IT] ScenarioCanvas not found."); return; }

        // Resize canvas to 400×240 px (= 2m × 1.2m at scale 0.005)
        var rt = canvasGO.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(400, 240);

        var ui = canvasGO.GetComponent<ScenarioUI>();

        ui.scenarioNameText = CreateTMPChild(canvasGO, "ScenarioNameText",
            new Vector2(0, 90),  new Vector2(360, 50), 22, TextAlignmentOptions.Center);
        ui.scenarioNameText.text = ui.scenarioName;
        ui.scenarioNameText.color = new Color(1f, 0.85f, 0.2f);   // jaune

        ui.timerText = CreateTMPChild(canvasGO, "TimerText",
            new Vector2(-110, 10), new Vector2(120, 90), 72, TextAlignmentOptions.Center);
        ui.timerText.text = "30";
        ui.timerText.fontStyle = FontStyles.Bold;

        ui.scoreText = CreateTMPChild(canvasGO, "ScoreText",
            new Vector2(110, 10),  new Vector2(140, 50), 28, TextAlignmentOptions.Center);
        ui.scoreText.text = "0 / 3";

        ui.feedbackText = CreateTMPChild(canvasGO, "FeedbackText",
            new Vector2(0, -80), new Vector2(360, 70), 42, TextAlignmentOptions.Center);
        ui.feedbackText.text = string.Empty;
        ui.feedbackText.fontStyle = FontStyles.Bold;

        EditorUtility.SetDirty(canvasGO);
        if (!Application.isPlaying) UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
        Debug.Log("[STOP IT] UI créée : TimerText, ScenarioNameText, FeedbackText, ScoreText.");
    }

    private static TextMeshProUGUI CreateTMPChild(GameObject parent, string childName,
        Vector2 anchoredPos, Vector2 size, float fontSize, TextAlignmentOptions align)
    {
        var existing = parent.transform.Find(childName);
        GameObject go = existing != null ? existing.gameObject : new GameObject(childName);
        go.transform.SetParent(parent.transform, false);

        // Add TMP first — Unity converts Transform → RectTransform automatically
        var tmp = go.GetComponent<TextMeshProUGUI>() ?? go.AddComponent<TextMeshProUGUI>();

        // RectTransform now exists
        var rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = size;

        tmp.fontSize = fontSize;
        tmp.alignment = align;
        tmp.color = Color.white;
        tmp.text = string.Empty;
        return tmp;
    }

    [MenuItem("Tools/STOP IT/Fix Hand Colliders")]
    public static void FixHandColliders()
    {
        int count = 0;
        // Find all PlayerBlocker components (attached to the hands)
        foreach (var pb in Object.FindObjectsByType<PlayerBlocker>())
        {
            var go = pb.gameObject;
            if (go.GetComponent<SphereCollider>() != null) continue;

            var sc = go.AddComponent<SphereCollider>();
            sc.radius = 0.08f;
            sc.isTrigger = true;
            EditorUtility.SetDirty(go);
            Debug.Log("[STOP IT] SphereCollider (trigger) added to " + go.name);
            count++;
        }
        if (count == 0) Debug.Log("[STOP IT] Hand colliders already present.");
        if (!Application.isPlaying) UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
    }

    [MenuItem("Tools/STOP IT/Fix XR Camera")]
    public static void FixXRCamera()
    {
        var xrOrigin = Object.FindAnyObjectByType<Unity.XR.CoreUtils.XROrigin>();
        if (xrOrigin == null) { Debug.LogError("[STOP IT] XROrigin not found."); return; }

        // Find camera: first in XROrigin field, then in children, then anywhere in scene
        var cam = xrOrigin.Camera;
        if (cam == null) cam = xrOrigin.GetComponentInChildren<Camera>(true);
        if (cam == null) cam = Object.FindAnyObjectByType<Camera>();

        // If still no camera, create one under Camera Offset
        if (cam == null)
        {
            var offsetT = xrOrigin.CameraFloorOffsetObject != null
                ? xrOrigin.CameraFloorOffsetObject.transform
                : xrOrigin.transform;
            var camGO = new GameObject("Main Camera");
            camGO.transform.SetParent(offsetT, false);
            camGO.tag = "MainCamera";
            cam = camGO.AddComponent<Camera>();
            Debug.Log("[STOP IT] Created new camera under " + offsetT.name);
        }

        // Assign camera to XROrigin's m_Camera field via SerializedObject
        var so = new SerializedObject(xrOrigin);
        var camProp = so.FindProperty("m_Camera");
        if (camProp != null)
        {
            camProp.objectReferenceValue = cam;
            so.ApplyModifiedProperties();
            Debug.Log("[STOP IT] XROrigin.Camera assigned to " + cam.gameObject.name);
        }

        // Tag camera as MainCamera so Camera.main works
        if (cam.gameObject.tag != "MainCamera")
        {
            cam.gameObject.tag = "MainCamera";
            Debug.Log("[STOP IT] Camera tagged MainCamera.");
        }

        // Add AudioListener if missing
        if (cam.GetComponent<AudioListener>() == null)
        {
            cam.gameObject.AddComponent<AudioListener>();
            Debug.Log("[STOP IT] AudioListener added to camera.");
        }

        // Add TrackedPoseDriver if missing
        if (cam.GetComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>() == null)
        {
            cam.gameObject.AddComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
            Debug.Log("[STOP IT] TrackedPoseDriver added to camera.");
        }

        EditorUtility.SetDirty(xrOrigin.gameObject);
        EditorUtility.SetDirty(cam.gameObject);
        if (!Application.isPlaying) UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
        Debug.Log("[STOP IT] XR Camera fully configured.");
    }
}
