using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// Editor tool that builds the cinematic menu into the CURRENTLY OPEN scene
/// (HousePreview / any house). It adapts to the actual house by deriving the room
/// markers from the real per-scenario anchors in the scene (ScenarioManager
/// configs, or SpawnChild_S1..S5 markers) — no hard-coded coordinates.
///
/// Default build = a dedicated dolly camera in FOLLOW mode that trails the two
/// roaming NPCs around the house, with the scenario showcase as a full-screen 2D
/// overlay. The MenuCamera (TeleportFade) + world-space UI VR path is also
/// supported by the scripts — flip MenuCameraTour.comfortMode to TeleportFade and
/// MenuScenarioShowcase.worldSpace to true when testing in the headset.
///
/// Re-running deletes and rebuilds "MenuTour" — idempotent.
/// </summary>
public static class MenuTourBuilder
{
    // Real baby assets (shared with the gameplay child built by StoryModeSetup) — the menu
    // strollers use the same rigged, animated model instead of a capsule placeholder.
    private const string WalkingBabyPath    = "Assets/_ThirdParty/Models/WalkingBaby.fbx";
    private const string BabyControllerPath = "Assets/_ThirdParty/Models/BabyController.controller";

    private static readonly string[] Objectives =
    {
        "Empêche l'enfant d'enfoncer une fourchette dans la prise.",
        "Il veut glisser le chat dans le micro-ondes.",
        "Il s'élance pour dévaler l'escalier en skateboard.",
        "Il s'apprête à boire un produit ménager.",
        "Il grimpe sur le rebord pour attraper un pigeon.",
    };

    private static readonly Vector3[] FallbackAnchors =
    {
        new Vector3(-5f, 0f,  4.5f), new Vector3(5f, 0f, 4.5f),
        new Vector3( 5f, 0f, -4.5f), new Vector3(-5f, 0f, -4.5f),
        new Vector3( 5f, 3f, -4.5f),
    };

    private const float ViewDistance = 2.6f;
    private const float EyeHeight    = 1.6f;

    [MenuItem("Tools/STOP IT/Build Menu Tour")]
    public static void BuildMenuTour()
    {
        var previous = GameObject.Find("MenuTour");
        if (previous != null) Undo.DestroyObjectImmediate(previous);

        var root = new GameObject("MenuTour");
        Undo.RegisterCreatedObjectUndo(root, "Build Menu Tour");

        // 1. Real scenario anchors in this house (room markers for the showcase sync).
        var anchors = CollectScenarioAnchors();
        Vector3 centroid = Centroid(anchors);

        // 2. Room-marker waypoints (also serve as camera poses if you switch to Glide/Teleport).
        var wpHolder = new GameObject("Waypoints");
        wpHolder.transform.SetParent(root.transform, false);
        Undo.RegisterCreatedObjectUndo(wpHolder, "Waypoints");

        var wps = new List<Transform>();
        for (int i = 0; i < anchors.Count; i++)
        {
            ComputeStopPose(anchors[i], centroid, out Vector3 pos, out Quaternion rot);
            var wp = new GameObject($"WP_S{i + 1}");
            wp.transform.SetParent(wpHolder.transform, false);
            wp.transform.SetPositionAndRotation(pos, rot);
            Undo.RegisterCreatedObjectUndo(wp, "Waypoint");
            wps.Add(wp.transform);
        }

        // 3. Comfort fader (used only if you switch the camera to TeleportFade for VR).
        var faderGO = new GameObject("MenuFader");
        faderGO.transform.SetParent(root.transform, false);
        var fader = faderGO.AddComponent<MenuFader>();
        Undo.RegisterCreatedObjectUndo(faderGO, "Menu Fader");

        // 4. Roam points + the two strollers the camera will follow.
        var roamPts = BuildRoamPoints(root.transform, anchors);
        var strollers = new List<Transform>();
        if (roamPts.Count > 0)
        {
            strollers.Add(BuildRoamer(root.transform, "MenuStroller_A", roamPts[0].position, roamPts, false).transform);
            strollers.Add(BuildRoamer(root.transform, "MenuStroller_B",
                          roamPts[Mathf.Min(1, roamPts.Count - 1)].position, roamPts, true).transform);
        }

        // 5. Follow camera that trails the strollers.
        var tour = BuildFollowCamera(root.transform, wps, strollers, fader, centroid);

        // 6. Scenario showcase (2D overlay).
        BuildShowcase(root.transform, tour, anchors.Count);

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Selection.activeGameObject = root;

        Debug.Log($"[STOP IT] Menu Tour built — {anchors.Count} rooms. The MenuCamera follows the two " +
                  "strollers (real WalkingBaby, animated) around the house; the showcase shows the scenario " +
                  "of the room they're in. Bake the NavMesh if the house changed, then save the scene. " +
                  "For VR: set MenuCamera ▸ comfortMode = TeleportFade and MenuShowcase ▸ worldSpace = true.");
    }

