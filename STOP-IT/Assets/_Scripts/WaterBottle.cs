using TMPro;
using UnityEngine;

/// <summary>
/// STOP IT! — WaterBottle
/// Bathroom scenario interactable. Two-press flow:
///   1. Approach the bottle on the back cabinet, press E (or grip) → bottle vanishes
///      from the world (the player is "carrying" it), pickup prompt hides, swap
///      prompt appears above the cleaning-product hazard.
///   2. Approach the hazard (or stay anywhere — the swap prompt is always visible
///      once the bottle is held) and press E again → swap fires, scenario success.
///
/// The bottle is NOT parented to the player's hand: keeping it as a free root
/// object lets us survive the temporary-GO grab-hand lifecycle of DesktopTestRig
/// (which destroys its hand on E release). Visually we just hide the mesh.
/// </summary>
public class WaterBottle : MonoBehaviour
{
    [Header("Indicator")]
    [Tooltip("Optional 'grab me' visual hint (e.g. floating chevron). Hidden while held.")]
    public GameObject pickupIndicator;

    [Header("Swap")]
    [Tooltip("Target hazard zone name to match for the swap (e.g. 'HazardZone_CleaningProduct').")]
    public string targetHazardName = "HazardZone_CleaningProduct";
    [Tooltip("Reference to the cleaning-product hazard zone (auto-found by name if null).")]
    public HazardZone targetHazardZone;
    [Tooltip("Visual GameObject for the cleaning product bottle (hidden on swap; auto-found by name 'CleaningBottle' if null).")]
    public GameObject cleaningProductVisual;

    [Header("Interaction")]
    [Tooltip("Distance (metres) from the player camera at which pickup is allowed.")]
    public float interactionRadius = 1.5f;

    [Header("Prompts")]
    public string pickupPromptText = "E pour récupérer l'eau";
    public string swapPromptText = "E pour remplacer";
    [Tooltip("Local offset from the anchor (bottle / hazard zone) for the prompt.")]
    public Vector3 promptOffset = new Vector3(0f, 0.4f, 0f);

    private bool _held;
    private Vector3 _initialPos;
    private Quaternion _initialRot;
    private Collider _col;
    private Rigidbody _rb;
    private Renderer[] _renderers;
    private Renderer[] _hazardRenderers;
    private GameObject _pickupPrompt;
    private GameObject _swapPrompt;

    public bool IsHeld => _held;

    private void Awake()
    {
        _initialPos = transform.position;
        _initialRot = transform.rotation;
        _col = GetComponent<Collider>();
        _rb = GetComponent<Rigidbody>();
        _renderers = GetComponentsInChildren<Renderer>(includeInactive: true);
    }

    private void Start()
    {
        if (targetHazardZone == null)
        {
            var go = GameObject.Find(targetHazardName);
            if (go != null) targetHazardZone = go.GetComponent<HazardZone>();
        }
        if (cleaningProductVisual == null)
        {
            var go = GameObject.Find("CleaningBottle");
            if (go != null) cleaningProductVisual = go;
        }
        if (targetHazardZone != null)
            _hazardRenderers = targetHazardZone.GetComponentsInChildren<Renderer>(includeInactive: true);
        _pickupPrompt = BuildPrompt("WaterBottle_PickupPrompt", pickupPromptText);
        _swapPrompt = BuildPrompt("WaterBottle_SwapPrompt", swapPromptText);

        // Self-reset on every Playing state transition. This is the fail-safe path
        // in case the ScenarioManager config isn't wired to call ResetBottle:
        // a player hitting "Recommencer" after the swap MUST find the bottle back
        // on the cabinet, otherwise the scenario is unplayable on retry.
        if (GameManager.Instance != null)
            GameManager.Instance.OnStateChanged.AddListener(OnGameStateChanged);
    }

