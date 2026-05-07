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
        var surfaces = Object.FindObjectsByType<NavMeshSurface>(FindObjectsInactive.Exclude);
        if (surfaces.Length == 0) { Debug.LogError("[STOP IT] No NavMeshSurface found."); return; }
        foreach (var surface in surfaces)
        {
            surface.BuildNavMesh();
            EditorUtility.SetDirty(surface);
        }
        if (!Application.isPlaying) UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
        Debug.Log("[STOP IT] NavMesh baked on " + surfaces.Length + " surfaces.");
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

        ui.actionHintText = CreateTMPChild(canvasGO, "ActionHintText",
            new Vector2(0, 50), new Vector2(380, 50), 28, TextAlignmentOptions.Center);
        ui.actionHintText.text = string.Empty;
        ui.actionHintText.fontStyle = FontStyles.Italic | FontStyles.Bold;
        ui.actionHintText.color = new Color(0.2f, 0.95f, 1f);   // cyan

        EditorUtility.SetDirty(canvasGO);
        if (!Application.isPlaying) UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
        Debug.Log("[STOP IT] UI créée : TimerText, ScenarioNameText, FeedbackText, ScoreText, ActionHintText.");
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

    [MenuItem("Tools/STOP IT/Setup UX (Indicator + Vignette + Countdown + Shake)")]
    public static void SetupUX()
    {
        var child = Object.FindAnyObjectByType<ChildNPC>();
        var hazard = Object.FindAnyObjectByType<HazardZone>();
        var canvasGO = GameObject.Find("ScenarioCanvas");
        var gm = Object.FindAnyObjectByType<GameManager>();

        if (hazard == null) { Debug.LogError("[STOP IT] No HazardZone found."); return; }

        // 1) Hazard indicator (floating arrow)
        var existingIndicator = Object.FindAnyObjectByType<HazardIndicator>();
        if (existingIndicator == null)
        {
            var go = new GameObject("HazardIndicator");
            var ind = go.AddComponent<HazardIndicator>();
            ind.hazard = hazard;
            Debug.Log("[STOP IT] HazardIndicator created.");
        }

        // 2) Danger vignette on GameManager
        if (gm != null && gm.GetComponent<DangerVignette>() == null)
        {
            var v = gm.gameObject.AddComponent<DangerVignette>();
            v.hazard = hazard;
            Debug.Log("[STOP IT] DangerVignette added to GameManager.");
        }

        // 3) Intro countdown on ScenarioCanvas
        if (canvasGO != null && canvasGO.GetComponent<ScenarioIntroCountdown>() == null)
        {
            var c = canvasGO.AddComponent<ScenarioIntroCountdown>();
            c.child = child;
            Debug.Log("[STOP IT] ScenarioIntroCountdown added to ScenarioCanvas.");
        }

        // 4) Camera shake on main camera (auto-added at runtime too, but we can pre-create it)
        var cam = Camera.main;
        if (cam != null && cam.GetComponent<CameraShake>() == null)
        {
            cam.gameObject.AddComponent<CameraShake>();
            Debug.Log("[STOP IT] CameraShake added to Main Camera.");
        }

        if (!Application.isPlaying) UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
        Debug.Log("[STOP IT] UX setup complete!");
    }

    [MenuItem("Tools/STOP IT/Fix Hand Colliders")]
    public static void FixHandColliders()
    {
        int colliderCount = 0;
        int grabberCount  = 0;
        // Find all PlayerBlocker components (attached to the hands)
        foreach (var pb in Object.FindObjectsByType<PlayerBlocker>())
        {
            var go = pb.gameObject;
            if (go.GetComponent<SphereCollider>() == null)
            {
                var sc = go.AddComponent<SphereCollider>();
                sc.radius = 0.08f;
                sc.isTrigger = true;
                Debug.Log("[STOP IT] SphereCollider (trigger) added to " + go.name);
                colliderCount++;
            }
            if (go.GetComponent<ChildGrabber>() == null)
            {
                go.AddComponent<ChildGrabber>();
                Debug.Log("[STOP IT] ChildGrabber added to " + go.name);
                grabberCount++;
            }
            EditorUtility.SetDirty(go);
        }
        if (colliderCount == 0 && grabberCount == 0)
            Debug.Log("[STOP IT] Hand colliders + grabbers already present.");
        if (!Application.isPlaying) UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
    }

    [MenuItem("Tools/STOP IT/Wire Scenarios")]
    public static void WireScenarios()
    {
        var sm = Object.FindAnyObjectByType<ScenarioManager>();
        if (sm == null) { Debug.LogError("[STOP IT] ScenarioManager not found."); return; }

        var childNPC = Object.FindAnyObjectByType<ChildNPC>();
        var scenarioUI = Object.FindAnyObjectByType<ScenarioUI>();

        sm.childNPC = childNPC;
        sm.scenarioUI = scenarioUI;

        // Define all 5 scenarios
        var configs = new ScenarioManager.ScenarioConfig[5];

        // 1. Living Room - fork in outlet
        configs[0] = new ScenarioManager.ScenarioConfig
        {
            scenarioName = "Salon — La prise électrique",
            actionHint = "ATTRAPE LE BÉBÉ !",
            childSpawnPoint = FindTransform("SpawnChild_Salon"),
            hazardZone = FindHazard("HazardZone_Outlet"),
            playerSpawnPoint = FindTransform("SpawnPlayer_Salon"),
            scenarioObjects = new GameObject[0]
        };

        // 2. Kitchen - cat in microwave
        configs[1] = new ScenarioManager.ScenarioConfig
        {
            scenarioName = "Cuisine — Le chat dans le micro-ondes",
            actionHint = "ATTRAPE LE BÉBÉ AVANT LE CHAT !",
            childSpawnPoint = FindTransform("SpawnChild_Kitchen"),
            hazardZone = FindHazard("HazardZone_Microwave"),
            playerSpawnPoint = FindTransform("SpawnPlayer_Kitchen"),
            scenarioObjects = new GameObject[0]
        };

        // 3. Bathroom - cleaning product
        configs[2] = new ScenarioManager.ScenarioConfig
        {
            scenarioName = "Salle de bain — Le produit ménager",
            actionHint = "BLOQUE-LE !",
            childSpawnPoint = FindTransform("SpawnChild_Bathroom"),
            hazardZone = FindHazard("HazardZone_CleaningProduct"),
            playerSpawnPoint = FindTransform("SpawnPlayer_Bathroom"),
            scenarioObjects = new GameObject[0]
        };

        // 4. Stairs - skateboard
        configs[3] = new ScenarioManager.ScenarioConfig
        {
            scenarioName = "Escalier — Le skateboard",
            actionHint = "RATTRAPE-LE !",
            childSpawnPoint = FindTransform("SpawnChild_Stairs"),
            hazardZone = FindHazard("HazardZone_StairsBottom"),
            playerSpawnPoint = FindTransform("SpawnPlayer_Stairs"),
            scenarioObjects = new GameObject[0]
        };

        // 5. Bedroom - climbing window ledge
        configs[4] = new ScenarioManager.ScenarioConfig
        {
            scenarioName = "Chambre — Le rebord de fenêtre",
            actionHint = "PORTE-LE LOIN DE LA FENÊTRE !",
            childSpawnPoint = FindTransform("SpawnChild_Bedroom"),
            hazardZone = FindHazard("HazardZone_Window"),
            playerSpawnPoint = FindTransform("SpawnPlayer_Bedroom"),
            scenarioObjects = new GameObject[0]
        };

        sm.scenarios = configs;
        EditorUtility.SetDirty(sm);

        // Also wire the first scenario's hazard to childNPC
        if (childNPC != null && configs[0].hazardZone != null)
        {
            childNPC.targetHazard = configs[0].hazardZone;
            EditorUtility.SetDirty(childNPC);
        }

        if (!Application.isPlaying) UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
        Debug.Log("[STOP IT] All 5 scenarios wired to ScenarioManager!");
    }

    private static Transform FindTransform(string name)
    {
        var go = GameObject.Find(name);
        return go != null ? go.transform : null;
    }

    private static HazardZone FindHazard(string name)
    {
        var go = GameObject.Find(name);
        return go != null ? go.GetComponent<HazardZone>() : null;
    }

    private static GameObject[] FindRoomObjects(string roomName)
    {
        var go = GameObject.Find(roomName);
        return go != null ? new GameObject[] { go } : new GameObject[0];
    }

    [MenuItem("Tools/STOP IT/Create Menu")]
    public static void CreateMenu()
    {
        // Delete existing menu if any
        var existing = GameObject.Find("ScenarioMenu");
        if (existing != null) Object.DestroyImmediate(existing);

        var menuGO = new GameObject("ScenarioMenu");
        var canvas = menuGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        var rt = menuGO.GetComponent<RectTransform>();
        // Place menu in center of living room, facing south (toward player spawn)
        rt.position = new Vector3(-3, 1.5f, 0);
        rt.rotation = Quaternion.Euler(0, 180, 0);
        rt.sizeDelta = new Vector2(500, 400);
        rt.localScale = new Vector3(0.005f, 0.005f, 0.005f);

        menuGO.AddComponent<UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster>();
        var menu = menuGO.AddComponent<ScenarioMenu>();
        menu.scenarioManager = Object.FindAnyObjectByType<ScenarioManager>();

        EditorUtility.SetDirty(menuGO);
        if (!Application.isPlaying) UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
        Debug.Log("[STOP IT] ScenarioMenu created!");
    }

    [MenuItem("Tools/STOP IT/Add Desktop Test Rig")]
    public static void AddDesktopTestRig()
    {
        var xrOrigin = Object.FindAnyObjectByType<Unity.XR.CoreUtils.XROrigin>();
        if (xrOrigin == null) { Debug.LogError("[STOP IT] XROrigin not found — cannot attach DesktopTestRig."); return; }

        if (xrOrigin.GetComponent<DesktopTestRig>() == null)
        {
            xrOrigin.gameObject.AddComponent<DesktopTestRig>();
            EditorUtility.SetDirty(xrOrigin.gameObject);
            Debug.Log("[STOP IT] DesktopTestRig added to XROrigin. Press Play in editor — WASD/RMB/LMB to test without HMD.");
        }
        else
        {
            Debug.Log("[STOP IT] DesktopTestRig already present on XROrigin.");
        }

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
