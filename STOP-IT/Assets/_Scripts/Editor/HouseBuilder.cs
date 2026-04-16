using UnityEngine;
using UnityEditor;
using Unity.AI.Navigation;

/// <summary>
/// Builds a realistic two-story house.
///
/// GROUND FLOOR (Y=0):
///  14m wide (X: -7..7) x 12m deep (Z: -6..6)
///
///  ┌───────────┬───────────┐ Z=6
///  │           │           │
///  │  SALON    │  CUISINE  │ 5m
///  │ (prise)   │(microwave)│
///  │           │           │
///  ├──door─────┼──door─────┤ Z=1
///  │  COULOIR               │ 2m (Z:-1..1)
///  ├──door─────┼──door─────┤ Z=-1
///  │           │           │
///  │   SdB     │ ESCALIER  │ 5m
///  │ (produit) │  + cage   │
///  │           │           │
///  └───────────┴───────────┘ Z=-6
///
/// FIRST FLOOR (Y=3):  only right half (above escalier + cuisine)
///  ┌───────────────────────┐
///  │     CHAMBRE           │
///  │   (fenêtre + pigeon)  │
///  │     7m x 6m           │
///  └───────────────────────┘
///
/// The staircase is a real set of 10 steps going from Y=0 to Y=3,
/// located in the bottom-right room, with a landing at the top
/// that connects to the bedroom floor.
/// </summary>
public static class HouseBuilder
{
    const float W = 0.15f;   // wall thickness
    const float H = 3f;      // floor height
    const float H2 = 6f;     // total height (2 floors)

    [MenuItem("Tools/STOP IT/Build House")]
    public static void BuildHouse()
    {
        var house = new GameObject("House");
        house.isStatic = true;
        Undo.RegisterCreatedObjectUndo(house, "Build House");

        // ══════════ STRUCTURE ══════════
        GroundFloor(house);
        ExteriorWalls(house);
        InteriorWalls(house);
        Staircase(house);
        FirstFloor(house);

        // ══════════ ROOMS ══════════
        Salon(house);
        Kitchen(house);
        Bathroom(house);
        Bedroom(house);

        // ══════════ CEILING ══════════
        // Left side ceiling (salon + sdb have no second floor above)
        Slab(house, "Ceiling_Left", new Vector3(-3.5f, H, 0), new Vector3(7, W, 12));

        // ══════════ SPAWNS ══════════
        Spawns(house);

        // ══════════ LIGHT ══════════
        var light = new GameObject("Sun");
        light.transform.SetParent(house.transform, false);
        light.transform.position = new Vector3(0, 12, 0);
        light.transform.eulerAngles = new Vector3(50, -30, 0);
        var l = light.AddComponent<Light>();
        l.type = LightType.Directional;
        l.intensity = 1.5f;
        l.shadows = LightShadows.Soft;
        Undo.RegisterCreatedObjectUndo(light, "Sun");

        Debug.Log("[STOP IT] House built — 2 floors, 5 rooms!");
    }

    // ─── GROUND FLOOR ──────────────────────────────────────────────────
    static void GroundFloor(GameObject p)
    {
        // Single ground plane covering the whole footprint
        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "GroundFloor";
        floor.transform.SetParent(p.transform, false);
        floor.transform.position = new Vector3(0, 0, 0);
        // Plane is 10x10 by default, scale to 14x12
        floor.transform.localScale = new Vector3(1.4f, 1, 1.2f);
        floor.isStatic = true;
        Mat(floor, "Mat_Floor");
        var nav = floor.AddComponent<NavMeshSurface>();
        nav.collectObjects = CollectObjects.All;
        Undo.RegisterCreatedObjectUndo(floor, "GroundFloor");
    }

