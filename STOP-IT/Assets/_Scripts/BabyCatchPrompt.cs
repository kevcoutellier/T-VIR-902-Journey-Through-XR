using UnityEngine;

/// <summary>
/// STOP IT! — BabyCatchPrompt
/// Floating "{KEY} pour attraper" prompt above the child, shown only when the player is
/// close enough to actually grab them — used by the catch scenarios (S1 fork, S4 skateboard).
///
/// Dual-input via <see cref="InputHints"/>: desktop shows "E", VR shows the 4-trigger gesture.
/// Reuses the shared <see cref="ProximityPrompt"/> world-space billboard. Attach to the
/// ChildNPC (it also works anywhere — it finds the child at Start).
/// </summary>
public class BabyCatchPrompt : MonoBehaviour
{
    [Tooltip("Show the prompt when the camera is within this distance of the child (metres).")]
    public float showDistance = 2.0f;
    [Tooltip("Height above the child's origin where the prompt floats (metres).")]
    public float heightOffset = 1.1f;
    [Tooltip("Prompt text. {KEY} is replaced by the active input ('E' / 'Presse les 4 gâchettes').")]
    public string promptText = "Maintiens {KEY} pour attraper le bébé";

    private ChildNPC _child;
    private GameObject _prompt;

    private void Start()
    {
        _child = GetComponent<ChildNPC>();
        if (_child == null) _child = FindAnyObjectByType<ChildNPC>();
        _prompt = ProximityPrompt.Build("BabyCatchPrompt", InputHints.ResolvePrompt(promptText));
    }

    private void OnDestroy()
    {
        if (_prompt != null) Destroy(_prompt);
    }

    private void LateUpdate()
    {
        if (_prompt == null) return;

        var cam = Camera.main;
        bool show = false;

        if (cam != null && _child != null
            && GameManager.Instance != null
            && GameManager.Instance.State == GameManager.GameState.Playing
            && _child.canBeSavedDirectly && !_child.IsHeld)
        {
            Vector3 childPos = _child.transform.position;
            if (Vector3.Distance(cam.transform.position, childPos) <= showDistance)
            {
                show = true;
                ProximityPrompt.Face(_prompt, childPos + Vector3.up * heightOffset, cam.transform.position);
            }
        }

        if (_prompt.activeSelf != show) _prompt.SetActive(show);
    }
}
