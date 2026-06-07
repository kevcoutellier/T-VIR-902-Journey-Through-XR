using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.Animations;
using Unity.AI.Navigation;
using Unity.XR.CoreUtils;

/// <summary>
/// STOP IT! — Story Mode setup (HousePreview).
/// One-click builder for the continuous "story mode" foundation + scenario 1 in the
/// HousePreview scene. Re-runnable / idempotent. Because the scene serialises as binary
/// (ForceBinary), all wiring is done here in C# rather than by editing the .unity file.
///
/// What it creates / wires:
///   • "StoryMode" GameObject: GameManager + ScenarioManager + StoryModeDirector + AudioSource
///   • "Child": NavMeshAgent + ChildNPC (+ WalkingBaby.fbx rigged/animated mesh) + CapsuleCollider + BabyCatchPrompt
///   • S1 props: HazardZone_Outlet, SpawnChild_S1, PickupWaypoint_S1, Fork (from Fork.glb)
///   • "ScenarioCanvas" world-space + ScenarioUI (+ TMP texts) + EventSystem
///   • "Left Controller" / "Right Controller" under Camera Offset: PlayerBlocker + ChildGrabber
///   • "NavMesh": NavMeshSurface baked from physics colliders with a toddler agent radius
///
/// NOTE: never use `GetComponent&lt;T&gt;() ?? AddComponent&lt;T&gt;()` — the C# `??` operator bypasses
/// Unity's overloaded null and returns a "fake-null" component. Use GetOrAdd&lt;T&gt;() instead.
/// </summary>
public static class StoryModeSetup
{
    private const string WalkingBabyPath = "Assets/_ThirdParty/Models/WalkingBaby.fbx";
    private const string AnimDir = "Assets/_ThirdParty/Models/Animations/";
    private const string BabyControllerPath = "Assets/_ThirdParty/Models/BabyController.controller";
    private const string ForkModelPath = "Assets/_ThirdParty/Models/Fork.glb";
    private const string CatModelPath = "Assets/_ThirdParty/Models/Cat.glb";
    private const string CatBedModelPath = "Assets/_ThirdParty/Models/Cat Bed.glb";
    private const string NavMeshAssetPath = "Assets/_Scenes/Sandboxes/HousePreview_NavMesh.asset";

    private static System.Text.StringBuilder _log;
    private static void L(string m) { if (_log != null) _log.AppendLine(m); Debug.Log(m); }

    [MenuItem("Tools/STOP IT/Setup Story (HousePreview)")]
    public static void SetupStory()
    {
        _log = new System.Text.StringBuilder();
        L("[StoryModeSetup] === BEGIN ===");
        try
        {
            SetupStoryInner();
            L("[StoryModeSetup] === END OK ===");
        }
        catch (System.Exception e)
        {
            L("[StoryModeSetup] EXCEPTION " + e.GetType().Name + ": " + e.Message);
            L(e.StackTrace);
            Debug.LogError("[StoryModeSetup] FAILED — see StoryModeSetup.log in the project root.\n" + e);
        }
        finally
        {
            try { System.IO.File.WriteAllText(System.IO.Path.Combine(Application.dataPath, "../StoryModeSetup.log"), _log.ToString()); }
            catch (System.Exception ex) { Debug.LogWarning("[StoryModeSetup] could not write log file: " + ex.Message); }
        }
    }

