using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;

/// <summary>
/// STOP IT! — Art Skin
/// Habille le greybox (généré par HouseBuilder) avec les assets Synty POLYGON + modèles GLB/OBJ,
/// SANS toucher au gameplay : on garde le nom, la position, les colliders et les scripts de chaque
/// objet greybox ; on désactive seulement son MeshRenderer et on superpose l'art.
///
///  • Meubles/électroménager : l'art est un FRÈRE (sous la même pièce) placé à la position monde.
///  • Props interactifs (Fork/Cat/Skateboard/bouteilles/Pigeon) : l'art est un ENFANT du prop
///    (pour suivre quand il est porté/saisi), counter-scalé à une taille monde uniforme.
///  • Bébé NPC : modèle sous un enfant "MeshHolder" (que ChildNPC anime en bob/tilt).
///
/// Idempotent : "Apply Art Skin" supprime d'abord l'art existant (objets suffixés __ART) et
/// réactive les renderers greybox, puis réapplique. Ajustez les offsets ci-dessous et relancez.
/// "Remove Art Skin" revient au greybox.
/// </summary>
public static class ArtSkin
{
    const string TOWN   = "Assets/PolygonTown/Prefabs/";
    const string OFFICE = "Assets/PolygonOffice/Prefabs/";
    const string MODELS = "Assets/_ThirdParty/Models/";
    const string SFX    = "__ART";

    // ── Meubles / électroménager : frère sous la pièce, position monde, rotation Y, échelle Synty ──
    struct Furn { public string t, a; public Vector3 p; public float r, s;
        public Furn(string t, string a, float x, float y, float z, float r, float s)
        { this.t=t; this.a=a; p=new Vector3(x,y,z); this.r=r; this.s=s; } }

    static Furn[] FURNITURE() => new[]
    {
        // ── Salon (sol Y=0) ──
        new Furn("Sofa",         TOWN+"Props/SM_Prop_Couch_01.prefab",            -5.5f, 0f,  4f,    90f, 1f),
        new Furn("TV_Stand",     TOWN+"Props/SM_Prop_TVCabinet_01.prefab",        -2f,   0f,  5.6f,  180f,1f),
        new Furn("CoffeeTable",  TOWN+"Props/SM_Prop_CoffeeTable_01.prefab",      -3.5f, 0f,  4f,    0f,  1f),
        // ── Cuisine ──
        new Furn("Counter",      TOWN+"Props/SM_Prop_Kitchen_Counter_01.prefab",   3.5f, 0f,  5.7f,  180f,1f),
        new Furn("Fridge",       TOWN+"Props/SM_Prop_Kitchen_Fridge_01.prefab",    6.2f, 0f,  5.6f,  180f,1f),
        new Furn("Microwave",    TOWN+"Props/SM_Prop_Microwave_01.prefab",         2.5f, 0.9f,5.6f,  180f,1f),
        new Furn("KitchenTable", TOWN+"Props/SM_Prop_DiningTable_01.prefab",        3.5f, 0f,  3f,    0f,  1f),
        // ── Salle de bain ──
        new Furn("Cabinet",      TOWN+"Props/SM_Prop_BathroomSink_01.prefab",     -4f,   0f, -5.6f,  0f,  1f),
        new Furn("Bathtub",      TOWN+"Props/SM_Prop_Bath_01.prefab",             -6f,   0f, -3.5f,  90f, 1f),
        new Furn("Toilet",       TOWN+"Props/SM_Prop_Toilet_01.prefab",           -2f,   0f, -5.6f,  0f,  1f),
        // ── Chambre (1er étage, sol Y=3) ──
        new Furn("Bed",          TOWN+"Props/SM_Prop_Bed_Single_01.prefab",        4f,   3f,  3.5f,  0f,  1f),
        new Furn("Wardrobe",     TOWN+"Props/SM_Prop_Wardrobe_01.prefab",          1f,   3f,  5.6f,  180f,1f),
        new Furn("Desk",         TOWN+"Props/SM_Prop_Desk_01.prefab",              6f,   3f,  0f,    270f,1f),
        // ── Pièce pigeon (1er étage) ──
        new Furn("Cot",            TOWN+"Props/SM_Prop_BabyCot_01.prefab",         0.8f, 3f, -4.5f,  90f, 1f),
        new Furn("NightTable_P6",  TOWN+"Props/SM_Prop_BedsideTable_01.prefab",    1.9f, 3f, -4.5f,  90f, 1f),
        new Furn("ToyBox_P6",      TOWN+"Props/SM_Prop_ToyBlock_01.prefab",        0.7f, 3f, -2.5f,  0f,  1f),
        // ── Prop de danger visible (le cube HazardZone_* reste séparé pour le pulse) ──
        new Furn("Outlet",       OFFICE+"Props/Wall Props/SM_Prop_Socket_Wall_01.prefab", -6.9f, 0.3f, 3f, 90f, 1f),
    };