    // ─── EXTERIOR WALLS ────────────────────────────────────────────────
    static void ExteriorWalls(GameObject p)
    {
        // Full-height exterior (H2=6 to cover both floors)
        // North (Z=6)
        Box(p, "Ext_N", new Vector3(0, H2 / 2, 6), new Vector3(14 + W, H2, W));
        // South (Z=-6)
        Box(p, "Ext_S", new Vector3(0, H2 / 2, -6), new Vector3(14 + W, H2, W));
        // West (X=-7)
        Box(p, "Ext_W", new Vector3(-7, H2 / 2, 0), new Vector3(W, H2, 12 + W));

        // East (X=7) — with WINDOW opening on first floor for bedroom
        // Ground floor section (Y: 0..3)
        Box(p, "Ext_E_GF", new Vector3(7, H / 2, 0), new Vector3(W, H, 12 + W));
        // First floor: wall below window (Y: 3..4)
        Box(p, "Ext_E_1F_Bot", new Vector3(7, 3.5f, 0), new Vector3(W, 1, 12 + W));
        // First floor: wall above window (Y: 5..6)
        Box(p, "Ext_E_1F_Top", new Vector3(7, 5.5f, 0), new Vector3(W, 1, 12 + W));
        // First floor: wall sections flanking window (window at Z: -1..1)
        Box(p, "Ext_E_1F_Left", new Vector3(7, 4.5f, -3.5f), new Vector3(W, 1, 5));
        Box(p, "Ext_E_1F_Right", new Vector3(7, 4.5f, 3.5f), new Vector3(W, 1, 5));
        // Window ledge
        Obj(p, "WindowLedge", PrimitiveType.Cube, new Vector3(6.85f, 4f, 0), new Vector3(0.3f, 0.08f, 2f), "Mat_Furniture");
    }

    // ─── INTERIOR WALLS ────────────────────────────────────────────────
    static void InteriorWalls(GameObject p)
    {
        // ── Center wall (X=0) — divides left / right ──
        // Top section: Salon | Cuisine (Z: 1..6), door at Z=3..4
        Box(p, "Int_Center_1", new Vector3(0, H / 2, 4.75f), new Vector3(W, H, 2.5f));  // Z:3.5..6
        Box(p, "Int_Center_2", new Vector3(0, H / 2, 1.75f), new Vector3(W, H, 1.5f));  // Z:1..2.5
        // Bottom section: SdB | Escalier (Z: -6..-1), door at Z=-2.5..-1.5
        Box(p, "Int_Center_3", new Vector3(0, H / 2, -4.25f), new Vector3(W, H, 3.5f)); // Z:-6..-2.5
        Box(p, "Int_Center_4", new Vector3(0, H / 2, -1.25f), new Vector3(W, H, 0.5f)); // Z:-1.5..-1

        // ── Hallway north wall (Z=1) ──
        // Left: X: -7..-1.5, door at X=-1.5..0
        Box(p, "Hall_N_L", new Vector3(-4.75f, H / 2, 1), new Vector3(4.5f, H, W));
        // Right: X: 1.5..7, door at X:0..1.5
        Box(p, "Hall_N_R", new Vector3(4.75f, H / 2, 1), new Vector3(4.5f, H, W));

        // ── Hallway south wall (Z=-1) ──
        // Left: X: -7..-1.5, door at X=-1.5..0
        Box(p, "Hall_S_L", new Vector3(-4.75f, H / 2, -1), new Vector3(4.5f, H, W));
        // Right: X: 1.5..7, door at X:0..1.5
        Box(p, "Hall_S_R", new Vector3(4.75f, H / 2, -1), new Vector3(4.5f, H, W));
    }

    // ─── STAIRCASE ─────────────────────────────────────────────────────
    static void Staircase(GameObject p)
    {
        // Stairs in bottom-right room (X: 0..7, Z: -6..-1)
        // 10 steps going from Y=0 to Y=3, along Z axis (south to north)
        // Each step: 0.3m high, 0.5m deep, 2m wide
        var stairParent = new GameObject("Staircase");
        stairParent.transform.SetParent(p.transform, false);
        stairParent.isStatic = true;

        float stepH = H / 10f;  // 0.3m per step
        float stepD = 0.45f;
        float stepW = 2f;
        float startZ = -5.5f;
        float startX = 5f;

        for (int i = 0; i < 10; i++)
        {
            float y = stepH * (i + 0.5f);
            float z = startZ + i * stepD;
            var step = GameObject.CreatePrimitive(PrimitiveType.Cube);
            step.name = "Step_" + i;
            step.transform.SetParent(stairParent.transform, false);
            step.transform.position = new Vector3(startX, y, z);
            step.transform.localScale = new Vector3(stepW, stepH, stepD);
            step.isStatic = true;
            Mat(step, "Mat_Floor");
            Undo.RegisterCreatedObjectUndo(step, "Step_" + i);
        }

        // Railing wall on the outer side of stairs (against east wall)
        Box(stairParent, "StairRail", new Vector3(startX + 1.1f, H / 2, -3.5f), new Vector3(W, H, 5));

        // Skateboard at top of stairs
        Obj(stairParent, "Skateboard", PrimitiveType.Cube, new Vector3(5f, H + 0.05f, -1f), new Vector3(0.7f, 0.05f, 0.25f), "Mat_Furniture");

        // HazardZone at bottom of stairs
        HZ(stairParent, "HazardZone_StairsBottom", new Vector3(startX, 0.2f, startZ), new Vector3(2f, 0.4f, 0.5f), "Bas de l'escalier");

        Undo.RegisterCreatedObjectUndo(stairParent, "Staircase");
    }