    private static void SetupStoryInner()
    {
        L("[StoryModeSetup] Building story foundation + scenario 1…");

        // 1. Container + managers
        var story = FindOrCreate("StoryMode");
        story.transform.SetParent(null);

        var gm = GetOrAdd<GameManager>(story);
        gm.totalScenarios = 5;
        gm.scenarioDuration = 45f; // safety cap per scenario
        var gmAudio = GetOrAdd<AudioSource>(story);
        gmAudio.playOnAwake = false;
        gm.audioSource = gmAudio;

        var sm = GetOrAdd<ScenarioManager>(story);
        var director = GetOrAdd<StoryModeDirector>(story);
        director.autoStartOnPlay = true;
        director.activeScenarioCount = 3; // S1 + S2 + S3 playable
        L("step 1 OK — managers (GameManager + ScenarioManager + StoryModeDirector + AudioSource)");

        // 2. Child NPC
        var child = SetupChild();
        L("step 2 OK — child");

        // 3. Scenario 1 props
        // Kitchen (microwave is at -9.8,-10.5). Baby spawns at the kitchen entrance, fetches the
        // fork from the kitchen floor, then walks all the way to the living-room outlet (-0.13,-6).
        var hazardOutlet = CreateHazard("HazardZone_Outlet", "Electrical Outlet",
                                        new Vector3(-0.13f, 0.40f, -6.00f), 1.5f, story.transform);
        var spawnPosS1  = new Vector3(-8.00f, 0f, -6.80f);
        var pickupPosS1 = new Vector3(-9.80f, 0f, -9.60f);
        Vector3 approachDir = pickupPosS1 - spawnPosS1; approachDir.y = 0f; approachDir.Normalize();
        var spawnS1  = CreateMarker("SpawnChild_S1",     spawnPosS1, story.transform);
        var pickupS1 = CreateMarker("PickupWaypoint_S1", pickupPosS1, story.transform);
        // Fork sits AHEAD of the waypoint (the child stops ~0.3 m short of it) so it reaches forward
        // for it instead of crouching directly on top of it.
        var fork     = SetupFork(pickupPosS1 + approachDir * 0.30f + Vector3.up * 0.06f, story.transform);

        // Player start spawn = the XR Origin's authored pose, re-applied each (re)activation so a
        // fail teleports the player back to the entrance.
        var xrRig = Object.FindAnyObjectByType<XROrigin>();
        var spawnPlayer = CreateMarker("SpawnPlayer_S1",
            xrRig != null ? xrRig.transform.position : new Vector3(-6.5f, 0f, -2f), story.transform);
        spawnPlayer.transform.rotation = xrRig != null ? xrRig.transform.rotation : Quaternion.Euler(0f, 180f, 0f);
        L("step 3 OK — props (hazard, spawn, pickup waypoint, fork, player spawn)");

        // 3b. Scenario 2 props — cat in its bed (salon) → microwave (kitchen).
        var hazardMicro = CreateHazard("HazardZone_Microwave", "Microwave",
                                       new Vector3(-9.80f, 1.00f, -10.50f), 1.6f, story.transform);
        hazardMicro.microwaveMode = true;
        hazardMicro.sparkColor = new Color(1f, 0.15f, 0.10f); // red — the cat exploding
        hazardMicro.microwaveRunSeconds = 2f;
        var spawnPosS2  = new Vector3(-1.00f, 0f, -6.50f);
        var catBedPos   = new Vector3(-4.00f, 0f, -10.20f); // SW corner of the salon (snap pulls it to the inner corner)
        var spawnS2  = CreateMarker("SpawnChild_S2",     spawnPosS2, story.transform);
        var pickupS2 = CreateMarker("PickupWaypoint_S2", catBedPos, story.transform); // repositioned in front of the bed after snap
        var catBed   = SetupCatBed(catBedPos, story.transform);
        var cat      = SetupCat(catBedPos + Vector3.up * 0.08f, hazardMicro, story.transform);
        var catCg = cat.GetComponent<CatGrab>(); if (catCg != null) catCg.basket = catBed.transform; // drop-the-cat target (S2→S3)
        L("step 3b OK — scenario 2 props (microwave hazard, cat bed, cat, spawn, pickup)");

        // 3c. Scenario 3 props — bathroom. REUSE the house's existing cleaning products (foot of the sink)
        // + existing water bottle (wall niche). Remove any placeholders created by earlier runs.
        DumpBathroom();
        var oldCP = GameObject.Find("CleaningProducts"); if (oldCP != null) Object.DestroyImmediate(oldCP);
        var oldWBph = GameObject.Find("WaterBottle");    if (oldWBph != null) Object.DestroyImmediate(oldWBph);

        // Existing cleaning products: 3 bottles + caps at the foot of Prop_Sink_02.
        var productObjs = new List<GameObject>();
        Vector3 prodSum = Vector3.zero; int prodN = 0;
        foreach (var pn in new[] { "B_product1_body", "B_product1_cap", "B_product2_body", "B_product2_cap", "B_product3_body", "B_product3_cap" })
        {
            var g = GameObject.Find(pn);
            if (g != null) { productObjs.Add(g); if (pn.EndsWith("_body")) { prodSum += g.transform.position; prodN++; } }
        }
        Vector3 productsCentroid = prodN > 0 ? prodSum / prodN : new Vector3(-1.12f, 0.13f, -4.05f);
        var hazardClean = CreateHazard("HazardZone_CleaningProduct", "Cleaning Product",
                                       new Vector3(productsCentroid.x, productsCentroid.y + 0.05f, productsCentroid.z), 1.4f, story.transform);
        hazardClean.silentApproach = true; // poison: no electrical sparks/hum on approach

        // Existing water bottle (wall niche): add the swap component; pull its cap under it so it follows.
        var waterGO = GameObject.Find("B_waterbottle_body");
        WaterBottle water = null;
        if (waterGO != null)
        {
            water = GetOrAdd<WaterBottle>(waterGO);
            water.targetHazardName = hazardClean.gameObject.name;
            water.targetHazardZone = hazardClean;
            water.cleaningProductVisual = null;
            water.cleaningProductVisuals = productObjs.ToArray();
            water.interactionRadius = 2.0f;
            water.promptOffset = new Vector3(0f, 0.4f, 0f); // FacePrompt also pulls it toward the camera (out of the niche)
            var wcap = GameObject.Find("B_waterbottle_cap");
            water.extraParts = wcap != null ? new[] { wcap.transform } : new Transform[0]; // the cap moves/hides with the bottle
            EditorUtility.SetDirty(water);
            L($"[StoryModeSetup] water bottle wired (cap {(wcap != null ? "found" : "MISSING")}).");
        }
        else L("[StoryModeSetup] WARNING: B_waterbottle_body not found — S3 has no water bottle to swap.");

        var spawnS3 = CreateMarker("SpawnChild_S3", new Vector3(-3.20f, 0f, -6.00f), story.transform);
        L($"step 3c OK — S3 wired to {productObjs.Count} existing product objects (centroid {productsCentroid}) + existing water bottle.");

        // 4. UI + EventSystem + lose screen
        var ui = SetupCanvasUI(story.transform);
        SetupLoseScreen(story.transform, director);
        L("step 4 OK — canvas/UI + EventSystem + lose screen");

        // 5. VR hands (Left/Right Controller) — also used by the 4-trigger grab in VR
        SetupControllers();
        L("step 5 OK — controllers");

        // 6. Wire ScenarioManager
        sm.childNPC = child;
        sm.scenarioUI = ui;
        var cfg = new ScenarioManager.ScenarioConfig
        {
            scenarioName     = "Salon — La prise électrique",
            actionHint       = "ATTRAPE LE BÉBÉ !",
            childSpawnPoint  = spawnS1.transform,
            hazardZone       = hazardOutlet,
            playerSpawnPoint = spawnPlayer.transform, // teleport the player back to the entrance on (re)start
            scenarioObjects  = new GameObject[0],
            pickupWaypoint   = pickupS1.transform,
            pickupItem       = fork,
            // Hand-relative (item parents to the right-hand bone). Reversed 180° so the tines (not the
            // handle) point outward; tuned offset to sit perfectly in the palm.
            pickupItemLocalPosition = new Vector3(-0.012f, 0.04f, 0.02f),
            pickupItemLocalEuler    = new Vector3(0f, 180f, 0f),
            disableDirectChildSave  = false,         // grab IS the win for S1
            loseMessage = "Une prise électrique non protégée peut électrocuter un enfant en quelques secondes.\n" +
                          "Couvre les prises et ne quitte jamais ton enfant des yeux.",
        };
        var cfg2 = new ScenarioManager.ScenarioConfig
        {
            scenarioName     = "Cuisine — Le chat dans le micro-ondes",
            actionHint       = "RÉCUPÈRE LE CHAT !",
            childSpawnPoint  = null, // start from where the player dropped the baby (no teleport)
            hazardZone       = hazardMicro,
            playerSpawnPoint = null,                 // no teleport when advancing from S1 (you just dropped the baby)
            scenarioObjects  = new GameObject[0],
            pickupWaypoint   = pickupS2.transform,
            pickupItem       = cat,
            pickupItemLocalPosition = new Vector3(0f, 0.06f, 0.10f), // cat held in the arm (tune)
            pickupItemLocalEuler    = Vector3.zero,
            disableDirectChildSave  = true,          // must take the CAT, not grab the baby
            loseMessage = "Un enfant peut glisser n'importe quoi dans un micro-ondes — même le chat.\n" +
                          "Ne le laisse jamais sans surveillance et garde les appareils hors de portée.",
            failScreenDelay = 0.1f, // the 2 s microwave run already played; explode right as TROP TARD appears
        };
        var cfg3 = new ScenarioManager.ScenarioConfig
        {
            scenarioName     = "Salle de bain — Les produits ménagers",
            actionHint       = "REMPLACE LE PRODUIT PAR L'EAU !",
            childSpawnPoint  = spawnS3.transform, // known reliable start (the S2 cat-take doesn't drop the baby anywhere specific)
            hazardZone       = hazardClean,
            playerSpawnPoint = null,
            scenarioObjects  = new GameObject[0],
            waterBottle      = water,                // ScenarioManager resets it on (re)activation
            disableDirectChildSave = true,           // must swap the bottle, not grab the baby
            loseMessage = "Les produits ménagers sont toxiques : un enfant peut en boire en un instant.\n" +
                          "Range-les en hauteur, fermés et hors de portée des enfants.",
            failScreenDelay = 0.3f,                  // the drink + death already played in full
        };
        sm.scenarios = new[] { cfg, cfg2, cfg3 };
        child.targetHazard = hazardOutlet;
        L("step 6 OK — ScenarioManager wired (3 scenarios)");

        // 7. Bake NavMesh (toddler radius) excluding dynamic objects
        var ignores = new List<Transform> {
            fork.transform, hazardOutlet.transform, spawnS1.transform, pickupS1.transform,
            hazardMicro.transform, cat.transform, catBed.transform, spawnS2.transform, pickupS2.transform,
            hazardClean.transform, spawnS3.transform
        };
        if (waterGO != null) ignores.Add(waterGO.transform);
        foreach (var g in productObjs) ignores.Add(g.transform);
        BakeStoryNavMesh(child, ignores.ToArray());
        L("step 7 OK — NavMesh baked");

        // 8. Snap markers/props onto the freshly-baked NavMesh
        SnapToNavMesh(spawnS1.transform, 0f);
        SnapToNavMesh(pickupS1.transform, 0f);
        SnapToNavMesh(fork.transform, 0.06f);
        SnapToNavMesh(spawnS2.transform, 0f);
        SnapToNavMesh(catBed.transform, 0f);
        // Seat the cat on the (snapped) bed, and put the pickup waypoint in front of it (toward the
        // room) so the child stops out of the corner instead of in the wall.
        Vector3 bedP = catBed.transform.position;
        cat.transform.position = new Vector3(bedP.x, cat.transform.position.y, bedP.z);
        RestOnSurface(cat, bedP.y + 0.05f);
        // Waypoint just IN FRONT of the cat (toward the room) so the child stops OUT of the basket,
        // facing the cat — the pick-up animation then reads as grabbing the cat from its bed.
        Vector3 towardRoom = new Vector3(-2.0f, bedP.y, -8.0f) - bedP; towardRoom.y = 0f;
        towardRoom = towardRoom.sqrMagnitude > 0.001f ? towardRoom.normalized : Vector3.forward;
        pickupS2.transform.position = bedP + towardRoom * 0.20f;
        SnapToNavMesh(pickupS2.transform, 0f);
        // S3: don't move the house's existing products/bottle — just snap the spawn + the invisible
        // hazard's floor height onto the products' spot.
        SnapToNavMesh(spawnS3.transform, 0f);
        // Do NOT SnapHazardFloorHeight the poison hazard: its 6 m NavMesh sample grabbed the UPSTAIRS
        // floor (the sink foot is a NavMesh hole), pushing the hazard to y≈3.4 and sending the baby
        // upstairs. The products are already at floor height, so the centroid-derived Y is correct.
        // Hazards stay exactly on the prop; the child paths to the nearest floor point.
        VerifyPath("S1 spawn -> fork", spawnS1.transform.position, fork.transform.position);
        VerifyPath("S1 fork -> outlet", fork.transform.position, hazardOutlet.transform.position);
        VerifyPath("S2 spawn -> cat", spawnS2.transform.position, cat.transform.position);
        VerifyPath("S2 cat -> microwave", cat.transform.position, hazardMicro.transform.position);
        VerifyPath("S3 spawn -> products", spawnS3.transform.position, hazardClean.transform.position);
        if (waterGO != null) VerifyPath("S3 water -> products", waterGO.transform.position, hazardClean.transform.position);

        EditorUtility.SetDirty(gm);
        EditorUtility.SetDirty(sm);
        EditorUtility.SetDirty(director);
        EditorUtility.SetDirty(child);
        UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
        if (!Application.isPlaying) UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();

        L("[StoryModeSetup] DONE. Enter Play (desktop, forceDesktopMode=true): the baby auto-walks " +
          "spawn → fork → outlet. Hold E near the baby to catch it. Let it reach the outlet to test fail→restart.");
    }

