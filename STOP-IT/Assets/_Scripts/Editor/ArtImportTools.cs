using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Helper one-shot pour importer les .unitypackage d'art (Synty POLYGON) depuis le dossier
/// d'assets local, de façon pilotable via MCP (execute_menu_item).
/// Outil de dev — peut être supprimé une fois l'art importé.
/// </summary>
public static class ArtImportTools
{
    const string AssetsDir = @"C:\Users\fourq\Documents\VR-Domicile-Risk\Assets";
    const string TownPkg   = AssetsDir + @"\Polygon_Town_URP.unitypackage";
    const string OfficePkg = AssetsDir + @"\Polygon_Office_URP.unitypackage";

    [MenuItem("Tools/STOP IT/Import Art Packages")]
    public static void ImportArtPackages()
    {
        ImportOne(TownPkg, "POLYGON Town URP");
        ImportOne(OfficePkg, "POLYGON Office URP");
        Debug.Log("[STOP IT] Import des packs d'art lancé. Patientez la fin du traitement Unity.");
    }

    static void ImportOne(string path, string label)
    {
        if (!File.Exists(path))
        {
            Debug.LogError($"[STOP IT] Package introuvable: {path}");
            return;
        }
        Debug.Log($"[STOP IT] Import: {label} <- {path}");
#pragma warning disable 0618
        AssetDatabase.ImportPackage(path, false); // false = silencieux (non interactif)
#pragma warning restore 0618
    }
}