    // ── Props interactifs : enfant du prop, counter-scalé à une taille monde uniforme ──
    struct Prop { public string t, a; public float w, y;
        public Prop(string t, string a, float w, float y) { this.t=t; this.a=a; this.w=w; this.y=y; } }

    static Prop[] PROPS() => new[]
    {
        new Prop("Fork",           TOWN+"Items/SM_Item_Cutlery_Fork_01.prefab",         0.22f, 0f),
        new Prop("Cat",            MODELS+"Cat.glb",                                     0.5f,  0f),
        new Prop("Skateboard",     MODELS+"Skateboard.glb",                              0.8f,  0f),
        new Prop("CleaningBottle", OFFICE+"Props/Misc/SM_Prop_SprayBottle_01.prefab",   0.28f, 0f),
        new Prop("WaterBottle",    OFFICE+"Props/Desk Props/SM_Prop_Bottle_01.prefab",  0.28f, 0f),
        new Prop("Pigeon",         MODELS+"Mourning dove.glb",                           0.28f, 0f),
        new Prop("Pigeon_P6",      MODELS+"Mourning dove.glb",                           0.28f, 0f),
    };

    const float BABY_MAXDIM = 0.8f;   // hauteur monde cible du bébé DEBOUT (~bambin)
    static readonly Vector3 BABY_EULER = new Vector3(-90f, 0f, 0f); // OBJ Z-up → redressé debout (ajuster si besoin)

    [MenuItem("Tools/STOP IT/Apply Art Skin")]
    public static void ApplyArtSkin()
    {
        RemoveArtSkinInternal(false);
        SkinStructure();
        HideGameplayMarkers();
        int n = 0;
        foreach (var f in FURNITURE()) if (ApplyFurniture(f)) n++;
        foreach (var p in PROPS())     if (ApplyProp(p))      n++;
        bool child = ApplyChild();
        if (!Application.isPlaying) UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
        Debug.Log($"[ART SKIN] {n} pièces d'art appliquées" + (child ? " + bébé" : "") +
                  ". Ajustez les offsets dans ArtSkin.cs et relancez si besoin.");
    }

    [MenuItem("Tools/STOP IT/Remove Art Skin")]
    public static void RemoveArtSkin() => RemoveArtSkinInternal(true);

