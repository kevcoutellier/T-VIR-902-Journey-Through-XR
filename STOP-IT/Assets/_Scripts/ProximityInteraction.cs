using System.Collections.Generic;
using TMPro;
using UnityEngine;

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
    /// </summary>
    bool TryInteract(Vector3 cameraPosition);
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
    /// The label to drop into a "{KEY} pour …" prompt. Desktop = "E"; VR = the
    /// 4-trigger grab gesture (both index triggers + both grips held together).
    /// </summary>
    public static string ActionKeyLabel => IsVRActive() ? "Presse les 4 gâchettes" : "E";

    /// <summary>Substitute {KEY} (or a legacy leading "E ") for the active input label.</summary>
    public static string ResolvePrompt(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return raw;
        string key = ActionKeyLabel;
        if (raw.Contains("{KEY}")) return raw.Replace("{KEY}", key);
        // Backwards compatibility with assets saved before the placeholder existed.
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
        var rt = (RectTransform)go.transform;
        // Wider/taller than the original bottle prompt so the longer VR label
        // ("Presse les 4 gâchettes pour …") fits without clipping.
        rt.sizeDelta = new Vector2(560, 160);
        rt.localScale = Vector3.one * 0.003f;

        var tmpGO = new GameObject("Text");
        tmpGO.transform.SetParent(go.transform, false);
        var tmp = tmpGO.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = 40;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = new Color(0.2f, 0.95f, 1f);
        tmp.fontStyle = FontStyles.Bold;
        var trt = (RectTransform)tmpGO.transform;
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;

        go.SetActive(false);
        return go;
    }

    /// <summary>Move the prompt to <paramref name="worldPos"/> and billboard it toward the camera.</summary>
    public static void Face(GameObject prompt, Vector3 worldPos, Vector3 camPos)
    {
        if (prompt == null) return;
        prompt.transform.position = worldPos;
        Vector3 toCam = worldPos - camPos;
        toCam.y = 0f;
        if (toCam.sqrMagnitude > 0.001f)
            prompt.transform.rotation = Quaternion.LookRotation(toCam, Vector3.up);
    }
}
