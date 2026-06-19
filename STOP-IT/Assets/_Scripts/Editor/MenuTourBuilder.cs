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
                  "strollers around the house; the showcase shows the scenario of the room they're in. " +
                  "Swap the stroller capsules for real meshes, bake the NavMesh, then save the scene. " +
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

        var mesh = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        mesh.name = "Mesh";
        var meshCol = mesh.GetComponent<Collider>();
        if (meshCol != null) Object.DestroyImmediate(meshCol);
        mesh.transform.SetParent(npc.transform, false);
        mesh.transform.localPosition = new Vector3(0f, 1f, 0f);
        mesh.transform.localScale = new Vector3(0.5f, 0.6f, 0.5f);

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

        EditorUtility.SetDirty(roamer);
        return npc;
    }
}
