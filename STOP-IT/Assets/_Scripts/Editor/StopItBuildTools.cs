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

    [MenuItem("Tools/STOP IT/Setup All Scenarios")]
    public static void SetupAllScenarios()
    {
        Debug.Log("[STOP IT] Setup All Scenarios — Setup Scenario 6 → Reposition Spawns → Bake NavMesh → Wire Scenarios.");
        SetupScenario6();   // creates Room_PigeonRoom + window + spawns + wires (includes WireScenarios)
        RepositionSpawns(); // canonical positions for ALL spawn points (including pigeon room)
        BakeNavMesh();
        Debug.Log("[STOP IT] Setup All Scenarios — done. Read warnings (if any) above and Play to verify.");
    }

    [MenuItem("Tools/STOP IT/Reposition Spawns")]
    public static void RepositionSpawns()
    {
        var house = GameObject.Find("House");
        Transform parent = house != null ? house.transform : null;
        if (parent == null)
            Debug.LogWarning("[STOP IT] No 'House' GameObject found — orphan spawns will be created at scene root.");

        int created = 0, updated = 0;
        foreach (var sp in HouseBuilder.SpawnDefinitions())
        {
            var go = GameObject.Find(sp.name);
            if (go == null)
            {
                go = new GameObject(sp.name);
                if (parent != null) go.transform.SetParent(parent, false);
                Undo.RegisterCreatedObjectUndo(go, "Create " + sp.name);
                created++;
            }
            else
            {
                Undo.RecordObject(go.transform, "Reposition " + sp.name);
                updated++;
            }
            go.transform.position = sp.pos;
            go.transform.rotation = Quaternion.Euler(0f, sp.yaw, 0f);
            EditorUtility.SetDirty(go);
        }

        if (!Application.isPlaying) UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
        Debug.Log($"[STOP IT] Spawn points repositioned at room entrances. Created {created}, updated {updated}. " +
                  "Run 'Wire Scenarios' next to refresh the ScenarioManager references.");
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

        // Define all 6 scenarios
        var configs = new ScenarioManager.ScenarioConfig[6];

        // 1. Living Room - fork in outlet
        configs[0] = BuildConfig("Salon — La prise électrique",       "ATTRAPE LE BÉBÉ !",
                                 "SpawnChild_Salon",    "HazardZone_Outlet",          "SpawnPlayer_Salon");

        // 2. Kitchen - cat in microwave
        configs[1] = BuildConfig("Cuisine — Le chat dans le micro-ondes", "ATTRAPE LE BÉBÉ AVANT LE CHAT !",
                                 "SpawnChild_Kitchen",  "HazardZone_Microwave",       "SpawnPlayer_Kitchen");

        // 3. Bathroom - cleaning product (swap mechanic — pick up water bottle, drop on hazard)
        configs[2] = BuildConfig("Salle de bain — Le produit ménager", "ÉCHANGE LE PRODUIT !",
                                 "SpawnChild_Bathroom", "HazardZone_CleaningProduct", "SpawnPlayer_Bathroom");
        configs[2].waterBottle = Object.FindAnyObjectByType<WaterBottle>();

        // 4. Stairs - skateboard (cosmetic skateboard visual under the NPC)
        configs[3] = BuildConfig("Escalier — Le skateboard",          "RATTRAPE-LE !",
                                 "SpawnChild_Stairs",   "HazardZone_StairsBottom",    "SpawnPlayer_Stairs");
        configs[3].showSkateboard = true;

        // 5. Bedroom - climbing east window ledge (pigeon flies away when NPC approaches)
        configs[4] = BuildConfig("Chambre — Le rebord de fenêtre",    "PORTE-LE LOIN DE LA FENÊTRE !",
                                 "SpawnChild_Bedroom",  "HazardZone_Window",          "SpawnPlayer_Bedroom");
        configs[4].pigeon = Object.FindAnyObjectByType<PigeonEscape>();

        // 6. Pigeon room - child climbs south window to catch pigeon
        configs[5] = BuildConfig("Attraper le pigeon — La fenêtre du sud", "ATTRAPE-LE AVANT QU'IL TOMBE !",
                                 "SpawnChild_PigeonRoom", "HazardZone_PigeonWindow",  "SpawnPlayer_PigeonRoom");

        sm.scenarios = configs;
        EditorUtility.SetDirty(sm);

        // Also wire the first scenario's hazard to childNPC
        if (childNPC != null && configs[0].hazardZone != null)
        {
            childNPC.targetHazard = configs[0].hazardZone;
            EditorUtility.SetDirty(childNPC);
        }

        if (!Application.isPlaying) UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
        Debug.Log("[STOP IT] All 5 scenarios wired to ScenarioManager! " +
                  "If any warnings printed above, fix them and re-run (e.g. 'Reposition Spawns' or 'Build House').");
    }

    private static ScenarioManager.ScenarioConfig BuildConfig(
        string scenarioName, string hint,
        string childSpawnName, string hazardName, string playerSpawnName)
    {
        var childSpawn  = FindTransform(childSpawnName);
        var hazard      = FindHazard(hazardName);
        var playerSpawn = FindTransform(playerSpawnName);

        if (childSpawn  == null) Debug.LogError($"[STOP IT] '{scenarioName}' — MISSING '{childSpawnName}'. NPC will spawn from previous scenario position.");
        if (hazard      == null) Debug.LogError($"[STOP IT] '{scenarioName}' — MISSING HazardZone '{hazardName}'. NPC will keep walking toward the previous target (the salon outlet on first run).");
        if (playerSpawn == null) Debug.LogWarning($"[STOP IT] '{scenarioName}' — missing '{playerSpawnName}'. Player will not be teleported (cosmetic).");

        return new ScenarioManager.ScenarioConfig
        {
            scenarioName     = scenarioName,
            actionHint       = hint,
            childSpawnPoint  = childSpawn,
            hazardZone       = hazard,
            playerSpawnPoint = playerSpawn,
            scenarioObjects  = new GameObject[0]
        };
    }

    [MenuItem("Tools/STOP IT/Diagnose Scenarios")]
    public static void DiagnoseScenarios()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("[STOP IT] === Scenario diagnostic ===");

        // 1. Inventory of HazardZones in the scene
        var hazards = Object.FindObjectsByType<HazardZone>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        sb.AppendLine($"\nHazardZone components in scene ({hazards.Length}):");
        foreach (var h in hazards)
            sb.AppendLine($"  • GO='{h.gameObject.name}'  hazardName='{h.hazardName}'  pos={h.transform.position}");

        // 2. Inventory of spawn-named GameObjects
        sb.AppendLine($"\nSpawn-point GameObjects (by name match):");
        foreach (var sp in HouseBuilder.SpawnDefinitions())
        {
            var go = GameObject.Find(sp.name);
            if (go != null) sb.AppendLine($"  ✓ '{sp.name}' found at {go.transform.position}");
            else            sb.AppendLine($"  ✗ '{sp.name}' MISSING (expected at {sp.pos})");
        }

        // 3. Current ScenarioManager wiring
        var sm = Object.FindAnyObjectByType<ScenarioManager>();
        if (sm == null)
        {
            sb.AppendLine("\n[ERROR] No ScenarioManager in scene.");
        }
        else if (sm.scenarios == null || sm.scenarios.Length == 0)
        {
            sb.AppendLine("\n[ERROR] ScenarioManager.scenarios is empty — run 'Wire Scenarios'.");
        }
        else
        {
            sb.AppendLine($"\nScenarioManager.scenarios ({sm.scenarios.Length}):");
            for (int i = 0; i < sm.scenarios.Length; i++)
            {
                var c = sm.scenarios[i];
                string spawn  = c.childSpawnPoint  != null ? c.childSpawnPoint.gameObject.name  : "<NULL>";
                string hazard = c.hazardZone       != null ? c.hazardZone.gameObject.name       : "<NULL>";
                string player = c.playerSpawnPoint != null ? c.playerSpawnPoint.gameObject.name : "<null>";
                string mark   = (c.hazardZone == null || c.childSpawnPoint == null) ? "✗" : "✓";
                sb.AppendLine($"  {mark} [{i}] '{c.scenarioName}'  child={spawn}  hazard={hazard}  player={player}");
            }
        }

        // 4. ChildNPC current state
        var child = Object.FindAnyObjectByType<ChildNPC>();
        if (child != null)
        {
            string th = child.targetHazard != null ? child.targetHazard.gameObject.name : "<NULL>";
            sb.AppendLine($"\nChildNPC.targetHazard at edit time = {th}  (this is the FIRST scenario's hazard until runtime overrides it)");
        }

        sb.AppendLine("\nIf any line above starts with ✗, fix it (rename / re-create the missing GameObject) and re-run 'Setup All Scenarios'.");
        Debug.Log(sb.ToString());
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

    // ─────────────────────────────────────────────────────────────────────
    // Scenario 6 — pigeon room setup (works on top of an existing scene)
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// One-click setup for scenario 6 on an existing scene.
    /// 1. Replaces the solid south wall (Ext_S) with a windowed version.
    /// 2. Creates Room_PigeonRoom with furniture + pigeon + HazardZone.
    /// 3. Creates spawn points.
    /// 4. Runs WireScenarios (adds scenario 6 to ScenarioManager).
    /// 5. Sets GameManager.totalScenarios = 6.
    /// After running, execute "Bake NavMesh" to update the navmesh.
    /// </summary>
    public static void SetupScenario6()
    {
        var house = GameObject.Find("House");
        Transform houseT = house != null ? house.transform : null;

        // 1. South wall: replace solid Ext_S with windowed version if needed
        var extS = GameObject.Find("Ext_S");
        bool alreadyWindowed = GameObject.Find("Ext_S_Base") != null;

        if (extS != null && !alreadyWindowed)
        {
            Undo.DestroyObjectImmediate(extS);
            // Window opening: X 0.7..2.3, Y 3.8..5.0, Z -6
            S6Wall(houseT, "Ext_S_Base",  new Vector3(0f,     1.9f, -6f), new Vector3(14.15f, 3.8f, 0.15f));
            S6Wall(houseT, "Ext_S_WinL",  new Vector3(-3.15f, 4.4f, -6f), new Vector3( 7.7f,  1.2f, 0.15f));
            S6Wall(houseT, "Ext_S_WinR",  new Vector3( 4.65f, 4.4f, -6f), new Vector3( 4.7f,  1.2f, 0.15f));
            S6Wall(houseT, "Ext_S_Top",   new Vector3(0f,     5.5f, -6f), new Vector3(14.15f, 1.0f, 0.15f));
            Debug.Log("[STOP IT] South wall replaced with windowed version (opening X:0.7..2.3, Y:3.8..5.0).");
        }
        else if (!alreadyWindowed)
        {
            Debug.LogWarning("[STOP IT] 'Ext_S' not found. If your scene has a custom south wall, " +
                             "manually add a window opening at X: 0.7..2.3, Y: 3.8..5.0, Z: -6.");
        }

        // 2. Create/replace Room_PigeonRoom
        var existing = GameObject.Find("Room_PigeonRoom");
        if (existing != null) Undo.DestroyObjectImmediate(existing);

        var r = new GameObject("Room_PigeonRoom");
        r.isStatic = true;
        if (houseT != null) r.transform.SetParent(houseT, false);
        Undo.RegisterCreatedObjectUndo(r, "Room_PigeonRoom");

        S6Obj(r, "Cot",            PrimitiveType.Cube,   new Vector3(0.8f,  3.25f, -4.5f),  new Vector3(0.9f,  0.5f,  1.5f),  "Mat_Furniture");
        S6Obj(r, "NightTable_P6",  PrimitiveType.Cube,   new Vector3(1.9f,  3.40f, -4.5f),  new Vector3(0.4f,  0.8f,  0.4f),  "Mat_Furniture");
        S6Obj(r, "ToyBox_P6",      PrimitiveType.Cube,   new Vector3(0.7f,  3.25f, -2.5f),  new Vector3(0.6f,  0.5f,  0.6f),  "Mat_Furniture");
        S6Obj(r, "WindowLedge_P6", PrimitiveType.Cube,   new Vector3(1.5f,  3.77f, -5.93f), new Vector3(1.8f,  0.05f, 0.3f),  "Mat_Furniture");

        // Window panel — rotated by WindowOpener when child "opens" it
        var panelGO = S6ObjRef(r, "WindowPanel_P6", PrimitiveType.Cube,
                               new Vector3(1.5f, 4.1f, -5.93f), new Vector3(1.5f, 1.1f, 0.06f), "Mat_Furniture");

        S6Obj(r, "Pigeon_P6",      PrimitiveType.Sphere, new Vector3(1.5f,  4.30f, -6.3f),  new Vector3(0.2f,  0.2f,  0.2f),  "Mat_Wall");

        // Hazard zone at the south window (child falls out after opening)
        var hzGO = GameObject.CreatePrimitive(PrimitiveType.Cube);
        hzGO.name = "HazardZone_PigeonWindow";
        hzGO.transform.SetParent(r.transform, false);
        hzGO.transform.position   = new Vector3(1.5f, 4.0f, -5.85f);
        hzGO.transform.localScale = new Vector3(1.6f, 0.6f, 0.4f);
        hzGO.isStatic = false;
        S6SetMat(hzGO, "Mat_Hazard");
        var hzCol = hzGO.GetComponent<BoxCollider>();
        if (hzCol) hzCol.isTrigger = true;
        var hz = hzGO.AddComponent<HazardZone>();
        hz.hazardName     = "Fenetre pigeon";
        hz.warningRadius  = 2f;
        hz.hazardRenderer = hzGO.GetComponent<Renderer>();
        Undo.RegisterCreatedObjectUndo(hzGO, "HazardZone_PigeonWindow");

        // WindowOpener trigger — child walks through it, stops to "open" the window (~8 s grace),
        // then resumes toward HazardZone_PigeonWindow. Player spawns downstairs and must run up in time.
        var woGO = new GameObject("WindowOpener_P6");
        woGO.transform.SetParent(r.transform, false);
        woGO.transform.position = new Vector3(1.5f, 3.5f, -5.2f);
        var woSphere = woGO.AddComponent<SphereCollider>();
        woSphere.radius   = 0.8f;
        woSphere.isTrigger = true;
        var wo = woGO.AddComponent<WindowOpener>();
        wo.windowPanel   = panelGO != null ? panelGO.transform : null;
        wo.openDuration  = 10f;
        wo.openAngleDeg  = 80f;
        Undo.RegisterCreatedObjectUndo(woGO, "WindowOpener_P6");

        // 3. Spawn points: child deep in the pigeon room, player at the base of the staircase (Y=0)
        S6Spawn(houseT, "SpawnChild_PigeonRoom",  new Vector3(0.8f,  3f, -2.5f));
        S6Spawn(houseT, "SpawnPlayer_PigeonRoom", new Vector3(4.5f,  0f, -2.0f));

        // 4. Wire all 6 scenarios into ScenarioManager
        WireScenarios();

        // 5. Set GameManager.totalScenarios = 6
        var gm = Object.FindAnyObjectByType<GameManager>();
        if (gm != null && gm.totalScenarios < 6)
        {
            Undo.RecordObject(gm, "Set totalScenarios 6");
            gm.totalScenarios = 6;
            EditorUtility.SetDirty(gm);
            Debug.Log("[STOP IT] GameManager.totalScenarios set to 6.");
        }

        if (!Application.isPlaying) UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
        Debug.Log("[STOP IT] Scenario 6 ready! Run 'Bake NavMesh' next to update navigation.");
    }

    // ── Helpers for SetupScenario6 ────────────────────────────────────────

    private static void S6Wall(Transform parent, string name, Vector3 pos, Vector3 scale)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        if (parent != null) go.transform.SetParent(parent, false);
        go.transform.position   = pos;
        go.transform.localScale = scale;
        go.isStatic = true;
        S6SetMat(go, "Mat_Wall");
        Undo.RegisterCreatedObjectUndo(go, name);
    }

    private static void S6Obj(GameObject parent, string name, PrimitiveType type, Vector3 pos, Vector3 scale, string mat)
        => S6ObjRef(parent, name, type, pos, scale, mat);

    private static GameObject S6ObjRef(GameObject parent, string name, PrimitiveType type, Vector3 pos, Vector3 scale, string mat)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent.transform, false);
        go.transform.position   = pos;
        go.transform.localScale = scale;
        go.isStatic = true;
        S6SetMat(go, mat);
        Undo.RegisterCreatedObjectUndo(go, name);
        return go;
    }

    private static void S6Spawn(Transform parent, string name, Vector3 pos)
    {
        var existing = GameObject.Find(name);
        if (existing != null) Undo.DestroyObjectImmediate(existing);
        var go = new GameObject(name);
        if (parent != null) go.transform.SetParent(parent, false);
        go.transform.position = pos;
        Undo.RegisterCreatedObjectUndo(go, name);
    }

    private static void S6SetMat(GameObject go, string matName)
    {
        var guids = AssetDatabase.FindAssets(matName + " t:Material");
        if (guids.Length == 0) return;
        var mat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guids[0]));
        if (mat != null) { var r = go.GetComponent<Renderer>(); if (r) r.sharedMaterial = mat; }
    }

    [MenuItem("Tools/STOP IT/Add First Stair Step")]
    public static void AddFirstStairStep()
    {
        // Some scenes are missing Step_0 — the first riser. Without it, the player
        // has to climb 60 cm in one move (floor → Step_1 top), which is taller
        // than the step-up threshold. This menu re-adds a Step_0 sized to match
        // the existing Step_1, positioned 0.45 m south and 0.30 m lower.

        var step1 = GameObject.Find("Step_1");
        if (step1 == null)
        {
            Debug.LogError("[STOP IT] Step_1 not found — cannot derive Step_0 position.");
            return;
        }

        var existing = GameObject.Find("Step_0");
        if (existing != null) Undo.DestroyObjectImmediate(existing);

        var step = GameObject.CreatePrimitive(PrimitiveType.Cube);
        step.name = "Step_0";
        step.transform.SetParent(step1.transform.parent, false);
        step.transform.position = new Vector3(
            step1.transform.position.x,
            step1.transform.position.y - step1.transform.localScale.y,   // one riser lower
            step1.transform.position.z - step1.transform.localScale.z    // one depth south
        );
        step.transform.localScale = step1.transform.localScale;
        step.isStatic = true;

        // Match material to other stair cubes.
        var guids = AssetDatabase.FindAssets("Mat_Floor t:Material");
        if (guids.Length > 0)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guids[0]));
            if (mat != null) step.GetComponent<Renderer>().sharedMaterial = mat;
        }

        Undo.RegisterCreatedObjectUndo(step, "Create Step_0");
        EditorUtility.SetDirty(step);
        if (!Application.isPlaying) UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
        Debug.Log($"[STOP IT] Step_0 created at {step.transform.position} (matched Step_1's scale).");
    }

    [MenuItem("Tools/STOP IT/Add Stair Ramp")]
    public static void AddStairRamp()
    {
        // Bottom of stairs at (5, 0, -5.5), top at (5, 3, -1). The ramp is an
        // invisible inclined slab the player can walk on — the visible stair cubes
        // remain unchanged. Configured to NOT contribute to the NavMesh bake so
        // the NPC keeps using the cube tops for its path.
        var existing = GameObject.Find("StairRamp");
        if (existing != null)
        {
            Undo.DestroyObjectImmediate(existing);
            Debug.Log("[STOP IT] Existing StairRamp removed before re-creating.");
        }

        Transform parent = GameObject.Find("Staircase")?.transform
                          ?? GameObject.Find("House")?.transform;

        var ramp = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ramp.name = "StairRamp";
        if (parent != null) ramp.transform.SetParent(parent, false);

        // Geometry: midpoint (5, 1.5, -3.25), length ~5.4m along slope,
        // angle atan2(3, 4.5) ≈ 33.69°.
        ramp.transform.position = new Vector3(5f, 1.5f, -3.25f);
        ramp.transform.rotation = Quaternion.Euler(-33.69f, 0f, 0f);
        ramp.transform.localScale = new Vector3(2f, 0.1f, 5.41f);

        // Invisible: keep collider, drop renderer.
        var mr = ramp.GetComponent<MeshRenderer>();
        if (mr != null) Object.DestroyImmediate(mr);
        var mf = ramp.GetComponent<MeshFilter>();
        if (mf != null) Object.DestroyImmediate(mf);

        // Keep the collider for the player capsule to slide on.
        var col = ramp.GetComponent<BoxCollider>();
        if (col != null) col.isTrigger = false;

        // Exclude from NavMesh so the NPC's path keeps following the stair cubes.
        var modifier = ramp.AddComponent<Unity.AI.Navigation.NavMeshModifier>();
        modifier.overrideArea = true;
        modifier.area = 1; // NotWalkable
        modifier.ignoreFromBuild = true;

        Undo.RegisterCreatedObjectUndo(ramp, "Create StairRamp");
        EditorUtility.SetDirty(ramp);
        if (!Application.isPlaying) UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
        Debug.Log("[STOP IT] StairRamp created (invisible slope, 33.69°) — walk forward to climb. NavMesh ignores it.");
    }

    [MenuItem("Tools/STOP IT/Apply Scenario Extras")]
    public static void ApplyScenarioExtras()
    {
        int updates = 0;

        var sm = Object.FindAnyObjectByType<ScenarioManager>();
        if (sm != null && sm.scenarios != null)
        {
            // Index 0 = Salon — toddler carries a fork toward the outlet.
            if (sm.scenarios.Length > 0)
            {
                var fork = GameObject.Find("Fork");
                if (fork != null)
                {
                    PrepareCarriable(fork);
                    sm.scenarios[0].carriedItem = fork;
                    sm.scenarios[0].carriedItemLocalPosition = new Vector3(0.6f, 0.1f, 0.5f);
                    sm.scenarios[0].carriedItemLocalEuler    = new Vector3(0f, 0f, 0f);
                    Debug.Log("[STOP IT] config[0].carriedItem ← Fork");
                }
                else Debug.LogWarning("[STOP IT] Fork GameObject not found in scene.");
                updates++;
            }
            // Index 1 = Kitchen — toddler carries the cat toward the microwave.
            if (sm.scenarios.Length > 1)
            {
                var cat = GameObject.Find("Cat");
                if (cat != null)
                {
                    PrepareCarriable(cat);
                    sm.scenarios[1].carriedItem = cat;
                    sm.scenarios[1].carriedItemLocalPosition = new Vector3(0.7f, 0.2f, 0.5f);
                    sm.scenarios[1].carriedItemLocalEuler    = new Vector3(0f, 0f, 0f);
                    Debug.Log("[STOP IT] config[1].carriedItem ← Cat");
                }
                else Debug.LogWarning("[STOP IT] Cat GameObject not found in scene.");
                updates++;
            }
            // Index 2 = Bathroom — wire WaterBottle + update action hint.
            if (sm.scenarios.Length > 2)
            {
                var wb = Object.FindAnyObjectByType<WaterBottle>();
                if (wb != null) sm.scenarios[2].waterBottle = wb;
                sm.scenarios[2].actionHint = "ÉCHANGE LE PRODUIT !";
                updates++;
            }
            // Index 3 = Stairs — carry the existing static Skateboard under the
            // toddler's feet for the duration of the scenario. If the Skateboard
            // was deleted from the scene, recreate it as a placeholder cube.
            if (sm.scenarios.Length > 3)
            {
                sm.scenarios[3].showSkateboard = false; // deprecated path
                var skate = GameObject.Find("Skateboard");
                if (skate == null)
                {
                    var parent = GameObject.Find("Staircase")?.transform
                              ?? GameObject.Find("House")?.transform;
                    skate = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    skate.name = "Skateboard";
                    if (parent != null) skate.transform.SetParent(parent, false);
                    skate.transform.position = new Vector3(5f, 3.05f, -1f);
                    skate.transform.localScale = new Vector3(0.7f, 0.05f, 0.25f);
                    var matGuids = AssetDatabase.FindAssets("Mat_Furniture t:Material");
                    if (matGuids.Length > 0)
                    {
                        var mat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(matGuids[0]));
                        if (mat != null) skate.GetComponent<Renderer>().sharedMaterial = mat;
                    }
                    // Skateboard must not have a collider while carried (would push
                    // the toddler around). Drop it.
                    var sCol = skate.GetComponent<Collider>();
                    if (sCol != null) Object.DestroyImmediate(sCol);
                    Undo.RegisterCreatedObjectUndo(skate, "Recreate Skateboard");
                    Debug.Log("[STOP IT] Skateboard placeholder re-created (it was missing).");
                }
                else
                {
                    // Drop the collider on the existing one too — same reason.
                    var sCol = skate.GetComponent<Collider>();
                    if (sCol != null) Object.DestroyImmediate(sCol);
                }
                PrepareCarriable(skate);
                sm.scenarios[3].carriedItem = skate;
                sm.scenarios[3].carriedItemLocalPosition = new Vector3(0f, -1.0f, 0f);
                sm.scenarios[3].carriedItemLocalEuler    = new Vector3(0f, 0f, 0f);
                Debug.Log("[STOP IT] config[3].carriedItem ← Skateboard");
                updates++;
            }
            // Index 4 = Bedroom — wire PigeonEscape (include inactive so we still
            // find it after a previous play hid its visuals).
            if (sm.scenarios.Length > 4)
            {
                var pe = Object.FindAnyObjectByType<PigeonEscape>(FindObjectsInactive.Include);
                if (pe != null) sm.scenarios[4].pigeon = pe;
                updates++;
            }
            EditorUtility.SetDirty(sm);

            // Diagnostic dump so we know the array is now in the expected state.
            var sb = new System.Text.StringBuilder("[STOP IT] Scenario carriedItem wiring after Apply:\n");
            for (int i = 0; i < sm.scenarios.Length; i++)
            {
                var c = sm.scenarios[i];
                sb.AppendLine($"  [{i}] {c.scenarioName} → carriedItem={(c.carriedItem ? c.carriedItem.name : "<null>")}, " +
                              $"pos={c.carriedItemLocalPosition}, euler={c.carriedItemLocalEuler}, showSkate={c.showSkateboard}");
            }
            Debug.Log(sb.ToString());
        }
        else
        {
            Debug.LogWarning("[STOP IT] ScenarioManager or its scenarios array not found.");
        }

        // Bump grabRadius on every ChildGrabber for VR room-scale comfort.
        int handCount = 0;
        foreach (var grabber in Object.FindObjectsByType<ChildGrabber>(FindObjectsInactive.Include))
        {
            grabber.grabRadius = 0.30f;
            EditorUtility.SetDirty(grabber);
            handCount++;
        }

        if (!Application.isPlaying) UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
        Debug.Log($"[STOP IT] Scenario extras applied — {updates} configs updated, grabRadius set on {handCount} hand(s).");
    }

    [MenuItem("Tools/STOP IT/Setup Cat & Window Actions")]
    public static void SetupCatAndWindowActions()
    {
        // Wires the two scenarios whose win condition changed from "grab the baby"
        // to a scenario-specific verb:
        //   • Cat scenario (Kitchen) → take the cat out of the toddler's hands (CatGrab).
        //   • Window scenario        → build an openable window + slam it shut (WindowCloser).
        // In both, the direct baby grab/touch is disabled (ChildNPC.canBeSavedDirectly via
        // ScenarioConfig.disableDirectChildSave). Scenario indices are resolved dynamically
        // (by carried item / hazard) so this works whatever the scenario count/order.
        var sm = Object.FindAnyObjectByType<ScenarioManager>();
        if (sm == null)
        {
            Debug.LogError("[STOP IT] ScenarioManager not found — run 'Setup All Scenarios' first.");
            return;
        }
        if (sm.scenarios == null || sm.scenarios.Length < 2)
        {
            Debug.LogError("[STOP IT] ScenarioManager has too few scenarios — run 'Wire Scenarios' first.");
            return;
        }

        int changes = 0;

        // ── Cat scenario — take the cat ──────────────────────────────────────
        var cat = GameObject.Find("Cat");
        if (cat != null)
        {
            int catIdx = FindScenarioByCarriedItem(sm, cat);
            if (catIdx < 0 && sm.scenarios.Length > 1) catIdx = 1; // sensible fallback (kitchen)
            if (catIdx >= 0)
            {
                PrepareCarriable(cat); // unmark static + disable colliders so it can ride the NPC
                if (sm.scenarios[catIdx].carriedItem == null)
                {
                    sm.scenarios[catIdx].carriedItem = cat;
                    sm.scenarios[catIdx].carriedItemLocalPosition = new Vector3(0.7f, 0.2f, 0.5f);
                }
                sm.scenarios[catIdx].disableDirectChildSave = true;
                sm.scenarios[catIdx].actionHint = "RÉCUPÈRE LE CHAT !";

                var catGrab = cat.GetComponent<CatGrab>() ?? cat.AddComponent<CatGrab>();
                catGrab.targetHazard = sm.scenarios[catIdx].hazardZone; // microwave (neutralised on win)
                EditorUtility.SetDirty(cat);
                Debug.Log($"[STOP IT] Cat — CatGrab attached; scenario index {catIdx} ('{sm.scenarios[catIdx].scenarioName}') direct save disabled.");
                changes++;
            }
        }
        else
        {
            Debug.LogWarning("[STOP IT] 'Cat' GameObject not found. Run 'Apply Scenario Extras' " +
                             "(it finds/wires the Cat) or create a 'Cat' object, then retry.");
        }

        // ── Window scenario — build an openable window + close action ────────
        var hazardGO = GameObject.Find("HazardZone_Window");
        var hazard = hazardGO != null ? hazardGO.GetComponent<HazardZone>() : null;
        if (hazard != null)
        {
            int winIdx = FindScenarioByHazard(sm, hazard);
            if (winIdx >= 0)
            {
                var opener = BuildBedroomWindow(hazardGO);
                if (opener != null)
                {
                    var closer = opener.GetComponent<WindowCloser>() ?? opener.gameObject.AddComponent<WindowCloser>();
                    closer.targetHazard = hazard;   // arms this closer for the window scenario + neutralised on win
                    sm.scenarios[winIdx].disableDirectChildSave = true;
                    sm.scenarios[winIdx].actionHint = "FERME LA FENÊTRE !";
                    EditorUtility.SetDirty(opener.gameObject);
                    Debug.Log($"[STOP IT] Window — built openable window + WindowCloser; scenario index {winIdx} ('{sm.scenarios[winIdx].scenarioName}') direct save disabled.");
                    changes++;
                }
            }
            else Debug.LogWarning("[STOP IT] No scenario uses HazardZone_Window — window action skipped.");
        }
        else
        {
            Debug.LogWarning("[STOP IT] 'HazardZone_Window' not found — window action skipped.");
        }

        EditorUtility.SetDirty(sm);
        if (!Application.isPlaying) UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
        Debug.Log($"[STOP IT] Cat & Window actions setup — {changes} change(s). " +
                  "Baby grab/touch disabled in those scenarios; player must take the cat / close the window. " +
                  "Test in Play mode (desktop: E near the object; VR: hold the 4 triggers near it).");
    }

    private static int FindScenarioByCarriedItem(ScenarioManager sm, GameObject item)
    {
        for (int i = 0; i < sm.scenarios.Length; i++)
            if (sm.scenarios[i] != null && sm.scenarios[i].carriedItem == item) return i;
        return -1;
    }

    private static int FindScenarioByHazard(ScenarioManager sm, HazardZone hz)
    {
        for (int i = 0; i < sm.scenarios.Length; i++)
            if (sm.scenarios[i] != null && sm.scenarios[i].hazardZone == hz) return i;
        return -1;
    }

    /// <summary>
    /// Builds (idempotently) the bedroom's openable window: a cosmetic panel in the
    /// east-wall opening + a WindowOpener trigger on the child's approach path. The
    /// child walks into the trigger, pauses to "open" the window (grace period), then
    /// resumes toward HazardZone_Window — giving the player time to run up and close it.
    /// Positions are derived from HazardZone_Window so they track the actual opening.
    /// Returns the WindowOpener (host for WindowCloser too).
    /// </summary>
    private static WindowOpener BuildBedroomWindow(GameObject hazardGO)
    {
        Transform parent = hazardGO.transform.parent; // Room_Bedroom
        Vector3 hz = hazardGO.transform.position;     // (6.917, 4.1, 2.722) — east wall, opening along Z

        // Cosmetic openable panel, in the opening, a bit above the sill.
        var existingPanel = GameObject.Find("WindowPanel_Bedroom");
        if (existingPanel != null) Undo.DestroyObjectImmediate(existingPanel);
        var panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
        panel.name = "WindowPanel_Bedroom";
        if (parent != null) panel.transform.SetParent(parent, false);
        panel.transform.position   = new Vector3(hz.x, hz.y + 0.30f, hz.z);
        panel.transform.rotation   = Quaternion.identity;
        panel.transform.localScale = new Vector3(0.08f, 1.2f, 1.8f);
        var pcol = panel.GetComponent<Collider>();
        if (pcol != null) Object.DestroyImmediate(pcol); // cosmetic — must not block the player capsule
        S6SetMat(panel, "Mat_Furniture");
        Undo.RegisterCreatedObjectUndo(panel, "WindowPanel_Bedroom");

        // Opener/closer host: a trigger sphere on the child's floor-level approach
        // (~1 m inside the room from the window).
        var existingOpener = GameObject.Find("WindowOpener_Bedroom");
        if (existingOpener != null) Undo.DestroyObjectImmediate(existingOpener);
        var woGO = new GameObject("WindowOpener_Bedroom");
        if (parent != null) woGO.transform.SetParent(parent, false);
        woGO.transform.position = new Vector3(hz.x - 1.0f, 3.0f, hz.z);
        var sphere = woGO.AddComponent<SphereCollider>();
        sphere.radius = 1.8f;
        sphere.isTrigger = true;
        var opener = woGO.AddComponent<WindowOpener>();
        opener.windowPanel  = panel.transform;
        opener.openDuration = 10f;
        opener.openAngleDeg = 80f;
        opener.triggerRadius = 1.8f;
        Undo.RegisterCreatedObjectUndo(woGO, "WindowOpener_Bedroom");

        return opener;
    }

    /// <summary>
    /// Unmarks the GameObject as static (Static-batched renderers don't follow
    /// their transform at runtime) and disables its colliders, so it can be
    /// parented to the NPC for carrying without pushing physics around.
    /// </summary>
    private static void PrepareCarriable(GameObject go)
    {
        if (go == null) return;
        GameObjectUtility.SetStaticEditorFlags(go, 0);
        go.isStatic = false;
        foreach (var col in go.GetComponentsInChildren<Collider>(includeInactive: true))
            col.enabled = false;
        EditorUtility.SetDirty(go);
    }

    [MenuItem("Tools/STOP IT/Create WaterBottle (Bathroom)")]
    public static void CreateWaterBottle()
    {
        // Bail out if it already exists.
        if (GameObject.Find("WaterBottle") != null)
        {
            Debug.Log("[STOP IT] WaterBottle already exists in scene — skipping.");
            return;
        }

        Transform parent = GameObject.Find("Room_Bathroom")?.transform;
        if (parent == null) parent = GameObject.Find("House")?.transform;

        var bottle = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        bottle.name = "WaterBottle";
        if (parent != null) bottle.transform.SetParent(parent, false);
        bottle.transform.position = new Vector3(-3.5f, 0.9f, -5.3f);
        bottle.transform.localScale = new Vector3(0.08f, 0.12f, 0.08f);

        // Try cyan-ish material first (Mat_Water) then fall back.
        var guids = AssetDatabase.FindAssets("Mat_Water t:Material");
        if (guids.Length == 0) guids = AssetDatabase.FindAssets("Mat_Wall t:Material");
        if (guids.Length > 0)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guids[0]));
            if (mat != null) bottle.GetComponent<Renderer>().sharedMaterial = mat;
        }

        // Trigger collider so the hand's overlap-sphere detects it without blocking.
        var col = bottle.GetComponent<Collider>();
        if (col != null) col.isTrigger = true;

        var rb = bottle.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        var wb = bottle.AddComponent<WaterBottle>();

        // Floating "grab me" chevron above the bottle.
        var indicator = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        indicator.name = "PickupIndicator";
        indicator.transform.SetParent(bottle.transform, false);
        indicator.transform.localPosition = new Vector3(0f, 2.5f, 0f);
        indicator.transform.localRotation = Quaternion.Euler(180f, 0f, 0f);
        indicator.transform.localScale = new Vector3(3f, 1.5f, 3f);
        var indCol = indicator.GetComponent<Collider>();
        if (indCol != null) Object.DestroyImmediate(indCol);
        if (guids.Length > 0)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guids[0]));
            if (mat != null) indicator.GetComponent<Renderer>().sharedMaterial = mat;
        }
        wb.pickupIndicator = indicator;

        Undo.RegisterCreatedObjectUndo(bottle, "Create WaterBottle");
        EditorUtility.SetDirty(bottle);
        if (!Application.isPlaying) UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
        Debug.Log("[STOP IT] WaterBottle created at (-3, 0.9, -4) in Room_Bathroom.");
    }

    [MenuItem("Tools/STOP IT/Spawn Floor Obstacles")]
    public static void SpawnFloorObstacles()
    {
        var existing = GameObject.Find("FloorObstacles");
        if (existing != null) Undo.DestroyObjectImmediate(existing);

        FloorObstacleAutoSetup.SpawnIntoScene();

        if (!Application.isPlaying)
            UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();

        Debug.Log("[STOP IT] 15 dalles violettes spawned. Ctrl+Z pour annuler.");
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
