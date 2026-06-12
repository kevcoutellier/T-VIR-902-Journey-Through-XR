using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// Editor tool that builds the cinematic menu background into the CURRENTLY OPEN
/// scene: a dolly "MenuCamera" that tours every room (via <see cref="MenuCameraTour"/>)
/// plus two characters strolling the house (via <see cref="MenuRoamingNPC"/>).
///
/// Usage (LivingRoom.unity is lock-protected — claim docs/SCENE_LOCK.md first):
///   1. Open LivingRoom.unity in Unity.
///   2. Tools ▸ STOP IT ▸ Build Menu Tour.
///   3. Inspect / tweak the generated "MenuTour" hierarchy, then save the scene.
///
/// Re-running deletes and rebuilds the previous "MenuTour" root so it's idempotent.
/// Waypoint poses use the room coordinates from <see cref="HouseBuilder"/>; the two
/// NPCs are placeholder capsules you can swap for the real toddler/dad meshes.
/// </summary>
public static class MenuTourBuilder
{
    // Room camera poses: (position, lookAt). Coordinates match HouseBuilder's layout.
    private struct Pose { public string name; public Vector3 pos; public Vector3 look; }

    // One stop per scenario room, in scenario order (Salon, Cuisine, Escalier, SdB, Chambre/fenêtre).
    private static readonly Pose[] CameraStops =
    {
        new Pose { name = "WP_Salon",    pos = new Vector3(-1.5f, 1.8f,  1.8f), look = new Vector3(-5.0f, 1.0f,  4.5f) },
        new Pose { name = "WP_Cuisine",  pos = new Vector3( 1.5f, 1.8f,  1.8f), look = new Vector3( 5.0f, 1.0f,  4.5f) },
        new Pose { name = "WP_Escalier", pos = new Vector3( 1.5f, 1.8f, -1.8f), look = new Vector3( 5.0f, 1.0f, -4.5f) },
        new Pose { name = "WP_SdB",      pos = new Vector3(-1.5f, 1.8f, -1.8f), look = new Vector3(-5.0f, 1.0f, -4.5f) },
        new Pose { name = "WP_Chambre",  pos = new Vector3( 1.5f, 4.6f, -1.8f), look = new Vector3( 5.0f, 3.8f, -4.5f) },
    };

    // Objective line shown under each scenario title in the showcase (per scenario index).
    private static readonly string[] Objectives =
    {
        "Empêche l'enfant d'enfoncer une fourchette dans la prise.",
        "Il veut glisser le chat dans le micro-ondes.",
        "Il s'élance pour dévaler l'escalier en skateboard.",
        "Il s'apprête à boire un produit ménager.",
        "Il grimpe sur le rebord pour attraper un pigeon.",
    };

    // Roam points the two strollers wander between (ground-floor room centres + corridor).
    private static readonly Vector3[] RoamPoints =
    {
        new Vector3(-3.5f, 0f,  3.5f), // Salon
        new Vector3( 3.5f, 0f,  3.5f), // Cuisine
        new Vector3(-3.5f, 0f, -3.5f), // SdB
        new Vector3( 3.5f, 0f, -3.5f), // Escalier
        new Vector3( 0.0f, 0f,  0.0f), // Couloir
    };

    [MenuItem("Tools/STOP IT/Build Menu Tour")]
    public static void BuildMenuTour()
    {
        // Idempotent: nuke any previous build.
        var previous = GameObject.Find("MenuTour");
        if (previous != null) Undo.DestroyObjectImmediate(previous);

        var root = new GameObject("MenuTour");
        Undo.RegisterCreatedObjectUndo(root, "Build Menu Tour");

        var tour = BuildCamera(root.transform);
        var roamParent = BuildRoamPoints(root.transform);
        BuildRoamer(root.transform, "MenuStroller_A", new Vector3(-3.5f, 0f, 3.5f), roamParent, sequential: false);
        BuildRoamer(root.transform, "MenuStroller_B", new Vector3( 3.5f, 0f, -3.5f), roamParent, sequential: true);
        BuildShowcase(root.transform, tour);

        // Mark the scene dirty so the user is prompted to save.
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Selection.activeGameObject = root;

        Debug.Log("[STOP IT] Menu Tour built: dolly camera + 5 scenario waypoints + 2 strollers + " +
                  "scenario showcase UI. Swap the placeholder capsules for the real character meshes, " +
                  "then save the scene. Bake the NavMesh if the strollers don't move.");
    }

