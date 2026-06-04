using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Outil de dev : rend quelques vues de la scène vers des PNG (hors dossier Assets pour éviter
/// l'import) afin de vérifier visuellement l'habillage. Menu : Tools/STOP IT/Capture Views.
/// </summary>
public static class ArtCapture
{
    struct View { public string name; public Vector3 pos, look; public float fov;
        public View(string n, Vector3 p, Vector3 l, float f){name=n;pos=p;look=l;fov=f;} }

    static View[] VIEWS() => new[]
    {
        new View("01_salon",    new Vector3(-1.5f, 2.0f,  0.5f), new Vector3(-5.5f, 0.6f, 4.5f), 65f),
        new View("02_kitchen",  new Vector3( 4.0f, 2.0f,  2.5f), new Vector3( 4.5f, 0.9f, 5.6f), 65f),
        new View("03_bathroom", new Vector3(-2.0f, 2.0f, -2.0f), new Vector3(-5.5f, 0.6f,-5.0f), 65f),
        new View("04_stairs",   new Vector3( 2.5f, 2.2f, -2.5f), new Vector3( 5.0f, 1.0f,-5.0f), 70f),
        new View("05_bedroom",  new Vector3( 1.0f, 4.6f,  0.0f), new Vector3( 5.0f, 3.4f, 1.5f), 70f),
        new View("06_overview", new Vector3(-6.5f, 5.5f, -5.5f), new Vector3( 1.0f, 1.0f, 1.0f), 70f),
        new View("07_npc",      new Vector3(-1.5f, 1.4f,  0.0f), new Vector3( 2.5f, 0.5f, 0.0f), 50f),
    };

    [MenuItem("Tools/STOP IT/Capture Views")]
    public static void CaptureViews()
    {
        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "ArtCaptures"));
        Directory.CreateDirectory(dir);

        var camGO = new GameObject("__CaptureCam");
        var cam = camGO.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 60f;
        cam.allowMSAA = true;

        const int W = 1280, H = 720;
        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);

        var sb = new System.Text.StringBuilder("[CAPTURE] Vues rendues:\n");
        foreach (var v in VIEWS())
        {
            cam.transform.position = v.pos;
            cam.transform.rotation = Quaternion.LookRotation((v.look - v.pos).normalized, Vector3.up);
            cam.fieldOfView = v.fov;
            cam.targetTexture = rt;
            cam.Render();

            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = null;

            string path = Path.Combine(dir, v.name + ".png");
            File.WriteAllBytes(path, tex.EncodeToPNG());
            sb.AppendLine("  " + path);
        }

        cam.targetTexture = null;
        Object.DestroyImmediate(camGO);
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(tex);

        Debug.Log(sb.ToString());
    }
}