    // ─── FIRST FLOOR ───────────────────────────────────────────────────
    static void FirstFloor(GameObject p)
    {
        // First floor with stairwell opening (trémie)
        // Stairwell opening: X: 3.5..7, Z: -6..-1 (above the stairs)
        //
        // Layout of first floor slab (X: 0..7, Z: -6..6):
        //  ┌───────────────────────┐ Z=6
        //  │                       │
        //  │  Front slab           │ X: 0..7, Z: -1..6
        //  │  (above cuisine)      │
        //  │                       │
        //  ├───────┬───────────────┤ Z=-1
        //  │ Back  │  STAIRWELL    │
        //  │ left  │  (open hole)  │ X: 3.5..7, Z: -6..-1
        //  │ slab  │               │
        //  └───────┴───────────────┘ Z=-6
        //  X=0    X=3.5           X=7

        // Front slab: X: 0..7, Z: -1..6 (7m x 7m)
        Slab(p, "1F_Front", new Vector3(3.5f, H, 2.5f), new Vector3(7, W, 7));

        // Back-left slab: X: 0..3.5, Z: -6..-1 (3.5m x 5m)
        Slab(p, "1F_BackLeft", new Vector3(1.75f, H, -3.5f), new Vector3(3.5f, W, 5));

        // No slab at X: 3.5..7, Z: -6..-1 → this is the stairwell opening!

        // First floor walls (only around bedroom, H above the slab)
        // West wall of bedroom (X=0, Y: 3..6)
        Box(p, "1F_Wall_W", new Vector3(0, H + H / 2, 0), new Vector3(W, H, 12 + W));

        // Railing around the stairwell opening (safety)
        Box(p, "StairwellRail_S", new Vector3(5.25f, H + 0.5f, -1), new Vector3(3.5f, 1, W));  // south edge
        Box(p, "StairwellRail_W", new Vector3(3.5f, H + 0.5f, -3.5f), new Vector3(W, 1, 5));   // west edge
    }

    // ─── ROOM 1: SALON (ground floor, top-left) ───────────────────────
    static void Salon(GameObject p)
    {
        var r = Room(p, "Room_Salon");
        // Sofa against west wall
        Obj(r, "Sofa", PrimitiveType.Cube, new Vector3(-5.5f, 0.35f, 4), new Vector3(1f, 0.7f, 2.5f), "Mat_Furniture");
        // TV stand opposite sofa
        Obj(r, "TV_Stand", PrimitiveType.Cube, new Vector3(-2, 0.4f, 5.5f), new Vector3(1.8f, 0.8f, 0.4f), "Mat_Furniture");
        // Coffee table between sofa and TV
        Obj(r, "CoffeeTable", PrimitiveType.Cube, new Vector3(-3.5f, 0.25f, 4), new Vector3(1.2f, 0.5f, 0.6f), "Mat_Furniture");
        // Electrical outlet low on west wall
        Obj(r, "Outlet", PrimitiveType.Cube, new Vector3(-6.85f, 0.3f, 3), new Vector3(0.15f, 0.2f, 0.1f), "Mat_Hazard");
        // Fork near the outlet
        Obj(r, "Fork", PrimitiveType.Cube, new Vector3(-6.5f, 0.01f, 3), new Vector3(0.02f, 0.02f, 0.15f), "Mat_Wall");
        // Hazard
        HZ(r, "HazardZone_Outlet", new Vector3(-6.85f, 0.3f, 3), new Vector3(0.4f, 0.5f, 0.4f), "Prise electrique");
    }