    // ──────────────────────────────────────────────────────────────────────
    private static ChildNPC SetupChild()
    {
        var go = FindOrCreate("Child");
        go.transform.SetParent(null);
        try { go.tag = "Child"; } catch { Debug.LogWarning("[StoryModeSetup] 'Child' tag not defined — add it in Tags & Layers."); }
        go.transform.position = new Vector3(-8.00f, 0f, -6.80f);
        go.transform.rotation = Quaternion.identity;

        var agent = GetOrAdd<NavMeshAgent>(go);
        agent.agentTypeID    = 0;
        agent.baseOffset     = 0f;
        agent.radius         = 0.28f;
        agent.height         = 1.0f;
        agent.speed          = 1.4f;
        agent.angularSpeed   = 360f;
        agent.acceleration   = 12f;
        agent.stoppingDistance = 0.30f;
        agent.autoBraking    = true;

        var col = GetOrAdd<CapsuleCollider>(go);
        col.center = new Vector3(0f, 0.40f, 0f);
        col.radius = 0.22f;
        col.height = 0.80f;
        col.isTrigger = false;

        var audio = GetOrAdd<AudioSource>(go);
        audio.playOnAwake = false;

        var npc = GetOrAdd<ChildNPC>(go);
        npc.walkSpeed = 1.4f;
        npc.startDelay = 1.2f;
        npc.audioSource = audio;

        // Visual mesh: WalkingBaby.fbx (rigged, textured, animated) as "MeshHolder".
        var oldHolder = go.transform.Find("MeshHolder");
        if (oldHolder != null) Object.DestroyImmediate(oldHolder.gameObject);

        EnsureBabyImported();
        var babyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(WalkingBabyPath);
        if (babyPrefab != null)
        {
            var mesh = (GameObject)PrefabUtility.InstantiatePrefab(babyPrefab);
            mesh.name = "MeshHolder";
            mesh.transform.SetParent(go.transform, false);
            mesh.transform.localPosition = Vector3.zero;
            mesh.transform.localRotation = Quaternion.identity;
            FitHeightFeetAtOrigin(mesh, 0.80f);

            var anim = GetOrAdd<Animator>(mesh);
            anim.runtimeAnimatorController = BuildBabyController();
            anim.applyRootMotion = false; // NavMeshAgent drives position, not the clip
            L("[StoryModeSetup] WalkingBaby instantiated as MeshHolder (animator wired).");
        }
        else L("[StoryModeSetup] WARNING: WalkingBaby.fbx not found at " + WalkingBabyPath + " — child has no mesh.");

        // A real rig now drives the legs → silence the procedural waddle so they don't fight.
        var so = new SerializedObject(npc);
        foreach (var pn in new[] { "stepBobHeight", "waddleSideAmount", "waddleRollDeg", "bobAmplitude", "wobbleAngle", "forwardLeanDeg" })
        {
            var pr = so.FindProperty(pn);
            if (pr != null) pr.floatValue = 0f;
        }
        // Match the pickup / put-fork pauses to the real clip lengths so each animation plays fully.
        var pickClip = FindClip(AnimDir + "PickingUpCatBaby.fbx");
        var putClip  = FindClip(AnimDir + "PutForkInBaby.fbx");
        var putCatClip = FindClip(AnimDir + "PuttingUpTheCatBaby.fbx");
        if (pickClip != null) { var p = so.FindProperty("pickupAnimDuration");  if (p != null) p.floatValue = pickClip.length * 0.6f; }  // ~3/5 then resume walking
        if (pickClip != null) { var p = so.FindProperty("pickupAttachDelay");   if (p != null) p.floatValue = pickClip.length * 0.35f; } // fork/cat transfers to hand ~35% in (the grab)
        if (putClip  != null) { var p = so.FindProperty("putForkAnimDuration"); if (p != null) p.floatValue = putClip.length * 0.25f; } // ~1/4 then the zap
        if (putCatClip != null) { var p = so.FindProperty("putCatAnimDuration"); if (p != null) p.floatValue = putCatClip.length * 0.7f; } // most of the put-cat anim, then it runs
        var drinkClip = FindClip(AnimDir + "SittingDrinkingBaby.fbx");
        var dieClip   = FindClip(AnimDir + "DyingBackwardsBaby.fbx");
        if (drinkClip != null) { var p = so.FindProperty("drinkAnimDuration"); if (p != null) p.floatValue = drinkClip.length; }        // full sit + drink (= the swap window)
        if (dieClip   != null) { var p = so.FindProperty("dieAnimDuration");   if (p != null) p.floatValue = dieClip.length * 0.9f; }   // most of the death, then the loss
        so.ApplyModifiedProperties();

        if (go.GetComponent<BabyCatchPrompt>() == null) go.AddComponent<BabyCatchPrompt>();
        return npc;
    }

