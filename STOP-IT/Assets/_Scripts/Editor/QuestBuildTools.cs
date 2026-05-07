using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// STOP IT! — Quest 3 build helpers
/// One-click build / install / run pipeline so the team can iterate on the
/// headset before every demo without remembering switches.
///
/// Menu: Tools/STOP IT/Build Quest 3 (.apk)        — build only
///       Tools/STOP IT/Build & Run on Quest        — build, then deploy & launch via adb
///       Tools/STOP IT/Configure Android Settings  — flips PlayerSettings to Quest-friendly defaults
/// </summary>
public static class QuestBuildTools
{
    const string ProductName = "STOP IT XR";
    const string DefaultIdentifier = "com.epitech.stopitxr";
    static readonly string OutputDir = Path.Combine("Builds", "Quest");

    // ── Configuration ──────────────────────────────────────────────────────
    [MenuItem("Tools/STOP IT/Configure Android Settings", priority = 100)]
    public static void ConfigureAndroidSettings()
    {
        // Switch to Android target if needed
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
        {
            if (EditorUtility.DisplayDialog(
                    "Switch to Android",
                    "The active build target must be Android for Quest builds. Switch now? (this can take a while)",
                    "Switch", "Cancel"))
            {
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
            }
            else
            {
                return;
            }
        }

        // Mandatory Quest settings
        PlayerSettings.companyName = string.IsNullOrEmpty(PlayerSettings.companyName) ? "Epitech" : PlayerSettings.companyName;
        PlayerSettings.productName = ProductName;
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, DefaultIdentifier);

        // Architecture: ARM64 only (Quest stores reject ARMv7)
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);

        // SDK levels — Meta requires API 32 (Android 12L) min for Quest 3 multi-window features
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
        PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel32;

        // Use Vulkan first, fall back to OpenGL ES 3 (Meta recommended)
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[]
        {
            UnityEngine.Rendering.GraphicsDeviceType.Vulkan,
            UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3,
        });

        // Strip engine code aggressively — required for fast Quest startup
        PlayerSettings.stripEngineCode = true;

        // Keep "Run In Background" off — Quest pauses the app on dashboard
        PlayerSettings.runInBackground = false;

        EditorUtility.SetDirty(PlayerSettings.GetSerializedObject().targetObject);
        AssetDatabase.SaveAssets();
        Debug.Log("[STOP IT] Android settings configured for Quest 3 (ARM64 / IL2CPP / Vulkan).");
    }

    // ── Build ──────────────────────────────────────────────────────────────
    [MenuItem("Tools/STOP IT/Build Quest 3 (.apk)", priority = 110)]
    public static void BuildOnly()
    {
        Build(false);
    }

    [MenuItem("Tools/STOP IT/Build && Run on Quest", priority = 111)]
    public static void BuildAndRun()
    {
        Build(true);
    }

    private static void Build(bool runOnDevice)
    {
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
        {
            EditorUtility.DisplayDialog(
                "Wrong build target",
                "Active build target is " + EditorUserBuildSettings.activeBuildTarget +
                ".\nRun Tools/STOP IT/Configure Android Settings first.", "OK");
            return;
        }

        Directory.CreateDirectory(OutputDir);
        string apkName = $"{ProductName.Replace(' ', '_')}-{DateTime.Now:yyyyMMdd-HHmm}.apk";
        string fullPath = Path.Combine(OutputDir, apkName);

        var scenes = EditorBuildSettings.scenes
            .Where(s => s.enabled)
            .Select(s => s.path)
            .ToArray();

        if (scenes.Length == 0)
        {
            // Fall back to the currently open scene to avoid an empty build.
            var current = EditorSceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(current.path))
            {
                EditorUtility.DisplayDialog("No scene", "Add at least one scene to Build Settings, or open the LivingRoom scene.", "OK");
                return;
            }
            scenes = new[] { current.path };
            Debug.LogWarning("[STOP IT] No scenes in Build Settings — falling back to active scene: " + current.path);
        }

        var opts = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = fullPath,
            target = BuildTarget.Android,
            targetGroup = BuildTargetGroup.Android,
            options = runOnDevice ? BuildOptions.AutoRunPlayer : BuildOptions.None,
        };

        Debug.Log($"[STOP IT] Building APK → {fullPath}");
        BuildReport report = BuildPipeline.BuildPlayer(opts);
        var summary = report.summary;

        if (summary.result == BuildResult.Succeeded)
        {
            Debug.Log($"[STOP IT] Build OK ({summary.totalSize / (1024f * 1024f):F1} MB) in {summary.totalTime}.");
            EditorUtility.RevealInFinder(fullPath);
        }
        else
        {
            Debug.LogError($"[STOP IT] Build FAILED — {summary.totalErrors} errors.");
        }
    }

    // ── Sanity helpers ─────────────────────────────────────────────────────
    [MenuItem("Tools/STOP IT/Open Builds Folder", priority = 120)]
    public static void OpenBuildsFolder()
    {
        Directory.CreateDirectory(OutputDir);
        EditorUtility.RevealInFinder(Path.GetFullPath(OutputDir));
    }
}