    // ─── ROOM 2: CUISINE (ground floor, top-right) ────────────────────
    static void Kitchen(GameObject p)
    {
        var r = Room(p, "Room_Kitchen");
        // Counter along north wall
        Obj(r, "Counter", PrimitiveType.Cube, new Vector3(3.5f, 0.45f, 5.5f), new Vector3(5f, 0.9f, 0.6f), "Mat_Furniture");
        // Fridge
        Obj(r, "Fridge", PrimitiveType.Cube, new Vector3(6.2f, 0.9f, 5.5f), new Vector3(0.7f, 1.8f, 0.7f), "Mat_Furniture");
        // Microwave on counter
        Obj(r, "Microwave", PrimitiveType.Cube, new Vector3(2.5f, 1.05f, 5.5f), new Vector3(0.5f, 0.35f, 0.4f), "Mat_Furniture");
        // Cat on counter
        Obj(r, "Cat", PrimitiveType.Capsule, new Vector3(4f, 1.2f, 5.5f), new Vector3(0.15f, 0.15f, 0.15f), "Mat_Child");
        // Kitchen table
        Obj(r, "KitchenTable", PrimitiveType.Cube, new Vector3(3.5f, 0.4f, 3), new Vector3(1.5f, 0.8f, 1f), "Mat_Furniture");
        // Hazard
        HZ(r, "HazardZone_Microwave", new Vector3(2.5f, 1.05f, 5.5f), new Vector3(0.5f, 0.35f, 0.4f), "Micro-ondes");
    }

    // ─── ROOM 4: SALLE DE BAIN (ground floor, bottom-left) ────────────
    static void Bathroom(GameObject p)
    {
        var r = Room(p, "Room_Bathroom");
        // Cabinet/sink against south wall
        Obj(r, "Cabinet", PrimitiveType.Cube, new Vector3(-4, 0.4f, -5.3f), new Vector3(1.5f, 0.8f, 0.5f), "Mat_Furniture");
        // Cleaning product bottle on cabinet
        Obj(r, "CleaningBottle", PrimitiveType.Cylinder, new Vector3(-4, 0.9f, -5.3f), new Vector3(0.08f, 0.12f, 0.08f), "Mat_Hazard");
        // Bathtub along west wall
        Obj(r, "Bathtub", PrimitiveType.Cube, new Vector3(-6, 0.3f, -3.5f), new Vector3(1f, 0.6f, 1.8f), "Mat_Furniture");
        // Toilet
        Obj(r, "Toilet", PrimitiveType.Cube, new Vector3(-2, 0.25f, -5.3f), new Vector3(0.4f, 0.5f, 0.4f), "Mat_Furniture");
        // Hazard
        HZ(r, "HazardZone_CleaningProduct", new Vector3(-4, 0.9f, -5.3f), new Vector3(0.4f, 0.4f, 0.4f), "Produit menager");
    }

    // ─── ROOM 5: CHAMBRE (first floor, right side) ────────────────────
    static void Bedroom(GameObject p)
    {
        var r = Room(p, "Room_Bedroom");
        // Bed
        Obj(r, "Bed", PrimitiveType.Cube, new Vector3(4, H + 0.3f, 3.5f), new Vector3(2f, 0.6f, 1.5f), "Mat_Furniture");
        // Wardrobe
        Obj(r, "Wardrobe", PrimitiveType.Cube, new Vector3(1, H + 1f, 5), new Vector3(1.5f, 2f, 0.6f), "Mat_Furniture");
        // Desk near window
        Obj(r, "Desk", PrimitiveType.Cube, new Vector3(6, H + 0.4f, 0), new Vector3(0.8f, 0.8f, 1.2f), "Mat_Furniture");
        // Pigeon outside the window
        Obj(r, "Pigeon", PrimitiveType.Sphere, new Vector3(7.3f, 4.3f, 0), new Vector3(0.2f, 0.2f, 0.2f), "Mat_Wall");
        // Hazard: window ledge
        HZ(r, "HazardZone_Window", new Vector3(6.85f, 4.1f, 0), new Vector3(0.4f, 0.3f, 2f), "Rebord de fenetre");
    }

