using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// STOP IT! — EnvironmentAutoSetup
/// Configures a soft gradient skybox + matching distance fog so the empty space
/// beyond the house (visible through the bedroom window, or from an elevated
/// menu-tour shot) fades cleanly into the sky instead of showing a flat void.
///
/// Runs automatically on compile (mirrors FloorObstacleAutoSetup) and is also
/// exposed as Tools/STOP IT/Setup Environment (Sky + Fog) for manual re-trigger —
/// idempotent either way.
/// </summary>
[InitializeOnLoad]
public static class EnvironmentAutoSetup
{
    private const string SkyboxMatPath = "Assets/_Materials/Mat_SkyboxGradient.mat";
    private const string ShaderName = "Skybox/SimpleGradient";

    private static readonly Color SkyColor = new Color(0.45f, 0.60f, 0.80f);
    private static readonly Color HorizonColor = new Color(0.72f, 0.78f, 0.85f);

    // Matches HorizonColor (== fogColor) on purpose: real terrain below the horizon fades into
    // fogColor at distance, so the skybox's own "ground" band must be the same color, otherwise
    // a visible seam/band appears between the fogged-out terrain and the skybox where they meet.
    private static readonly Color GroundColor = HorizonColor;

    private const float FogStart = 9f;
    private const float FogEnd = 20f;

    static EnvironmentAutoSetup()
    {
        EditorApplication.update += Poll;
    }

    private static void Poll()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded) return;

        EditorApplication.update -= Poll;

        if (!scene.name.Contains("LivingRoom")) return;
        if (RenderSettings.skybox != null && RenderSettings.skybox.name == "Mat_SkyboxGradient") return;

        Apply();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[EnvironmentAutoSetup] Skybox gradient + distance fog configured and scene saved.");
    }

    [MenuItem("Tools/STOP IT/Setup Environment (Sky + Fog)")]
    public static void SetupEnvironment()
    {
        Apply();
        if (!Application.isPlaying)
            EditorSceneManager.SaveOpenScenes();
        Debug.Log("[STOP IT] Skybox gradient + distance fog configured.");
    }

    private static void Apply()
    {
        RenderSettings.skybox = GetOrCreateSkyboxMaterial();

        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = HorizonColor;
        RenderSettings.fogStartDistance = FogStart;
        RenderSettings.fogEndDistance = FogEnd;

        DynamicGI.UpdateEnvironment();
    }

    private static Material GetOrCreateSkyboxMaterial()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(SkyboxMatPath);
        bool isNew = mat == null;

        if (isNew)
        {
            var shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                Debug.LogError($"[EnvironmentAutoSetup] Shader '{ShaderName}' not found — skybox not created.");
                return null;
            }
            mat = new Material(shader) { name = "Mat_SkyboxGradient" };
        }

        // Always re-push the tuned colors — re-running Tools > STOP IT > Setup Environment
        // must pick up constant tweaks made here, not just skip because the asset exists.
        mat.SetColor("_SkyColor", SkyColor);
        mat.SetColor("_HorizonColor", HorizonColor);
        mat.SetColor("_GroundColor", GroundColor);
        mat.SetFloat("_HorizonHeight", 0.4f);

        if (isNew) AssetDatabase.CreateAsset(mat, SkyboxMatPath);
        else EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();
        return mat;
    }
}