    // ── Scenario anchors ──────────────────────────────────────────────────
    private static List<Vector3> CollectScenarioAnchors()
    {
        var result = new List<Vector3>();

        var sm = Object.FindAnyObjectByType<ScenarioManager>();
        if (sm != null && sm.scenarios != null && sm.scenarios.Length > 0)
        {
            foreach (var cfg in sm.scenarios)
            {
                if (cfg.hazardZone != null)            result.Add(cfg.hazardZone.transform.position);
                else if (cfg.childSpawnPoint != null)  result.Add(cfg.childSpawnPoint.position);
            }
            if (result.Count > 0) return result;
        }

        for (int i = 1; i <= 5; i++)
        {
            var marker = GameObject.Find($"SpawnChild_S{i}");
            if (marker != null) result.Add(marker.transform.position);
        }
        if (result.Count > 0) return result;

        result.AddRange(FallbackAnchors);
        return result;
    }

    private static Vector3 Centroid(List<Vector3> pts)
    {
        if (pts.Count == 0) return Vector3.zero;
        Vector3 c = Vector3.zero;
        foreach (var p in pts) c += p;
        return c / pts.Count;
    }

    /// <summary>An eye-height camera pose standing back from the anchor toward the house interior.</summary>
    private static void ComputeStopPose(Vector3 anchor, Vector3 centroid, out Vector3 pos, out Quaternion rot)
    {
        Vector3 dir = centroid - anchor; dir.y = 0f;
        if (dir.sqrMagnitude < 0.01f) dir = Vector3.forward;
        dir.Normalize();

        pos = anchor + dir * ViewDistance;
        pos.y = anchor.y + EyeHeight;
        Vector3 look = (anchor + Vector3.up * 0.8f) - pos;
        rot = look.sqrMagnitude > 0.001f ? Quaternion.LookRotation(look, Vector3.up) : Quaternion.identity;
    }

    // ── Follow camera ────────────────────────────────────────────────────
    private static MenuCameraTour BuildFollowCamera(Transform parent, List<Transform> wps,
                                                    List<Transform> followTargets, MenuFader fader, Vector3 centroid)
    {
        var camGO = new GameObject("MenuCamera");
        camGO.transform.SetParent(parent, false);
        Undo.RegisterCreatedObjectUndo(camGO, "Menu Camera");

        var cam = camGO.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.depth = 10; // renders on top of / instead of the suppressed XR camera in 2D
        cam.fieldOfView = 60f;
        cam.nearClipPlane = 0.05f;

        // Start near the house centre looking in, so frame 1 isn't pointed at the void.
        camGO.transform.position = centroid + Vector3.up * EyeHeight - Vector3.forward * ViewDistance;

        var tour = camGO.AddComponent<MenuCameraTour>();
        tour.waypoints = wps;                // room markers for showcase sync
        tour.tourTarget = camGO.transform;
        tour.comfortMode = MenuCameraTour.ComfortMode.Follow;
        tour.followTargets = new List<Transform>(followTargets);
        tour.followBackDistance = 3f;
        tour.followHeight = 1.8f;
        tour.followLookHeight = 0.9f;
        tour.followSmoothTime = 0.6f;
        tour.followAimLerp = 4f;
        tour.followSwitchInterval = 10f;
        tour.fader = fader;
        tour.onlyDuringMenu = true;

        EditorUtility.SetDirty(tour);
        return tour;
    }