    private void OnDestroy()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnStateChanged.RemoveListener(OnGameStateChanged);
    }

    private void OnGameStateChanged(GameManager.GameState state)
    {
        if (state == GameManager.GameState.Playing) ResetBottle();
    }

    private GameObject BuildPrompt(string name, string text)
    {
        var go = new GameObject(name);
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var rt = (RectTransform)go.transform;
        rt.sizeDelta = new Vector2(400, 80);
        rt.localScale = Vector3.one * 0.003f;

        var tmpGO = new GameObject("Text");
        tmpGO.transform.SetParent(go.transform, false);
        var tmp = tmpGO.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = 48;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = new Color(0.2f, 0.95f, 1f);
        tmp.fontStyle = FontStyles.Bold;
        var trt = (RectTransform)tmpGO.transform;
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;

        go.SetActive(false);
        return go;
    }

    private void Update()
    {
        var cam = Camera.main;
        if (cam == null) return;
        Vector3 camPos = cam.transform.position;

        // Pickup prompt: visible only when the bottle is still on the table and the player is close.
        if (_pickupPrompt != null)
        {
            bool show = !_held && Vector3.Distance(camPos, transform.position) < interactionRadius;
            _pickupPrompt.SetActive(show);
            if (show) FacePrompt(_pickupPrompt, transform.position + promptOffset, camPos);
        }

        // Swap prompt: visible AS SOON AS the bottle is held — no distance gate.
        if (_swapPrompt != null && targetHazardZone != null)
        {
            _swapPrompt.SetActive(_held);
            if (_held) FacePrompt(_swapPrompt, targetHazardZone.transform.position + promptOffset, camPos);
        }
    }

    private static void FacePrompt(GameObject prompt, Vector3 worldPos, Vector3 camPos)
    {
        prompt.transform.position = worldPos;
        Vector3 toCam = worldPos - camPos;
        toCam.y = 0f;
        if (toCam.sqrMagnitude > 0.001f)
            prompt.transform.rotation = Quaternion.LookRotation(toCam, Vector3.up);
    }

    /// <summary>
    /// Pick up the bottle. We don't parent it to a hand — the bottle is "in inventory"
    /// and visually hidden. Returns true if the pickup happened.
    /// </summary>
    public bool TryPickup(Transform _ /* hand unused */)
    {
        if (_held) return false;
        _held = true;
        SetVisible(false);
        if (_col != null) _col.enabled = false;
        if (pickupIndicator != null) pickupIndicator.SetActive(false);
        if (_pickupPrompt != null) _pickupPrompt.SetActive(false);
        return true;
    }

    /// <summary>
    /// Drop the bottle on the cleaning-product hazard zone. Hides the cleaning
    /// products (visual bottle + hazard cube renderer) and marks the zone neutralised.
    /// Does NOT report success — that fires when the toddler reaches the (harmless)
    /// zone, preserving the dramatic timing of the chase.
    /// </summary>
    public bool TryDropAt(HazardZone zone)
    {
        if (!_held || zone == null) return false;
        if (!zone.gameObject.name.Equals(targetHazardName, System.StringComparison.OrdinalIgnoreCase))
            return false;

        _held = false;
        Vector3 dropPos = zone.transform.position + Vector3.up * 0.15f;
        transform.position = dropPos;
        transform.rotation = Quaternion.identity;
        SetVisible(true);
        if (_col != null) _col.enabled = true;
        if (_swapPrompt != null) _swapPrompt.SetActive(false);

        // Hide the cleaning products so the swap reads visually.
        SetCleaningProductsVisible(false);

        // Mark the hazard as neutralised: when the toddler arrives, TriggerHazard
        // will route to a "win" path instead of a "fail" flash.
        zone.MarkNeutralised();
        return true;
    }

    /// <summary>Reset to initial pose. Called by ScenarioManager when (re)starting the bathroom scenario.</summary>
    public void ResetBottle()
    {
        _held = false;
        transform.position = _initialPos;
        transform.rotation = _initialRot;
        SetVisible(true);
        if (_col != null) _col.enabled = true;
        if (_rb != null) { _rb.isKinematic = true; _rb.useGravity = false; }
        if (pickupIndicator != null) pickupIndicator.SetActive(true);
        if (_pickupPrompt != null) _pickupPrompt.SetActive(false);
        if (_swapPrompt != null) _swapPrompt.SetActive(false);
        SetCleaningProductsVisible(true);
    }

    private void SetCleaningProductsVisible(bool on)
    {
        if (cleaningProductVisual != null) cleaningProductVisual.SetActive(on);
        if (_hazardRenderers != null)
            foreach (var r in _hazardRenderers)
                if (r != null) r.enabled = on;
    }

    private void SetVisible(bool on)
    {
        if (_renderers == null) return;
        for (int i = 0; i < _renderers.Length; i++)
            if (_renderers[i] != null) _renderers[i].enabled = on;
    }

    private void OnDisable()
    {
        if (_pickupPrompt != null) _pickupPrompt.SetActive(false);
        if (_swapPrompt != null) _swapPrompt.SetActive(false);
    }
}
