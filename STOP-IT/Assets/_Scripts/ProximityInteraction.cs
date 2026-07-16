using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;

/// <summary>
/// STOP IT! — Proximity interaction support (shared infrastructure).
///
/// A scenario-specific "use this object when you're close" verb (take the cat,
/// close the window, …). The player triggers it with the SAME input that grabs
/// the baby — desktop "E", VR the 4 triggers held together — but unlike the baby
/// grab it is gated by proximity to the object instead of a hand overlap-sphere.
///
/// ChildGrabber.Trigger() walks the registry and gives each interactable a chance
/// to consume the press (closest-relevant one wins). This mirrors the WaterBottle
/// flow but keeps ChildGrabber decoupled from the concrete interactable types so
/// new scenarios can add their own verb without editing the grabber.
/// </summary>
public interface IProximityInteractable
{
    /// <summary>
    /// The player pressed the interact input. Return true if THIS object handled
    /// it (player was close enough and the action fired) — that stops the grabber
    /// from also trying to grab the baby.
    /// <paramref name="hand"/> is the active hand/controller transform that fired
    /// the press (the ChildGrabber's own transform) — an object can attach itself
    /// to it to ride in the player's hand. May be ignored by verbs that don't carry.
    /// </summary>
    bool TryInteract(Vector3 cameraPosition, Transform hand);
}

/// <summary>
/// Tiny runtime registry of the active <see cref="IProximityInteractable"/>s.
/// Interactables add themselves in OnEnable and remove themselves in OnDisable,
/// so ChildGrabber never has to FindObjectsByType per press.
/// </summary>
public static class ProximityInteractables
{
    private static readonly List<IProximityInteractable> _list = new();

    public static void Register(IProximityInteractable it)
    {
        if (it != null && !_list.Contains(it)) _list.Add(it);
    }

    public static void Unregister(IProximityInteractable it)
    {
        _list.Remove(it);
    }

    public static IReadOnlyList<IProximityInteractable> All => _list;
}

/// <summary>
/// Active-input awareness shared by every prompt. The single source of truth for
/// "which key do I tell the player to press" so the cat / window / bottle prompts
/// never disagree.
/// </summary>
public static class InputHints
{
    /// <summary>True when an opaque XR headset is actively rendering.</summary>
    public static bool IsVRActive()
    {
        var displays = new List<UnityEngine.XR.XRDisplaySubsystem>();
        SubsystemManager.GetSubsystems(displays);
        foreach (var d in displays)
            if (d != null && d.running && d.displayOpaque) return true;
        return false;
    }

    /// <summary>
    /// Label for the BABY GRAB input (4 triggers in VR, E on desktop).
    /// Used by grab prompts ("attrape le bébé").
    /// </summary>
    public static string ActionKeyLabel => IsVRActive() ? "Presse les 4 gâchettes" : "E";

    /// <summary>
    /// Label for SCENARIO VERBS (2 triggers one hand in VR, E on desktop).
    /// Used by cat / bottle / window prompts.
    /// </summary>
    public static string VerbKeyLabel => IsVRActive() ? "Presse les 2 gâchettes d'une main" : "E";

    /// <summary>Substitute {KEY} with the baby-grab label.</summary>
    public static string ResolvePrompt(string raw) => ResolveWith(raw, ActionKeyLabel);

    /// <summary>Substitute {KEY} with the scenario-verb label (2 triggers one hand).</summary>
    public static string ResolveVerbPrompt(string raw) => ResolveWith(raw, VerbKeyLabel);

    private static string ResolveWith(string raw, string key)
    {
        if (string.IsNullOrEmpty(raw)) return raw;
        if (raw.Contains("{KEY}")) return raw.Replace("{KEY}", key);
        if (key != "E" && raw.StartsWith("E ")) return key + raw.Substring(1);
        return raw;
    }
}

