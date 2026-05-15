using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class FloorObstacleAutoSetup
{
    static FloorObstacleAutoSetup()
    {
        EditorApplication.update += Poll;
    }

    private static void Poll()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded) return;

        EditorApplication.update -= Poll;

        if (!scene.name.Contains("LivingRoom")) return;
        if (GameObject.Find("FloorObstacles") != null) return;

        SpawnIntoScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[FloorObstacleAutoSetup] Dalles violettes ajoutees et scene sauvegardee.");
    }

    public static void SpawnIntoScene()
    {
        var root = new GameObject("FloorObstacles");
        Undo.RegisterCreatedObjectUndo(root, "FloorObstacles");

        Vector3 s  = new Vector3(1.2f, 0.08f, 1.2f);
        Vector3 sH = new Vector3(1.2f, 0.08f, 1.8f);

        // Salon (X -7..0, Z 1..6)
        Add(root, "Obs_Salon_1",   new Vector3(-4.0f, 0.04f,  2.5f), s,  0.4f);
        Add(root, "Obs_Salon_2",   new Vector3(-2.5f, 0.04f,  3.8f), s,  0.4f);
        Add(root, "Obs_Salon_3",   new Vector3(-5.5f, 0.04f,  4.5f), s,  0.4f);

        // Cuisine (X 0..7, Z 1..6)
        Add(root, "Obs_Kitchen_1", new Vector3( 1.5f, 0.04f,  2.0f), s,  0.4f);
        Add(root, "Obs_Kitchen_2", new Vector3( 5.0f, 0.04f,  3.0f), s,  0.4f);
        Add(root, "Obs_Kitchen_3", new Vector3( 2.5f, 0.04f,  4.5f), s,  0.4f);

        // Couloir (X -7..7, Z -1..1)
        Add(root, "Obs_Hall_1",    new Vector3(-2.5f, 0.04f,  0.0f), sH, 0.5f);
        Add(root, "Obs_Hall_2",    new Vector3( 3.0f, 0.04f,  0.0f), sH, 0.5f);

        // Salle de bain (X -7..0, Z -6..-1)
        Add(root, "Obs_Bath_1",    new Vector3(-3.0f, 0.04f, -2.5f), s,  0.4f);
        Add(root, "Obs_Bath_2",    new Vector3(-5.5f, 0.04f, -3.5f), s,  0.4f);

        // Escalier (X 0..7, Z -6..-1, marches X 4..6)
        Add(root, "Obs_Stairs_1",  new Vector3( 1.5f, 0.04f, -2.5f), s,  0.4f);
        Add(root, "Obs_Stairs_2",  new Vector3( 2.5f, 0.04f, -4.0f), s,  0.4f);

        // Chambre 1er etage (X 0..7, Z -6..6, Y=3)
        Add(root, "Obs_Bedroom_1", new Vector3( 2.0f, 3.04f,  0.5f), s,  0.4f);
        Add(root, "Obs_Bedroom_2", new Vector3( 5.0f, 3.04f, -2.5f), s,  0.4f);
        Add(root, "Obs_Bedroom_3", new Vector3( 2.5f, 3.04f,  3.0f), s,  0.4f);
    }

    public static void Add(GameObject parent, string name, Vector3 pos, Vector3 size, float mult)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent.transform, false);
        go.transform.position = pos;
        go.transform.localScale = size;

        var col = go.GetComponent<BoxCollider>();
        if (col != null) col.isTrigger = true;

        var obs = go.AddComponent<FloorObstacle>();
        obs.size            = size;
        obs.speedMultiplier = mult;

        var mr = go.GetComponent<MeshRenderer>();
        if (mr != null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            if (shader != null)
            {
                var mat = new Material(shader);
                mat.name  = "Mat_FloorObstacle_Placeholder";
                mat.color = new Color(0.55f, 0f, 1f);
                mr.sharedMaterial = mat;
            }
        }

        Undo.RegisterCreatedObjectUndo(go, name);
    }
}