    private static void EnsureBabyImported()
    {
        var importer = AssetImporter.GetAtPath(WalkingBabyPath) as ModelImporter;
        if (importer == null) { L("[StoryModeSetup] WARNING: no ModelImporter at " + WalkingBabyPath + " (not imported yet?)."); return; }
        bool changed = false;
        if (importer.animationType != ModelImporterAnimationType.Human) { importer.animationType = ModelImporterAnimationType.Human; changed = true; }
        if (importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel) { importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel; changed = true; }
        if (!importer.importAnimation) { importer.importAnimation = true; changed = true; }
        if (importer.materialImportMode == ModelImporterMaterialImportMode.None) { importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard; changed = true; }
        if (changed) { importer.SaveAndReimport(); L("[StoryModeSetup] WalkingBaby.fbx reimported (Humanoid rig + animation + materials)."); }
    }

    private static RuntimeAnimatorController BuildBabyController()
    {
        var idle  = ImportAndGetClip(AnimDir + "IdleBaby.fbx", true);
        var walk  = ImportAndGetClip(AnimDir + "WalkingBaby.fbx", true);
        var pick  = ImportAndGetClip(AnimDir + "PickingUpCatBaby.fbx", false);
        var put   = ImportAndGetClip(AnimDir + "PutForkInBaby.fbx", false);
        var putcat = ImportAndGetClip(AnimDir + "PuttingUpTheCatBaby.fbx", false);
        var drink = ImportAndGetClip(AnimDir + "SittingDrinkingBaby.fbx", false);
        var die   = ImportAndGetClip(AnimDir + "DyingBackwardsBaby.fbx", false);
        var elec  = ImportAndGetClip(AnimDir + "BeingElectrocutedBaby.fbx", false);
        var surp  = ImportAndGetClip(AnimDir + "SurprisedBaby.fbx", false);
        var carry = ImportAndGetClip(AnimDir + "BeingCarriedBaby.fbx", true);

        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(BabyControllerPath) != null)
            AssetDatabase.DeleteAsset(BabyControllerPath); // rebuild clean each run
        var ctrl = AnimatorController.CreateAnimatorControllerAtPath(BabyControllerPath);

        ctrl.AddParameter("Speed", AnimatorControllerParameterType.Float);
        ctrl.AddParameter("PickUp", AnimatorControllerParameterType.Trigger);
        ctrl.AddParameter("PutFork", AnimatorControllerParameterType.Trigger);
        ctrl.AddParameter("PutCat", AnimatorControllerParameterType.Trigger);
        ctrl.AddParameter("Drink", AnimatorControllerParameterType.Trigger);
        ctrl.AddParameter("Die", AnimatorControllerParameterType.Trigger);
        ctrl.AddParameter("Electrocute", AnimatorControllerParameterType.Trigger);
        ctrl.AddParameter("Surprised", AnimatorControllerParameterType.Trigger);
        ctrl.AddParameter("Carried", AnimatorControllerParameterType.Bool);

        var sm = ctrl.layers[0].stateMachine;
        var sIdle  = sm.AddState("Idle");        sIdle.motion  = idle;
        var sWalk  = sm.AddState("Walk");        sWalk.motion  = walk;
        var sPick  = sm.AddState("PickUp");      sPick.motion  = pick;
        var sPut   = sm.AddState("PutFork");     sPut.motion   = put;
        var sPutCat= sm.AddState("PutCat");      sPutCat.motion= putcat;
        var sDrink = sm.AddState("Drink");       sDrink.motion = drink;
        var sDie   = sm.AddState("Die");         sDie.motion   = die;
        var sElec  = sm.AddState("Electrocute"); sElec.motion  = elec;
        var sSurp  = sm.AddState("Surprised");   sSurp.motion  = surp;
        var sCarry = sm.AddState("Carried");     sCarry.motion = carry;
        sm.defaultState = sIdle;

