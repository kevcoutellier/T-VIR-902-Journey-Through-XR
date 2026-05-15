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

        // 3. Bathroom - cleaning product
        configs[2] = BuildConfig("Salle de bain — Le produit ménager", "BLOQUE-LE !",
                                 "SpawnChild_Bathroom", "HazardZone_CleaningProduct", "SpawnPlayer_Bathroom");

        // 4. Stairs - skateboard
        configs[3] = BuildConfig("Escalier — Le skateboard",          "RATTRAPE-LE !",
                                 "SpawnChild_Stairs",   "HazardZone_StairsBottom",    "SpawnPlayer_Stairs");

        // 5. Bedroom - climbing east window ledge
        configs[4] = BuildConfig("Chambre — Le rebord de fenêtre",    "PORTE-LE LOIN DE LA FENÊTRE !",
                                 "SpawnChild_Bedroom",  "HazardZone_Window",          "SpawnPlayer_Bedroom");

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