    // ── Showcase UI ────────────────────────────────────────────────────────
    private static void BuildShowcase(Transform parent, MenuCameraTour tour, int stops)
    {
        var go = new GameObject("MenuShowcase", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        Undo.RegisterCreatedObjectUndo(go, "Menu Showcase");

        var showcase = go.AddComponent<MenuScenarioShowcase>();
        showcase.cameraTour = tour;
        showcase.scenarioManager = Object.FindAnyObjectByType<ScenarioManager>();
        showcase.worldSpace = false; // 2D overlay; flip to true for VR

        var map = new int[stops];
        for (int i = 0; i < stops; i++) map[i] = i;
        showcase.scenarioPerStop = map;
        showcase.objectiveOverrides = (string[])Objectives.Clone();

        EditorUtility.SetDirty(showcase);
    }

    // ── Roam points + strollers ─────────────────────────────────────────────
    private static List<Transform> BuildRoamPoints(Transform parent, List<Vector3> anchors)
    {
        var holder = new GameObject("RoamPoints");
        holder.transform.SetParent(parent, false);
        Undo.RegisterCreatedObjectUndo(holder, "Roam Points");

        var pts = new List<Transform>();
        float minY = float.MaxValue;
        foreach (var a in anchors) minY = Mathf.Min(minY, a.y);

        int idx = 0;
        foreach (var a in anchors)
        {
            if (a.y > minY + 1.5f) continue; // ground floor only
            Vector3 p = a;
            if (NavMesh.SamplePosition(a, out NavMeshHit hit, 3f, NavMesh.AllAreas)) p = hit.position;
            var go = new GameObject($"Roam_{idx++}");
            go.transform.SetParent(holder.transform, false);
            go.transform.position = p;
            Undo.RegisterCreatedObjectUndo(go, "Roam Point");
            pts.Add(go.transform);
        }
        return pts;
    }

    private static GameObject BuildRoamer(Transform parent, string name, Vector3 startPos,
                                          List<Transform> roamPoints, bool sequential)
    {
        var npc = new GameObject(name);
        npc.transform.SetParent(parent, false);
        npc.transform.position = startPos;
        Undo.RegisterCreatedObjectUndo(npc, "Menu Stroller");

        // Real WalkingBaby.fbx (rigged + animated) as the stroller's visual, so the menu
        // shows the actual child walking rather than a capsule placeholder.
        var mesh = BuildBabyMesh(npc.transform);

        var agent = npc.AddComponent<NavMeshAgent>();
        agent.baseOffset = 0f;
        agent.radius = 0.25f;
        agent.height = 1.4f;
        agent.speed = 1.1f;
        agent.angularSpeed = 200f;
        agent.acceleration = 8f;
        agent.stoppingDistance = 0.4f;

        var roamer = npc.AddComponent<MenuRoamingNPC>();
        roamer.roamPoints = new List<Transform>(roamPoints);
        roamer.sequential = sequential;
        roamer.walkSpeed = 1.1f;
        roamer.idleTime = new Vector2(1.5f, 4f);
        roamer.onlyDuringMenu = true;
        roamer.hideDuringGameplay = true;
        roamer.proceduralBobFallback = true;
        roamer.animator = mesh != null ? mesh.GetComponentInChildren<Animator>(true) : null; // real walk clip, not the bob

        EditorUtility.SetDirty(roamer);
        return npc;
    }

    // ── Real-baby mesh + in-place stroller upgrade ───────────────────────────
    /// <summary>
    /// Upgrades the menu strollers already in the OPEN scene: swaps each one's capsule
    /// "Mesh" placeholder for the real WalkingBaby.fbx (animated via BabyController) and
    /// wires MenuRoamingNPC.animator so the roamer plays the real walk clip. Idempotent —
    /// safe to re-run. Does NOT rebuild the camera / waypoints / showcase (unlike a full
    /// Build Menu Tour), so VR tweaks such as worldSpace are preserved. Save the scene after.
    /// </summary>
    [MenuItem("Tools/STOP IT/Upgrade Menu Strollers to Real Baby")]
    public static void UpgradeMenuStrollers()
    {
        var roamers = Object.FindObjectsByType<MenuRoamingNPC>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (roamers.Length == 0)
        {
            Debug.LogWarning("[STOP IT] No MenuRoamingNPC in the scene — run Tools/STOP IT/Build Menu Tour first.");
            return;
        }

        int upgraded = 0;
        foreach (var roamer in roamers)
        {
            var npc = roamer.gameObject;

            // Drop the previous visual child (capsule "Mesh" or an earlier "MeshHolder").
            var oldMesh = npc.transform.Find("Mesh");
            if (oldMesh != null) Undo.DestroyObjectImmediate(oldMesh.gameObject);
            var oldHolder = npc.transform.Find("MeshHolder");
            if (oldHolder != null) Undo.DestroyObjectImmediate(oldHolder.gameObject);

            var mesh = BuildBabyMesh(npc.transform);
            Undo.RegisterCreatedObjectUndo(mesh, "Upgrade Menu Stroller");

            roamer.animator = mesh.GetComponentInChildren<Animator>(true); // real walk clip, not the procedural bob
            EditorUtility.SetDirty(roamer);
            upgraded++;
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log($"[STOP IT] Upgraded {upgraded} menu stroller(s) to the real WalkingBaby + BabyController. Save the scene (Ctrl+S).");
    }

    /// <summary>
    /// Builds a stroller's visual: the real WalkingBaby.fbx instance (Animator + BabyController),
    /// sized to the 0.80 m the gameplay child uses with feet at the parent origin, parented under
    /// <paramref name="parent"/>. Falls back to a capsule placeholder only if the model is missing.
    /// </summary>
    private static GameObject BuildBabyMesh(Transform parent)
    {
        var babyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(WalkingBabyPath);
        if (babyPrefab != null)
        {
            var mesh = (GameObject)PrefabUtility.InstantiatePrefab(babyPrefab);
            mesh.name = "MeshHolder";
            mesh.transform.SetParent(parent, false);
            mesh.transform.localPosition = Vector3.zero;
            mesh.transform.localRotation = Quaternion.identity;
            FitHeightFeetAtOrigin(mesh, 0.80f);

            var anim = mesh.GetComponent<Animator>();
            if (anim == null) anim = mesh.AddComponent<Animator>();
            var ctrl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(BabyControllerPath);
            if (ctrl != null) anim.runtimeAnimatorController = ctrl;
            anim.applyRootMotion = false; // the NavMeshAgent drives position; the clip only animates the body
            return mesh;
        }

        Debug.LogWarning("[STOP IT] WalkingBaby.fbx not found at " + WalkingBabyPath +
                         " — menu stroller falls back to a capsule placeholder.");
        var caps = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        caps.name = "Mesh";
        var col = caps.GetComponent<Collider>();
        if (col != null) Object.DestroyImmediate(col);
        caps.transform.SetParent(parent, false);
        caps.transform.localPosition = new Vector3(0f, 1f, 0f);
        caps.transform.localScale = new Vector3(0.5f, 0.6f, 0.5f);
        return caps;
    }

    /// <summary>Uniformly scales <paramref name="go"/> so its renderer bounds are
    /// <paramref name="targetHeight"/> tall, then lifts it so its feet rest at the parent's
    /// origin height.</summary>
    private static void FitHeightFeetAtOrigin(GameObject go, float targetHeight)
    {
        var rends = go.GetComponentsInChildren<Renderer>();
        if (rends.Length == 0) return;
        Bounds b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
        if (b.size.y > 1e-4f) go.transform.localScale *= targetHeight / b.size.y;

        rends = go.GetComponentsInChildren<Renderer>();
        b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
        float parentY = go.transform.parent != null ? go.transform.parent.position.y : 0f;
        var lp = go.transform.localPosition;
        lp.y += parentY - b.min.y;
        go.transform.localPosition = lp;
    }
}