        AnimatorStateTransition tr;
        // Idle <-> Walk on Speed.
        tr = sIdle.AddTransition(sWalk); tr.hasExitTime = false; tr.duration = 0.10f; tr.AddCondition(AnimatorConditionMode.Greater, 0.10f, "Speed");
        tr = sWalk.AddTransition(sIdle); tr.hasExitTime = false; tr.duration = 0.15f; tr.AddCondition(AnimatorConditionMode.Less, 0.10f, "Speed");
        // PickUp (one-shot) -> back to Idle.
        tr = sm.AddAnyStateTransition(sPick); tr.hasExitTime = false; tr.duration = 0.05f; tr.canTransitionToSelf = false; tr.AddCondition(AnimatorConditionMode.If, 0, "PickUp");
        tr = sPick.AddTransition(sWalk); tr.hasExitTime = false; tr.duration = 0.10f; tr.AddCondition(AnimatorConditionMode.Greater, 0.1f, "Speed"); // resume walking the instant the pause ends
        tr = sPick.AddTransition(sIdle); tr.hasExitTime = true; tr.exitTime = 0.85f; tr.duration = 0.10f;                                              // fallback (e.g. caught mid-pickup)
        // PutFork (one-shot) -> back to Idle (Electrocute usually interrupts it).
        tr = sm.AddAnyStateTransition(sPut); tr.hasExitTime = false; tr.duration = 0.05f; tr.canTransitionToSelf = false; tr.AddCondition(AnimatorConditionMode.If, 0, "PutFork");
        tr = sPut.AddTransition(sIdle); tr.hasExitTime = true; tr.exitTime = 0.95f; tr.duration = 0.10f;
        // PutCat (one-shot) -> Idle.
        tr = sm.AddAnyStateTransition(sPutCat); tr.hasExitTime = false; tr.duration = 0.05f; tr.canTransitionToSelf = false; tr.AddCondition(AnimatorConditionMode.If, 0, "PutCat");
        tr = sPutCat.AddTransition(sIdle); tr.hasExitTime = true; tr.exitTime = 0.95f; tr.duration = 0.15f;
        // Drink (one-shot: sit + drink) -> Idle (Die interrupts it on poison).
        tr = sm.AddAnyStateTransition(sDrink); tr.hasExitTime = false; tr.duration = 0.10f; tr.canTransitionToSelf = false; tr.AddCondition(AnimatorConditionMode.If, 0, "Drink");
        tr = sDrink.AddTransition(sIdle); tr.hasExitTime = true; tr.exitTime = 0.97f; tr.duration = 0.15f;
        // Die (one-shot) -> holds the collapsed pose (no exit; a restart snaps back to Idle).
        tr = sm.AddAnyStateTransition(sDie); tr.hasExitTime = false; tr.duration = 0.08f; tr.canTransitionToSelf = false; tr.AddCondition(AnimatorConditionMode.If, 0, "Die");
        // Electrocute (one-shot) -> Idle.
        tr = sm.AddAnyStateTransition(sElec); tr.hasExitTime = false; tr.duration = 0.05f; tr.canTransitionToSelf = false; tr.AddCondition(AnimatorConditionMode.If, 0, "Electrocute");
        tr = sElec.AddTransition(sIdle); tr.hasExitTime = true; tr.exitTime = 0.95f; tr.duration = 0.20f;
        // Surprised (one-shot) -> Carried (if held) else Idle.
        tr = sm.AddAnyStateTransition(sSurp); tr.hasExitTime = false; tr.duration = 0.05f; tr.canTransitionToSelf = false; tr.AddCondition(AnimatorConditionMode.If, 0, "Surprised");
        tr = sSurp.AddTransition(sCarry); tr.hasExitTime = true; tr.exitTime = 0.45f; tr.duration = 0.10f; tr.AddCondition(AnimatorConditionMode.If, 0, "Carried");
        tr = sSurp.AddTransition(sIdle);  tr.hasExitTime = true; tr.exitTime = 0.60f; tr.duration = 0.15f;
        // Carried loop -> Idle when released.
        tr = sCarry.AddTransition(sIdle); tr.hasExitTime = false; tr.duration = 0.15f; tr.AddCondition(AnimatorConditionMode.IfNot, 0, "Carried");