/// <summary>
/// Builds and positions a floating world-space text prompt — the same look the
/// WaterBottle uses, factored out so the cat and window verbs share it.
/// </summary>
public static class ProximityPrompt
{
    /// <summary>Create a hidden world-space prompt GameObject showing <paramref name="text"/>.</summary>
    public static GameObject Build(string name, string text)
    {
        var go = new GameObject(name);
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        // Tri au-dessus des autres canvas world-space ; le vrai anti-occlusion vient
        // du ZTest Always sur les matériaux (voir plus bas).
        canvas.sortingOrder = 32000;
        var rt = (RectTransform)go.transform;
        // Wider/taller than the original bottle prompt so the longer VR label
        // ("Presse les 4 gâchettes pour …") fits without clipping.
        rt.sizeDelta = new Vector2(560, 160);
        rt.localScale = Vector3.one * 0.003f;

        // Panneau de fond semi-transparent pour le contraste (rendu DERRIÈRE le texte
        // car premier enfant → dessiné en premier).
        var bgGO = new GameObject("Background");
        bgGO.transform.SetParent(go.transform, false);
        var bg = bgGO.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.72f);
        bg.raycastTarget = false;
        bg.material = OverlayUIMaterial(); // toujours au-dessus de la géométrie de la maison
        var brt = (RectTransform)bgGO.transform;
        brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one;
        // Léger padding négatif pour que le fond déborde un peu autour du texte.
        brt.offsetMin = new Vector2(-16f, -12f); brt.offsetMax = new Vector2(16f, 12f);

        var tmpGO = new GameObject("Text");
        tmpGO.transform.SetParent(go.transform, false);
        var tmp = tmpGO.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = 40;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = new Color(0.2f, 0.95f, 1f);
        tmp.fontStyle = FontStyles.Bold;
        tmp.raycastTarget = false;
        // Contour noir pour détacher le texte du décor. Accéder à outlineWidth crée
        // un matériau instancié (fontMaterial) propre à ce prompt.
        tmp.outlineColor = new Color32(0, 0, 0, 255);
        tmp.outlineWidth = 0.22f;
        var trt = (RectTransform)tmpGO.transform;
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
        // ZTest Always sur le matériau instancié du texte → jamais masqué par les murs.
        // fontMaterial est une copie propre à ce TMP, donc les autres textes ne bougent pas.
        if (tmp.fontMaterial != null)
            tmp.fontMaterial.SetFloat(_zTestProp, (float)CompareFunction.Always);

        go.SetActive(false);
        return go;
    }

    /// <summary>Nom de la propriété ZTest du shader TMP distance field.</summary>
    private static readonly int _zTestProp = Shader.PropertyToID("_ZTestMode");

    /// <summary>
    /// Matériau UI toujours-au-dessus (ZTest Always) pour les panneaux de fond,
    /// partagé par tous les prompts. Le motif <c>unity_GUIZTestMode</c> est la
    /// propriété que lit <c>ZTest [unity_GUIZTestMode]</c> du shader UI/Default.
    /// </summary>
    private static Material _overlayUIMat;
    private static Material OverlayUIMaterial()
    {
        if (_overlayUIMat == null)
        {
            var sh = Shader.Find("UI/Default");
            if (sh != null)
            {
                _overlayUIMat = new Material(sh) { name = "PromptOverlayUI" };
                _overlayUIMat.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
            }
        }
        return _overlayUIMat;
    }

    /// <summary>
    /// Place le prompt à <paramref name="worldPos"/>, le TIRE vers la caméra le long
    /// du vecteur horizontal aplati (pour qu'il passe DEVANT les murs / niches au lieu
    /// de s'y encastrer), puis le redresse face aux yeux (billboard en lacet).
    /// </summary>
    public static void Face(GameObject prompt, Vector3 worldPos, Vector3 camPos)
    {
        if (prompt == null) return;
        Vector3 toCamFlat = camPos - worldPos; toCamFlat.y = 0f;
        Vector3 pos = worldPos;
        if (toCamFlat.sqrMagnitude > 0.04f) pos += toCamFlat.normalized * 0.45f;
        prompt.transform.position = pos;
        Vector3 face = pos - camPos; face.y = 0f;
        if (face.sqrMagnitude > 0.001f)
            prompt.transform.rotation = Quaternion.LookRotation(face, Vector3.up);
    }
}
