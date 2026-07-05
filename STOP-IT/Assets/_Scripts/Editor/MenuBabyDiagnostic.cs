using UnityEditor;
using UnityEngine;

/// <summary>
/// TEMPORARY diagnostic — logs how many MenuRoamingNPC "strollers" and gameplay
/// ChildNPC objects exist during Play, and re-logs ONLY when a count changes, so any
/// runtime "re-spawn" (the reported menu baby army) shows up as a climbing number.
/// Runs editor-side (no scene changes). Delete this file once the menu bug is settled.
/// </summary>
[InitializeOnLoad]
public static class MenuBabyDiagnostic
{
    private static int _lastRoamers = -1;
    private static int _lastChildren = -1;
    private static double _next;

    static MenuBabyDiagnostic()
    {
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += s =>
        {
            if (s == PlayModeStateChange.EnteredPlayMode) { _lastRoamers = _lastChildren = -1; }
        };
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        if (EditorApplication.timeSinceStartup < _next) return;
        _next = EditorApplication.timeSinceStartup + 0.5; // sample twice a second

        var roamers  = Object.FindObjectsByType<MenuRoamingNPC>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var children = Object.FindObjectsByType<ChildNPC>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        if (roamers.Length == _lastRoamers && children.Length == _lastChildren) return; // no change — stay quiet
        _lastRoamers = roamers.Length;
        _lastChildren = children.Length;

        // How many of those are actually rendered right now (an on-screen "baby").
        int visibleChildren = 0;
        foreach (var c in children)
        {
            if (c == null) continue;
            foreach (var r in c.GetComponentsInChildren<Renderer>(true))
                if (r != null && r.enabled && r.gameObject.activeInHierarchy) { visibleChildren++; break; }
        }

        string state = GameManager.Instance != null ? GameManager.Instance.State.ToString() : "<no GameManager>";
        Debug.Log($"[BabyDiag] MenuRoamingNPC(strollers)={roamers.Length}  ChildNPC={children.Length} " +
                  $"(visible={visibleChildren})  GameState={state}");
    }
}