    static void RemoveArtSkinInternal(bool save)
    {
        int removed = 0;
        foreach (var tr in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (tr != null && tr.name.EndsWith(SFX)) { Object.DestroyImmediate(tr.gameObject); removed++; }
        }
        foreach (var f in FURNITURE()) SetRenderer(f.t, true);
        foreach (var p in PROPS())     SetRenderer(p.t, true);
        var npc = Object.FindAnyObjectByType<ChildNPC>();
        if (npc != null)
            foreach (var mr in npc.GetComponentsInChildren<MeshRenderer>(true)) mr.enabled = true;
        // Restaurer les marqueurs gameplay cachés (cubes jaunes HazardZone + dalles FloorObstacles).
        foreach (var hz in Object.FindObjectsByType<HazardZone>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        { var mr = hz.GetComponent<MeshRenderer>(); if (mr != null) mr.enabled = true; }
        foreach (var tr in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (tr.name == "FloorObstacles") tr.gameObject.SetActive(true);
        if (save)
        {
            if (!Application.isPlaying) UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
            Debug.Log($"[ART SKIN] {removed} pièces d'art supprimées, renderers greybox restaurés.");
        }
    }

    static bool ApplyFurniture(Furn f)
    {
        var target = GameObject.Find(f.t);
        if (target == null) { Debug.LogWarning($"[ART SKIN] cible '{f.t}' introuvable."); return false; }
        var go = Instantiate(f.a, f.t);
        if (go == null) return false;
        go.transform.SetParent(target.transform.parent, false);  // même pièce (échelle 1 → pas de distorsion)
        go.transform.position = f.p;
        go.transform.rotation = Quaternion.Euler(0f, f.r, 0f);
        go.transform.localScale = Vector3.one * f.s;
        go.isStatic = true;
        NavIgnore(go);
        SetRenderer(f.t, false);
        Undo.RegisterCreatedObjectUndo(go, "Art " + f.t);
        return true;
    }

    static bool ApplyProp(Prop p)
    {
        var target = GameObject.Find(p.t);
        if (target == null) { Debug.LogWarning($"[ART SKIN] prop '{p.t}' introuvable."); return false; }
        var go = Instantiate(p.a, p.t);   // instancié à la racine pour mesurer ses bounds réels
        if (go == null) return false;
        float f = FitFactor(go, p.w);     // facteur pour une taille monde uniforme = p.w
        go.transform.SetParent(target.transform, false);  // enfant du prop → suit quand porté/saisi
        Vector3 ls = target.transform.lossyScale;
        go.transform.localScale = new Vector3(
            f / Mathf.Max(1e-4f, Mathf.Abs(ls.x)),
            f / Mathf.Max(1e-4f, Mathf.Abs(ls.y)),
            f / Mathf.Max(1e-4f, Mathf.Abs(ls.z)));
        go.transform.localPosition = new Vector3(0f, p.y, 0f);
        go.transform.localRotation = Quaternion.identity;
        NavIgnore(go);
        SetRenderer(p.t, false);
        Undo.RegisterCreatedObjectUndo(go, "Art " + p.t);
        return true;
    }

    static bool ApplyChild()
    {
        var npc = Object.FindAnyObjectByType<ChildNPC>();
        if (npc == null) { Debug.LogWarning("[ART SKIN] ChildNPC introuvable."); return false; }

        // 1) Masquer les renderers greybox existants du NPC AVANT d'ajouter le bébé.
        foreach (var mr in npc.GetComponentsInChildren<MeshRenderer>(true)) mr.enabled = false;

        // 2) Assurer un enfant "MeshHolder" (ChildNPC l'anime en bob/tilt).
        var holderT = npc.transform.Find("MeshHolder");
        GameObject holder = holderT != null ? holderT.gameObject : null;
        if (holder == null)
        {
            holder = new GameObject("MeshHolder");
            holder.transform.SetParent(npc.transform, false);
            holder.transform.localPosition = Vector3.zero;
            holder.transform.localRotation = Quaternion.identity;
        }

        // 3) (Re)créer le bébé sous le holder.
        var prev = holder.transform.Find("baby" + SFX);
        if (prev != null) Object.DestroyImmediate(prev.gameObject);

        var baby = Instantiate(MODELS + "baby.obj", "baby");  // bébé DEBOUT (racine, pour mesurer)
        if (baby == null) return false;
        float f = FitFactor(baby, BABY_MAXDIM);
        baby.transform.SetParent(holder.transform, false);
        Vector3 ls = holder.transform.lossyScale;
        baby.transform.localScale = new Vector3(
            f / Mathf.Max(1e-4f, Mathf.Abs(ls.x)),
            f / Mathf.Max(1e-4f, Mathf.Abs(ls.y)),
            f / Mathf.Max(1e-4f, Mathf.Abs(ls.z)));
        baby.transform.localRotation = Quaternion.Euler(BABY_EULER);
        baby.transform.localPosition = Vector3.zero;
        // Poser les pieds du bébé au niveau de la base du NPC (holder).
        if (TryBounds(baby, out var bb))
            baby.transform.position += Vector3.up * (holder.transform.position.y - (bb.center.y - bb.size.y * 0.5f));
        NavIgnore(baby);

        // Le pivot du bébé est aux pieds (posés au sol) → l'agent ne doit PAS surélever le root,
        // sinon le bébé flotte en mouvement (l'ancien baseOffset valait ~1 pour la capsule).
        var agent = npc.GetComponent<NavMeshAgent>();
        if (agent != null) agent.baseOffset = 0f;

        Undo.RegisterCreatedObjectUndo(baby, "Art baby");
        return true;
    }

    // ── Helpers ──
    static GameObject Instantiate(string assetPath, string targetName)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        if (asset == null) { Debug.LogWarning($"[ART SKIN] asset manquant: {assetPath}"); return null; }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(asset);
        if (go == null) { Debug.LogWarning($"[ART SKIN] instanciation échouée: {assetPath}"); return null; }
        go.name = targetName + SFX;
        return go;
    }

    static void SetRenderer(string targetName, bool on)
    {
        var go = GameObject.Find(targetName);
        if (go == null) return;
        var mr = go.GetComponent<MeshRenderer>();
        if (mr != null) mr.enabled = on;
    }

    static void NavIgnore(GameObject go)
    {
        var mod = go.GetComponent<NavMeshModifier>() ?? go.AddComponent<NavMeshModifier>();
        mod.ignoreFromBuild = true;
    }

    // Donne des matériaux « maison » aux objets de structure greybox (murs/sol/plafond),
    // sans toucher aux HazardZone_* (jaunes, qui pulsent) ni aux objets d'art (__ART).
    static void SkinStructure()
    {
        var wall  = GetOrCreateMat("Mat_Home_Wall",    new Color(0.92f, 0.89f, 0.82f)); // crème chaud
        var floor = GetOrCreateMat("Mat_Home_Floor",   new Color(0.78f, 0.66f, 0.48f)); // bois clair
        var ceil  = GetOrCreateMat("Mat_Home_Ceiling", new Color(0.96f, 0.96f, 0.96f)); // blanc cassé
        foreach (var mr in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            string n = mr.name;
            if (n.EndsWith(SFX)) continue;
            if (n.StartsWith("Ext_") || n.StartsWith("Int_") || n.StartsWith("Hall_") ||
                n.StartsWith("1F_Wall") || n.StartsWith("StairwellRail") || n == "StairRail")
                mr.sharedMaterial = wall;
            else if (n == "GroundFloor" || n.StartsWith("Step_") || n.StartsWith("1F_Front") || n.StartsWith("1F_BackLeft"))
                mr.sharedMaterial = floor;
            else if (n.StartsWith("Ceiling"))
                mr.sharedMaterial = ceil;
        }
    }

    // Cache les marqueurs greybox SANS toucher à leur logique :
    //  • cubes jaunes HazardZone_* → on désactive juste le MeshRenderer (le composant HazardZone +
    //    le collider trigger restent → détection NPC + succès/échec intacts, ex. bas d'escalier scénario 4).
    //  • dalles violettes FloorObstacles → masquées (réutilisables plus tard).
    static void HideGameplayMarkers()
    {
        foreach (var hz in Object.FindObjectsByType<HazardZone>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var mr = hz.GetComponent<MeshRenderer>();
            if (mr != null) mr.enabled = false;
        }
        var fo = GameObject.Find("FloorObstacles");
        if (fo != null) fo.SetActive(false);
    }

    static Material GetOrCreateMat(string name, Color c)
    {
        string path = "Assets/_Materials/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(m, path);
        }
        m.SetColor("_BaseColor", c);
        return m;
    }

    // Mesure les bounds monde d'un modèle (tous renderers) à l'échelle native (localScale=1).
    static bool TryBounds(GameObject go, out Bounds b)
    {
        b = new Bounds(go.transform.position, Vector3.zero);
        bool has = false;
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            if (r is ParticleSystemRenderer) continue;
            if (!has) { b = r.bounds; has = true; } else b.Encapsulate(r.bounds);
        }
        return has;
    }

    // Facteur d'échelle uniforme pour que la plus grande dimension du modèle = target (mètres).
    static float FitFactor(GameObject go, float target)
    {
        go.transform.localScale = Vector3.one;   // mesurer à l'échelle native
        if (!TryBounds(go, out var b)) return 1f;
        float m = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
        return m < 1e-4f ? 1f : target / m;
    }
}