    // ── Camera + waypoints ────────────────────────────────────────────────
    private static MenuCameraTour BuildCamera(Transform parent)
    {
        var camGO = new GameObject("MenuCamera");
        camGO.transform.SetParent(parent, false);
        Undo.RegisterCreatedObjectUndo(camGO, "Menu Camera");

        var cam = camGO.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.depth = 10;            // render on top of the gameplay/XR camera while active
        cam.fieldOfView = 60f;
        cam.nearClipPlane = 0.05f;

        var tour = camGO.AddComponent<MenuCameraTour>();

        // Waypoints live on a STATIC holder under MenuTour — never under the camera,
        // or they'd move with it and the spline would chase fleeing targets (jitter).
        var wpHolder = new GameObject("Waypoints");
        wpHolder.transform.SetParent(parent, false);
        Undo.RegisterCreatedObjectUndo(wpHolder, "Menu Waypoints");

        // Create one waypoint Transform per room stop, posed + oriented.
        var wps = new List<Transform>(CameraStops.Length);
        foreach (var stop in CameraStops)
        {
            var wp = new GameObject(stop.name);
            wp.transform.SetParent(wpHolder.transform, false);
            wp.transform.position = stop.pos;
            wp.transform.rotation = Quaternion.LookRotation((stop.look - stop.pos).normalized, Vector3.up);
            Undo.RegisterCreatedObjectUndo(wp, "Menu Waypoint");
            wps.Add(wp.transform);
        }

        tour.waypoints = wps;
        tour.tourTarget = camGO.transform;
        tour.loop = true;
        tour.travelTime = 7f;       // slow, gentle glide
        tour.dwellTime = 4f;        // linger in each room so the scenario reads
        tour.autoAdvance = true;
        tour.autoResumeDelay = 8f;
        tour.onlyDuringMenu = true;
        tour.snapToFirstOnStart = true;

        // Start the camera on the first stop so the Scene view preview reads right.
        camGO.transform.SetPositionAndRotation(wps[0].position, wps[0].rotation);

        EditorUtility.SetDirty(tour);
        return tour;
    }

    // ── Scenario showcase UI ──────────────────────────────────────────────
    private static void BuildShowcase(Transform parent, MenuCameraTour tour)
    {
        var go = new GameObject("MenuShowcase", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        Undo.RegisterCreatedObjectUndo(go, "Menu Showcase");

        var showcase = go.AddComponent<MenuScenarioShowcase>();
        showcase.cameraTour = tour;
        showcase.scenarioManager = Object.FindAnyObjectByType<ScenarioManager>();
        // 5 camera stops map 1:1 to scenarios 0..4 (Salon, Cuisine, Escalier, SdB, fenêtre).
        showcase.scenarioPerStop = new[] { 0, 1, 2, 3, 4 };
        showcase.objectiveOverrides = (string[])Objectives.Clone();

        EditorUtility.SetDirty(showcase);
    }

    // ── Roam points ──────────────────────────────────────────────────────
    private static List<Transform> BuildRoamPoints(Transform parent)
    {
        var holder = new GameObject("RoamPoints");
        holder.transform.SetParent(parent, false);
        Undo.RegisterCreatedObjectUndo(holder, "Roam Points");

        var pts = new List<Transform>(RoamPoints.Length);
        for (int i = 0; i < RoamPoints.Length; i++)
        {
            var p = new GameObject($"Roam_{i}");
            p.transform.SetParent(holder.transform, false);
            p.transform.position = RoamPoints[i];
            Undo.RegisterCreatedObjectUndo(p, "Roam Point");
            pts.Add(p.transform);
        }
        return pts;
    }

    // ── Strollers ────────────────────────────────────────────────────────
    private static void BuildRoamer(Transform parent, string name, Vector3 startPos,
                                    List<Transform> roamPoints, bool sequential)
    {
        // Placeholder body (a capsule) so the NPC is visible out of the box. The user
        // swaps in the real rigged mesh under "Mesh" afterwards.
        var npc = new GameObject(name);
        npc.transform.SetParent(parent, false);
        npc.transform.position = startPos;
        Undo.RegisterCreatedObjectUndo(npc, "Menu Stroller");

        var mesh = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        mesh.name = "Mesh";
        var meshCol = mesh.GetComponent<Collider>();
        if (meshCol != null) Object.DestroyImmediate(meshCol); // NavMeshAgent handles avoidance; no physics collider needed
        mesh.transform.SetParent(npc.transform, false);
        mesh.transform.localPosition = new Vector3(0f, 1f, 0f); // capsule pivot is centre; lift so feet ~ floor
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