    // ─── SPAWN POINTS ──────────────────────────────────────────────────
    static void Spawns(GameObject p)
    {
        // Salon: child near sofa, player in center
        SP(p, "SpawnChild_Salon", new Vector3(-4, 0, 2.5f));
        SP(p, "SpawnPlayer_Salon", new Vector3(-3, 0, 3.5f));
        // Kitchen: child near table, player at door
        SP(p, "SpawnChild_Kitchen", new Vector3(4, 0, 3.5f));
        SP(p, "SpawnPlayer_Kitchen", new Vector3(2, 0, 2.5f));
        // Stairs: child at top landing, player at bottom in hallway
        SP(p, "SpawnChild_Stairs", new Vector3(5, H, -1));
        SP(p, "SpawnPlayer_Stairs", new Vector3(3, 0, 0));
        // Bathroom: child near cabinet, player near door
        SP(p, "SpawnChild_Bathroom", new Vector3(-3, 0, -3));
        SP(p, "SpawnPlayer_Bathroom", new Vector3(-2, 0, -2));
        // Bedroom (1st floor): child near desk/window, player near stairs
        SP(p, "SpawnChild_Bedroom", new Vector3(5, H, 1));
        SP(p, "SpawnPlayer_Bedroom", new Vector3(2, H, -2));
    }

    // ═══ HELPERS ═══════════════════════════════════════════════════════
    static GameObject Room(GameObject p, string n)
    {
        var r = new GameObject(n);
        r.transform.SetParent(p.transform, false);
        r.isStatic = true;
        Undo.RegisterCreatedObjectUndo(r, n);
        return r;
    }

    static void Box(GameObject p, string n, Vector3 pos, Vector3 scale)
    {
        var w = GameObject.CreatePrimitive(PrimitiveType.Cube);
        w.name = n;
        w.transform.SetParent(p.transform, false);
        w.transform.position = pos;
        w.transform.localScale = scale;
        w.isStatic = true;
        Mat(w, "Mat_Wall");
        Undo.RegisterCreatedObjectUndo(w, n);
    }

    static void Slab(GameObject p, string n, Vector3 pos, Vector3 scale)
    {
        var s = GameObject.CreatePrimitive(PrimitiveType.Cube);
        s.name = n;
        s.transform.SetParent(p.transform, false);
        s.transform.position = pos;
        s.transform.localScale = scale;
        s.isStatic = true;
        Mat(s, "Mat_Floor");
        Undo.RegisterCreatedObjectUndo(s, n);
    }

    static GameObject Obj(GameObject p, string n, PrimitiveType t, Vector3 pos, Vector3 scale, string mat)
    {
        var o = GameObject.CreatePrimitive(t);
        o.name = n;
        o.transform.SetParent(p.transform, false);
        o.transform.position = pos;
        o.transform.localScale = scale;
        o.isStatic = true;
        Mat(o, mat);
        Undo.RegisterCreatedObjectUndo(o, n);
        return o;
    }

    static void HZ(GameObject p, string n, Vector3 pos, Vector3 scale, string hazardName)
    {
        var o = GameObject.CreatePrimitive(PrimitiveType.Cube);
        o.name = n;
        o.transform.SetParent(p.transform, false);
        o.transform.position = pos;
        o.transform.localScale = scale;
        Mat(o, "Mat_Hazard");
        var c = o.GetComponent<BoxCollider>();
        if (c) c.isTrigger = true;
        var hz = o.AddComponent<HazardZone>();
        hz.hazardName = hazardName;
        hz.warningRadius = 2.5f;
        hz.hazardRenderer = o.GetComponent<Renderer>();
        Undo.RegisterCreatedObjectUndo(o, n);
    }

    static void SP(GameObject p, string n, Vector3 pos)
    {
        var g = new GameObject(n);
        g.transform.SetParent(p.transform, false);
        g.transform.position = pos;
        Undo.RegisterCreatedObjectUndo(g, n);
    }

    static void Mat(GameObject o, string matName)
    {
        var guids = AssetDatabase.FindAssets(matName + " t:Material");
        if (guids.Length > 0)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guids[0]));
            if (m != null) { var r = o.GetComponent<Renderer>(); if (r) r.sharedMaterial = m; }
        }
    }
}
