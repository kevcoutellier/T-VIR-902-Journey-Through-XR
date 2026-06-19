using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEditor;
using UnityEditor.SceneManagement;
using Unity.XR.CoreUtils;

/// <summary>
/// Editor tool that builds the cinematic menu into the CURRENTLY OPEN scene
/// (HousePreview / any house). It adapts to the actual house by deriving the
/// camera waypoints from the real per-scenario anchors in the scene (the
/// ScenarioManager configs, or SpawnChild_S1..S5 markers) — no hard-coded room
/// coordinates.
///
/// VR + 2D: if an XR Origin is present, the tour drives the rig with a comfort
/// fade (TeleportFade) so the player is whisked room-to-room without nauseating
/// motion; otherwise it falls back to a dedicated dolly Camera (Glide) for 2D.
/// The menu UI (MenuScenarioShowcase) is a world-space panel that floats in front
/// of the player, so it renders both in the HMD and on a 2D screen.
///
/// Re-running deletes and rebuilds "MenuTour" — idempotent. The two strollers are
/// placeholder capsules to swap for the real character meshes.
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

    // Used only if NO scenario anchors are found in the scene (legacy HouseBuilder layout).
    private static readonly Vector3[] FallbackAnchors =
    {
        new Vector3(-5f, 0f,  4.5f), new Vector3(5f, 0f, 4.5f),
        new Vector3( 5f, 0f, -4.5f), new Vector3(-5f, 0f, -4.5f),
        new Vector3( 5f, 3f, -4.5f),
    };

    private const float ViewDistance = 2.6f; // how far the camera/rig stands back from the anchor
    private const float EyeHeight    = 1.6f;  // camera height above the floor in 2D (Glide) mode

    [MenuItem("Tools/STOP IT/Build Menu Tour")]
    public static void BuildMenuTour()
    {
        var previous = GameObject.Find("MenuTour");
        if (previous != null) Undo.DestroyObjectImmediate(previous);

        var root = new GameObject("MenuTour");
        Undo.RegisterCreatedObjectUndo(root, "Build Menu Tour");

        // 1. Where is each scenario? (real anchors in this house)
        var anchors = CollectScenarioAnchors();
        Vector3 centroid = Centroid(anchors);

        var xrOrigin = Object.FindAnyObjectByType<XROrigin>();
        bool vr = xrOrigin != null;

        // 2. Waypoints (static holder), one pose per scenario.
        var wpHolder = new GameObject("Waypoints");
        wpHolder.transform.SetParent(root.transform, false);
        Undo.RegisterCreatedObjectUndo(wpHolder, "Waypoints");

        var wps = new List<Transform>();
        for (int i = 0; i < anchors.Count; i++)
        {
            ComputeStopPose(anchors[i], centroid, vr, out Vector3 pos, out Quaternion rot);
            var wp = new GameObject($"WP_S{i + 1}");
            wp.transform.SetParent(wpHolder.transform, false);
            wp.transform.SetPositionAndRotation(pos, rot);
            Undo.RegisterCreatedObjectUndo(wp, "Waypoint");
            wps.Add(wp.transform);
        }

        // 3. Comfort fader (used by TeleportFade).
        var faderGO = new GameObject("MenuFader");
        faderGO.transform.SetParent(root.transform, false);
        var fader = faderGO.AddComponent<MenuFader>();
        Undo.RegisterCreatedObjectUndo(faderGO, "Menu Fader");

        // 4. Tour driver.
        var tour = BuildTour(root.transform, wps, vr ? xrOrigin.transform : null, fader, out Camera dollyCam);

        // 5. Roam points + strollers (ground-floor anchors).
        var roamParent = BuildRoamPoints(root.transform, anchors);
        if (roamParent.Count > 0)
        {
            BuildRoamer(root.transform, "MenuStroller_A", roamParent[0].position, roamParent, sequential: false);
            BuildRoamer(root.transform, "MenuStroller_B",
                        roamParent[Mathf.Min(1, roamParent.Count - 1)].position, roamParent, sequential: true);
        }

        // 6. World-space scenario showcase UI.
        BuildShowcase(root.transform, tour, anchors.Count);

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Selection.activeGameObject = root;

        Debug.Log($"[STOP IT] Menu Tour built for the current house — {anchors.Count} room stops " +
                  $"({(vr ? "VR: rig teleport-fade" : "2D: dolly camera glide")}). " +
                  "Nudge the WP_S* transforms under MenuTour/Waypoints to frame each room, swap the " +
                  "stroller capsules, bake the NavMesh, then save the scene.");
    }

    // ── Scenario anchors ──────────────────────────────────────────────────
    /// <summary>
    /// One world-space "look at" point per scenario, in order. Prefers the
    /// ScenarioManager configs (hazard zone, else child spawn); falls back to
    /// SpawnChild_S1..S5 markers, then to legacy coordinates.
    /// </summary>
    private static List<Vector3> CollectScenarioAnchors()
    {
        var result = new List<Vector3>();

        var sm = Object.FindAnyObjectByType<ScenarioManager>();
        if (sm != null && sm.scenarios != null && sm.scenarios.Length > 0)
        {
            foreach (var cfg in sm.scenarios)
            {
                if (cfg.hazardZone != null)      result.Add(cfg.hazardZone.transform.position);
                else if (cfg.childSpawnPoint != null) result.Add(cfg.childSpawnPoint.position);
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

    /// <summary>
    /// Camera/rig pose that frames the anchor: stand back from it toward the house
    /// interior (centroid). VR = floor height, yaw only (HMD adds pitch + head height);
    /// 2D = eye height, looking slightly down at the anchor.
    /// </summary>
    private static void ComputeStopPose(Vector3 anchor, Vector3 centroid, bool vr,
                                        out Vector3 pos, out Quaternion rot)
    {
        Vector3 dir = centroid - anchor; dir.y = 0f;
        if (dir.sqrMagnitude < 0.01f) dir = Vector3.forward;
        dir.Normalize();

        pos = anchor + dir * ViewDistance;
        if (vr)
        {
            pos.y = anchor.y; // rig stands on the floor
            Vector3 flat = anchor - pos; flat.y = 0f;
            rot = flat.sqrMagnitude > 0.001f ? Quaternion.LookRotation(flat, Vector3.up) : Quaternion.identity;
        }
        else
        {
            pos.y = anchor.y + EyeHeight;
            Vector3 look = (anchor + Vector3.up * 0.8f) - pos;
            rot = look.sqrMagnitude > 0.001f ? Quaternion.LookRotation(look, Vector3.up) : Quaternion.identity;
        }
    }

    // ── Tour driver ──────────────────────────────────────────────────────
    private static MenuCameraTour BuildTour(Transform parent, List<Transform> wps, Transform rig,
                                            MenuFader fader, out Camera dollyCam)
    {
        dollyCam = null;
        GameObject host;

        if (rig != null)
        {
            // VR: a plain controller object drives the XR rig with a comfort fade.
            host = new GameObject("TourController");
            host.transform.SetParent(parent, false);
        }
        else
        {
            // 2D: a dedicated dolly camera glides through the rooms.
            host = new GameObject("MenuCamera");
            host.transform.SetParent(parent, false);
            dollyCam = host.AddComponent<Camera>();
            dollyCam.clearFlags = CameraClearFlags.Skybox;
            dollyCam.depth = 10;
            dollyCam.fieldOfView = 60f;
            dollyCam.nearClipPlane = 0.05f;
        }
        Undo.RegisterCreatedObjectUndo(host, "Tour Host");

        var tour = host.AddComponent<MenuCameraTour>();
        tour.waypoints = wps;
        tour.tourTarget = rig != null ? rig : host.transform;
        tour.comfortMode = rig != null ? MenuCameraTour.ComfortMode.TeleportFade : MenuCameraTour.ComfortMode.Glide;
        tour.fader = fader;
        tour.fadeDuration = 0.4f;
        tour.loop = true;
        tour.travelTime = 7f;
        tour.dwellTime = 4f;
        tour.autoAdvance = true;
        tour.autoResumeDelay = 8f;
        tour.onlyDuringMenu = true;
        tour.snapToFirstOnStart = true;

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

        var map = new int[stops];
        for (int i = 0; i < stops; i++) map[i] = i; // 1:1 stop → scenario
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

        // Ground-floor anchors only (so strollers stay on the main floor / NavMesh).
        var pts = new List<Transform>();
        float minY = float.MaxValue;
        foreach (var a in anchors) minY = Mathf.Min(minY, a.y);

        int idx = 0;
        foreach (var a in anchors)
        {
            if (a.y > minY + 1.5f) continue; // skip upstairs anchors
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

    private static void BuildRoamer(Transform parent, string name, Vector3 startPos,
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
    }
}