        EditorUtility.SetDirty(ctrl);
        AssetDatabase.SaveAssets();
        L("[StoryModeSetup] BabyController built (Idle/Walk/PickUp/PutFork/PutCat/Drink/Die/Electrocute/Surprised/Carried).");
        return ctrl;
    }

    /// <summary>Import an animation FBX (Generic, looped or one-shot) and return its first AnimationClip.</summary>
    private static AnimationClip ImportAndGetClip(string path, bool loop)
    {
        var imp = AssetImporter.GetAtPath(path) as ModelImporter;
        if (imp != null)
        {
            var existing = imp.clipAnimations;
            bool ok = imp.animationType == ModelImporterAnimationType.Human && imp.importAnimation
                      && imp.avatarSetup == ModelImporterAvatarSetup.CreateFromThisModel
                      && existing != null && existing.Length > 0 && existing[0].loopTime == loop
                      && existing[0].lockRootHeightY;
            if (!ok)
            {
                // Humanoid: with applyRootMotion=false the root motion is ignored → clips play in
                // place (NavMeshAgent drives the body). Bake XZ + Y (Based Upon = Feet) so crouch/
                // reach clips (pickup, put-fork) keep the feet planted instead of floating.
                imp.animationType = ModelImporterAnimationType.Human;
                imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                imp.importAnimation = true;
                var defs = (imp.clipAnimations != null && imp.clipAnimations.Length > 0) ? imp.clipAnimations : imp.defaultClipAnimations;
                for (int i = 0; i < defs.Length; i++)
                {
                    defs[i].loopTime = loop;
                    defs[i].lockRootPositionXZ = true;
                    defs[i].keepOriginalPositionXZ = true;
                    defs[i].lockRootHeightY = true;   // bake Y into the pose
                    defs[i].heightFromFeet = true;    // Based Upon = Feet → no floating during crouch/reach
                }
                imp.clipAnimations = defs;
                imp.SaveAndReimport();
            }
        }
        else L("[StoryModeSetup] WARNING: no importer at " + path);

        foreach (var a in AssetDatabase.LoadAllAssetRepresentationsAtPath(path))
            if (a is AnimationClip c && !c.name.StartsWith("__preview")) return c;
        L("[StoryModeSetup] WARNING: no AnimationClip in " + path);
        return null;
    }

    private static HazardZone CreateHazard(string name, string hazardName, Vector3 pos, float warnRadius, Transform parent)
    {
        var go = GameObject.Find(name);
        if (go == null) { go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name; }
        go.transform.SetParent(parent, true);
        go.transform.position = pos;
        go.transform.localScale = new Vector3(0.25f, 0.50f, 0.25f);

        // Invisible: strip the renderer/filter, keep a trigger collider + the auto-created sparks/hum VFX.
        var mr = go.GetComponent<MeshRenderer>(); if (mr != null) Object.DestroyImmediate(mr);
        var mf = go.GetComponent<MeshFilter>();   if (mf != null) Object.DestroyImmediate(mf);

        var col = GetOrAdd<BoxCollider>(go);
        col.isTrigger = true;

        var hz = GetOrAdd<HazardZone>(go);
        hz.hazardName = hazardName;
        hz.warningRadius = warnRadius;
        hz.hazardRenderer = null; // no visible cube; danger feedback comes from sparks + hum
        return hz;
    }

    private static GameObject CreateMarker(string name, Vector3 pos, Transform parent)
    {
        var go = FindOrCreate(name);
        go.transform.SetParent(parent, true);
        go.transform.position = pos;
        go.transform.rotation = Quaternion.identity;
        return go;
    }

    private static GameObject SetupFork(Vector3 pos, Transform parent)
    {
        var fork = GameObject.Find("Fork");
        if (fork == null)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ForkModelPath);
            if (prefab != null) { fork = (GameObject)PrefabUtility.InstantiatePrefab(prefab); fork.name = "Fork"; }
            else
            {
                L("[StoryModeSetup] WARNING: Fork.glb not found — using a placeholder cube.");
                fork = GameObject.CreatePrimitive(PrimitiveType.Cube);
                fork.name = "Fork";
                fork.transform.localScale = new Vector3(0.02f, 0.02f, 0.18f);
            }
        }
        fork.transform.SetParent(parent, true);
        fork.transform.position = pos;
        fork.transform.rotation = Quaternion.identity; // lie flat on the floor (tweak if the model needs it)
        FitMaxDimension(fork, 0.18f);
        foreach (var c in fork.GetComponentsInChildren<Collider>(true)) c.enabled = false; // carriable
        return fork;
    }

    private static GameObject SetupCat(Vector3 pos, HazardZone hazard, Transform parent)
    {
        var cat = GameObject.Find("Cat");
        if (cat == null)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CatModelPath);
            if (prefab != null) { cat = (GameObject)PrefabUtility.InstantiatePrefab(prefab); cat.name = "Cat"; }
            else
            {
                L("[StoryModeSetup] WARNING: Cat.glb not found — using a placeholder cube.");
                cat = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cat.name = "Cat";
                cat.transform.localScale = new Vector3(0.2f, 0.2f, 0.35f);
            }
        }
        cat.transform.SetParent(parent, true);
        cat.transform.position = pos;
        cat.transform.rotation = Quaternion.identity;
        FitMaxDimension(cat, 0.32f);
        foreach (var c in cat.GetComponentsInChildren<Collider>(true)) c.enabled = false; // carriable
        var cg = GetOrAdd<CatGrab>(cat);
        cg.targetHazard = hazard;
        cg.interactionRadius = 1.6f;
        return cat;
    }

    private static GameObject SetupCatBed(Vector3 pos, Transform parent)
    {
        var bed = GameObject.Find("Cat Bed");
        if (bed == null)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CatBedModelPath);
            if (prefab != null) { bed = (GameObject)PrefabUtility.InstantiatePrefab(prefab); bed.name = "Cat Bed"; }
            else
            {
                L("[StoryModeSetup] WARNING: 'Cat Bed.glb' not found — using a placeholder.");
                bed = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                bed.name = "Cat Bed";
                bed.transform.localScale = new Vector3(0.5f, 0.06f, 0.5f);
            }
        }
        bed.transform.SetParent(parent, true);
        bed.transform.position = pos;
        bed.transform.rotation = Quaternion.identity;
        FitMaxDimension(bed, 0.55f);
        foreach (var c in bed.GetComponentsInChildren<Collider>(true)) c.enabled = false; // decorative; don't block navmesh
        return bed;
    }

    private static ScenarioUI SetupCanvasUI(Transform parent)
    {
        var canvasGO = FindOrCreate("ScenarioCanvas");
        canvasGO.transform.SetParent(parent, true);

        var canvas = GetOrAdd<Canvas>(canvasGO);
        canvas.renderMode = RenderMode.ScreenSpaceOverlay; // always visible in the Game view (desktop)
        canvas.sortingOrder = 100;
        var scaler = GetOrAdd<CanvasScaler>(canvasGO);
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        GetOrAdd<GraphicRaycaster>(canvasGO);

        var ui = GetOrAdd<ScenarioUI>(canvasGO);
        ui.hintHoldDuration = 9999f; // keep the instruction on screen for the whole scenario
        ui.scenarioNameText = MakeTMP(canvasGO, "ScenarioNameText", new Vector2(0.5f, 1f),  new Vector2(0, -150),  new Vector2(1000, 60),  30, TextAlignmentOptions.Top,      new Color(1f, 0.85f, 0.2f));
        ui.actionHintText   = MakeTMP(canvasGO, "ActionHintText",   new Vector2(0.5f, 1f),  new Vector2(0, -55),   new Vector2(1300, 90),  52, TextAlignmentOptions.Top,      new Color(0.2f, 0.95f, 1f));
        ui.actionHintText.fontStyle = FontStyles.Bold;
        ui.timerText        = MakeTMP(canvasGO, "TimerText",        new Vector2(1f, 1f),    new Vector2(-40, -30), new Vector2(220, 110),  72, TextAlignmentOptions.TopRight, Color.white);
        ui.timerText.fontStyle = FontStyles.Bold;
        ui.scoreText        = MakeTMP(canvasGO, "ScoreText",        new Vector2(0f, 1f),    new Vector2(40, -30),  new Vector2(300, 60),   30, TextAlignmentOptions.TopLeft,  Color.white);
        ui.feedbackText     = MakeTMP(canvasGO, "FeedbackText",     new Vector2(0.5f, 0.5f), new Vector2(0, 0),    new Vector2(1300, 160), 80, TextAlignmentOptions.Center,   Color.white);
        ui.feedbackText.fontStyle = FontStyles.Bold;

        if (Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<UnityEngine.EventSystems.EventSystem>();
            es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        }
        return ui;
    }

    private static void SetupLoseScreen(Transform parent, StoryModeDirector director)
    {
        var go = FindOrCreate("StoryLoseScreen");
        go.transform.SetParent(parent, true);

        var canvas = GetOrAdd<Canvas>(go);
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200; // above the HUD
        var scaler = GetOrAdd<CanvasScaler>(go);
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        GetOrAdd<GraphicRaycaster>(go);
        var grp = GetOrAdd<CanvasGroup>(go);

        var bg = FindOrCreateChild(go, "BG");
        var bgImg = GetOrAdd<Image>(bg);
        bgImg.color = new Color(0.05f, 0f, 0f, 0.92f);
        var bgrt = (RectTransform)bg.transform;
        bgrt.anchorMin = Vector2.zero; bgrt.anchorMax = Vector2.one; bgrt.offsetMin = Vector2.zero; bgrt.offsetMax = Vector2.zero;

        var title  = MakeTMP(go, "LoseTitle",   new Vector2(0.5f, 0.5f), new Vector2(0, 190),  new Vector2(1500, 150), 96, TextAlignmentOptions.Center, new Color(1f, 0.3f, 0.25f));
        title.fontStyle = FontStyles.Bold;
        var msg    = MakeTMP(go, "LoseMessage", new Vector2(0.5f, 0.5f), new Vector2(0, -20),  new Vector2(1200, 320), 40, TextAlignmentOptions.Center, Color.white);
        var prompt = MakeTMP(go, "LosePrompt",  new Vector2(0.5f, 0f),   new Vector2(0, 130),  new Vector2(1200, 80),  36, TextAlignmentOptions.Center, new Color(0.2f, 0.95f, 1f));

        var lose = GetOrAdd<StoryLoseScreen>(go);
        var so = new SerializedObject(lose);
        SetRef(so, "group", grp);
        SetRef(so, "titleText", title);
        SetRef(so, "messageText", msg);
        SetRef(so, "promptText", prompt);
        so.ApplyModifiedProperties();

        director.loseScreen = lose;
        grp.alpha = 0f;
        grp.blocksRaycasts = false;
        // GameObject stays active (alpha 0) so StoryLoseScreen's Update/coroutines keep running.
    }

    private static void SetRef(SerializedObject so, string prop, Object value)
    {
        var p = so.FindProperty(prop);
        if (p != null) p.objectReferenceValue = value;
    }

    private static GameObject FindOrCreateChild(GameObject parent, string name)
    {
        var t = parent.transform.Find(name);
        if (t != null) return t.gameObject;
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        return go;
    }

    private static void SetupControllers()
    {
        var xr = Object.FindAnyObjectByType<XROrigin>();
        if (xr == null) { L("[StoryModeSetup] WARNING: No XROrigin — controllers not created."); return; }
        Transform offset = xr.CameraFloorOffsetObject != null ? xr.CameraFloorOffsetObject.transform : xr.transform;
        CreateController("Left Controller",  offset, new Vector3(-0.20f, -0.10f, 0.30f));
        CreateController("Right Controller", offset, new Vector3( 0.20f, -0.10f, 0.30f));
    }

    private static void CreateController(string name, Transform parent, Vector3 localPos)
    {
        Transform t = parent.Find(name);
        GameObject go = t != null ? t.gameObject : new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.identity;

        var sc = GetOrAdd<SphereCollider>(go);
        sc.isTrigger = true;
        sc.radius = 0.08f;
        var rb = GetOrAdd<Rigidbody>(go);
        rb.isKinematic = true;
        rb.useGravity = false;
        if (go.GetComponent<PlayerBlocker>() == null) go.AddComponent<PlayerBlocker>();
        if (go.GetComponent<ChildGrabber>() == null) go.AddComponent<ChildGrabber>();
    }

    // ── NavMesh ────────────────────────────────────────────────────────────
    private static void BakeStoryNavMesh(ChildNPC child, params Transform[] dynamicIgnores)
    {
        var hostGO = FindOrCreate("NavMesh");
        hostGO.transform.SetParent(null);
        hostGO.transform.position = Vector3.zero;
        var surface = GetOrAdd<NavMeshSurface>(hostGO);
        surface.collectObjects = CollectObjects.All;
        surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        surface.agentTypeID = 0;

        var settings = NavMesh.GetSettingsByID(0);
        settings.agentRadius = 0.28f;   // toddler — fits 1.1 m doorways
        settings.agentHeight = 1.0f;
        settings.agentSlope  = 50f;     // the stair ramp is ~33–42°
        settings.agentClimb  = 0.40f;
        settings.overrideVoxelSize = true;
        settings.voxelSize = 0.10f;     // finer voxels → thin ramp + door gaps resolve
        settings.minRegionArea = 0.5f;

        var markups = new List<NavMeshBuildMarkup>();
        void Ignore(Transform t) { if (t != null) markups.Add(new NavMeshBuildMarkup { ignoreFromBuild = true, root = t }); }
        if (child != null) Ignore(child.transform);
        if (dynamicIgnores != null) foreach (var t in dynamicIgnores) Ignore(t);
        Ignore(hostGO.transform);
        var xr = Object.FindAnyObjectByType<XROrigin>();
        if (xr != null) Ignore(xr.transform);

        // Furniture (tables, counters, sofas, beds…) otherwise becomes a walkable "island" on
        // top — which is why the baby spawned ON the dining table. Mark every solid collider whose
        // top sits 0.30–2.0 m above the ground floor (and isn't the huge floor slab) as NotWalkable
        // so the NavMesh stays on the floor only. Walls (tall) and floor/ramp (low/steep) are kept.
        int furniture = 0;
        foreach (var col in Object.FindObjectsByType<Collider>(FindObjectsInactive.Exclude))
        {
            if (!col.enabled || col.isTrigger) continue;
            if (col.GetComponentInParent<ChildNPC>() != null) continue; // the baby's own capsule
            var cb = col.bounds;
            float footprint = cb.size.x * cb.size.z;
            if (cb.max.y >= 0.30f && cb.max.y <= 2.0f && cb.min.y <= 1.5f && footprint < 12f)
            {
                markups.Add(new NavMeshBuildMarkup { overrideArea = true, area = 1 /* NotWalkable */, root = col.transform });
                furniture++;
            }
        }
        L($"[StoryModeSetup] Marked {furniture} furniture colliders NotWalkable (kept floor + walls + ramp).");
        DumpGeometry();

        var bounds = new Bounds(new Vector3(-5.5f, 3f, -5.5f), new Vector3(36f, 16f, 36f));
        var sources = new List<NavMeshBuildSource>();
        NavMeshBuilder.CollectSources(bounds, ~0, NavMeshCollectGeometry.PhysicsColliders, 0, markups, sources);

        var data = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
        if (data == null) { L("[StoryModeSetup] ERROR: NavMesh build returned null — check that the house has colliders."); return; }
        data.name = "HousePreview_NavMesh";

        var existing = AssetDatabase.LoadAssetAtPath<NavMeshData>(NavMeshAssetPath);
        if (existing != null) { EditorUtility.CopySerialized(data, existing); data = existing; }
        else AssetDatabase.CreateAsset(data, NavMeshAssetPath);
        AssetDatabase.SaveAssets();

        surface.RemoveData();
        surface.navMeshData = data;
        surface.AddData();
        EditorUtility.SetDirty(surface);

        L($"[StoryModeSetup] NavMesh baked from {sources.Count} collider sources (agentRadius {settings.agentRadius}). " +
          $"Asset: {NavMeshAssetPath}. Verify in the Navigation overlay that the living-room floor is covered.");
    }

    private static void SnapToNavMesh(Transform t, float yLift)
    {
        float intendedY = t.position.y;
        // 2.5 m keeps the snap on the SAME floor — wide enough to find nearby ground, narrow
        // enough not to grab the storey above (which is what put the old spawn at y≈3.3).
        if (NavMesh.SamplePosition(t.position, out NavMeshHit hit, 2.5f, NavMesh.AllAreas))
        {
            var p = hit.position; p.y += yLift; t.position = p;
            string warn = Mathf.Abs(hit.position.y - intendedY) > 1.5f ? "  *** WRONG FLOOR (large Y jump) ***" : "";
            L($"[StoryModeSetup] Snapped '{t.name}' to NavMesh at {p}.{warn}");
        }
        else
        {
            L($"[StoryModeSetup] WARNING: '{t.name}' has NO NavMesh within 2.5 m of {t.position} — " +
              "the point is off the walkable floor; adjust its coordinates.");
        }
    }

    private static void SnapHazardFloorHeight(Transform t, float heightAboveFloor)
    {
        if (NavMesh.SamplePosition(t.position, out NavMeshHit hit, 6f, NavMesh.AllAreas))
        {
            var p = t.position; p.y = hit.position.y + heightAboveFloor; t.position = p;
        }
    }

    /// <summary>Logs the floor slab(s) + chunky furniture with positions so spawn/fork can be placed precisely.</summary>
    private static void DumpGeometry()
    {
        var sb = new System.Text.StringBuilder("[StoryModeSetup] --- geometry (floor + chunky furniture, ground floor) ---\n");
        foreach (var col in Object.FindObjectsByType<Collider>(FindObjectsInactive.Exclude))
        {
            if (!col.enabled || col.isTrigger) continue;
            var b = col.bounds;
            float fp = b.size.x * b.size.z;
            bool floor = b.max.y < 0.30f && fp > 6f;
            bool furn  = b.max.y >= 0.30f && b.max.y <= 2.0f && b.min.y <= 1.5f && fp > 0.8f && fp < 12f;
            if (floor || furn)
                sb.AppendLine($"  {(floor ? "FLOOR" : "FURN ")} '{col.name}'  center=({b.center.x:F1},{b.center.y:F1},{b.center.z:F1})  size=({b.size.x:F1},{b.size.y:F1},{b.size.z:F1})");
        }
        L(sb.ToString());
    }

    /// <summary>Logs every renderer in the bathroom region so we can wire the EXISTING cleaning products
    /// (by name) instead of placeholders. Called before the S3 placeholders are created.</summary>
    private static void DumpBathroom()
    {
        var sb = new System.Text.StringBuilder("[StoryModeSetup] --- bathroom region renderers (existing props near the sink) ---\n");
        int n = 0;
        foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude))
        {
            var b = r.bounds;
            if (b.center.x < -4.3f || b.center.x > 0.3f) continue;   // bathroom X band
            if (b.center.z < -4.8f || b.center.z > 0.3f) continue;   // bathroom Z band
            if (b.center.y > 1.4f) continue;                         // at/under counter height
            float fp = b.size.x * b.size.z;
            if (fp > 3.5f) continue;                                 // skip the big floor slab
            sb.AppendLine($"  '{r.name}'  center=({b.center.x:F2},{b.center.y:F2},{b.center.z:F2})  size=({b.size.x:F2},{b.size.y:F2},{b.size.z:F2})");
            n++;
        }
        sb.AppendLine($"  ({n} renderers in the bathroom region)");
        L(sb.ToString());
    }

    /// <summary>Log whether a walkable path exists between two points (after sampling each to the NavMesh).</summary>
    private static void VerifyPath(string label, Vector3 from, Vector3 to)
    {
        if (NavMesh.SamplePosition(from, out var hf, 2.5f, NavMesh.AllAreas)) from = hf.position;
        if (NavMesh.SamplePosition(to,   out var ht, 2.5f, NavMesh.AllAreas)) to   = ht.position;
        var path = new NavMeshPath();
        NavMesh.CalculatePath(from, to, NavMesh.AllAreas, path);
        string verdict = path.status == NavMeshPathStatus.PathComplete ? "OK" : "*** " + path.status + " ***";
        L($"[StoryModeSetup] Path {label}: {verdict} ({path.corners.Length} corners)");
    }

    // ── Small helpers ───────────────────────────────────────────────────────
    private static T GetOrAdd<T>(GameObject go) where T : Component
    {
        var c = go.GetComponent<T>();
        return c != null ? c : go.AddComponent<T>(); // Unity's == handles fake-null
    }

    private static GameObject FindOrCreate(string name)
    {
        var go = GameObject.Find(name);
        return go != null ? go : new GameObject(name);
    }

    private static TextMeshProUGUI MakeTMP(GameObject parent, string name, Vector2 anchor, Vector2 anchoredPos, Vector2 size,
                                           float fontSize, TextAlignmentOptions align, Color col)
    {
        var existing = parent.transform.Find(name);
        GameObject go = existing != null ? existing.gameObject : new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        var tmp = GetOrAdd<TextMeshProUGUI>(go);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = anchor;
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = size;
        tmp.fontSize = fontSize;
        tmp.alignment = align;
        tmp.color = col;
        tmp.raycastTarget = false;
        tmp.text = string.Empty;
        return tmp;
    }

    private static AnimationClip FindClip(string path)
    {
        foreach (var a in AssetDatabase.LoadAllAssetRepresentationsAtPath(path))
            if (a is AnimationClip c && !c.name.StartsWith("__preview")) return c;
        return null;
    }

    private static Material FindMaterial(string name)
    {
        var guids = AssetDatabase.FindAssets(name + " t:Material");
        foreach (var g in guids)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(g));
            if (m != null && m.name == name) return m;
        }
        return guids.Length > 0 ? AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guids[0])) : null;
    }

    private static void FitHeightFeetAtOrigin(GameObject go, float targetHeight)
    {
        var rends = go.GetComponentsInChildren<Renderer>();
        if (rends.Length == 0) return;
        Bounds b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
        if (b.size.y > 1e-4f) go.transform.localScale *= targetHeight / b.size.y;

        // Recompute after scaling and lift so the feet sit at the parent's origin.
        rends = go.GetComponentsInChildren<Renderer>();
        b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
        float parentY = go.transform.parent != null ? go.transform.parent.position.y : 0f;
        var lp = go.transform.localPosition;
        lp.y += parentY - b.min.y;
        go.transform.localPosition = lp;
    }

    /// <summary>Lifts the object so the bottom of its renderer bounds rests at <paramref name="surfaceY"/>.</summary>
    private static void RestOnSurface(GameObject go, float surfaceY)
    {
        var rends = go.GetComponentsInChildren<Renderer>();
        if (rends.Length == 0) return;
        Bounds b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
        var p = go.transform.position;
        p.y += surfaceY - b.min.y;
        go.transform.position = p;
    }

    private static void FitMaxDimension(GameObject go, float target)
    {
        var rends = go.GetComponentsInChildren<Renderer>();
        if (rends.Length == 0) return;
        Bounds b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
        float m = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
        if (m > 1e-4f) go.transform.localScale *= target / m;
    }
}
