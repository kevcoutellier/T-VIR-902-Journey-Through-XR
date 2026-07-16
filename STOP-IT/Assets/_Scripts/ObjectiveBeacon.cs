using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

/// <summary>
/// STOP IT! — ObjectiveBeacon
/// Indice directionnel "va ici" pour le joueur VR (et desktop) qui ne sait pas OÙ aller.
/// Deux repères, tous deux TOUJOURS-AU-DESSUS de la géométrie (ZTest Always, comme
/// ProximityPrompt / DangerVignette) pour rester visibles à travers les murs de la maison :
///
///   • MARQUEUR MONDE : un chevron pointant vers la cible, flottant au-dessus d'elle,
///     billboardé face au joueur et redimensionné avec la distance (taille angulaire
///     constante → lisible depuis une autre pièce). C'est le "c'est ici".
///   • FLÈCHE TÊTE : une flèche verrouillée en bas du champ de vision (enfant de la caméra,
///     rigide = confortable en VR) qui ne s'affiche QUE lorsque la cible est hors-champ /
///     derrière le joueur, et pivote pour pointer vers elle. C'est le "tourne par là".
///
/// La cible provient de l'API PUBLIQUE existante de ScenarioManager (aucune modif de sa part) :
/// on privilégie l'objet interactif/ramassé/porté (pickupItem / carriedItem) sinon la hazardZone.
/// Actif uniquement pendant Playing ; masqué hors Playing ou quand la cible est
/// consommée/neutralisée (chat récupéré, fenêtre fermée → HazardZone.IsNeutralised).
///
/// Auto-bootstrap via RuntimeInitializeOnLoadMethod (comme VRUIWorldSpace / MenuControlsCard) :
/// aucun câblage de scène requis. Aucun collider, jamais de raycastTarget → ne bloque ni
/// l'input ni le gameplay. Fonctionne en VR et desktop (Camera.main gardée).
/// </summary>
[DefaultExecutionOrder(60)] // après VRUIWorldSpace (50) qui force la caméra XR
public class ObjectiveBeacon : MonoBehaviour
{
    [Header("Marqueur monde (au-dessus de la cible)")]
    [Tooltip("Hauteur du chevron au-dessus de la cible (m).")]
    public float markerHeight = 0.9f;
    [Tooltip("Taille apparente du marqueur par mètre de distance (taille angulaire ~constante).")]
    public float markerSizePerMeter = 0.08f;
    [Tooltip("Distance min/max prise en compte pour la mise à l'échelle du marqueur (m).")]
    public float markerMinDist = 1.5f, markerMaxDist = 12f;

    [Header("Flèche tête (hors-champ)")]
    [Tooltip("Distance de la flèche verrouillée devant les yeux (m).")]
    public float arrowDistance = 1.2f;
    [Tooltip("Abaissement de la flèche sous le centre du regard (m).")]
    public float arrowDrop = 0.42f;
    [Tooltip("Marge de viewport (0..0.5) : au-delà, la cible est considérée hors-champ.")]
    public float offscreenMargin = 0.12f;

    [Header("Couleur")]
    public Color beaconColor = new Color(0.2f, 0.95f, 1f, 0.9f);

    // ── Runtime ──────────────────────────────────────────────────────────────
    private static bool _spawned;

    private Camera _cam;
    private Transform _camParent;      // caméra à laquelle la flèche est parentée

    private Transform _marker;         // canvas monde (chevron au-dessus de la cible)
    private Image _markerImg;
    private Transform _arrow;          // canvas tête (flèche directionnelle)
    private Image _arrowImg;
    private RectTransform _arrowGfx;   // l'image pivotante à l'intérieur du canvas tête

    private Sprite _triangle;          // sprite flèche triangulaire (pointe vers le HAUT)
    private Material _overlayUI;       // matériau UI ZTest Always partagé

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (_spawned) return;
        _spawned = true;
        var go = new GameObject("Objective Beacon");
        go.AddComponent<ObjectiveBeacon>();
    }

    private void Awake()
    {
        _triangle = BuildTriangleSprite();
        _overlayUI = BuildOverlayUIMaterial();
        BuildMarker();
        BuildArrow();
        SetVisible(false);
    }

    // Positionnement dans LateUpdate : la pose HMD/caméra est déjà appliquée par le
    // TrackedPoseDriver → le marqueur billboardé et la flèche verrouillée collent à la vue.
    private void LateUpdate()
    {
        var cam = ResolveCamera();
        if (cam == null) { SetVisible(false); return; }

        bool playing = GameManager.Instance != null
                    && GameManager.Instance.State == GameManager.GameState.Playing;
        if (!playing) { SetVisible(false); return; }

        Transform target = ResolveTarget(out bool neutralised);
        if (target == null || neutralised) { SetVisible(false); return; }

        EnsureArrowParent(cam);

        Vector3 camPos = cam.transform.position;
        Vector3 markerPos = target.position + Vector3.up * markerHeight;

        // ── Marqueur monde : au-dessus de la cible, billboardé, taille angulaire constante ──
        _marker.gameObject.SetActive(true);
        _marker.position = markerPos;
        // Billboard en lacet (reste droit et lisible) — même logique que ScenarioUI/HazardIndicator.
        Vector3 face = markerPos - camPos; face.y = 0f;
        if (face.sqrMagnitude > 1e-4f)
            _marker.rotation = Quaternion.LookRotation(face, Vector3.up);
        float dist = Mathf.Clamp(Vector3.Distance(markerPos, camPos), markerMinDist, markerMaxDist);
        float pulse = 1f + 0.12f * Mathf.Sin(Time.time * 4f);
        // sizeDelta du canvas = 100 → taille monde = 100 * scale.
        _marker.localScale = Vector3.one * (markerSizePerMeter * dist * pulse * 0.01f);

        // ── Flèche tête : seulement si la cible est hors-champ ──
        Vector3 local = cam.transform.InverseTransformPoint(target.position); // x=droite, y=haut, z=avant
        bool behind = local.z <= 0.01f;
        bool offscreen = behind;
        if (!behind)
        {
            Vector3 vp = cam.WorldToViewportPoint(target.position);
            offscreen = vp.x < offscreenMargin || vp.x > 1f - offscreenMargin
                     || vp.y < offscreenMargin || vp.y > 1f - offscreenMargin;
        }

        if (offscreen)
        {
            _arrow.gameObject.SetActive(true);
            // Direction écran de la cible (plan XY caméra-local). Derrière → pointe latéralement.
            Vector2 dir = new Vector2(local.x, local.y);
            if (behind) dir = new Vector2(local.x >= 0f ? 1f : -1f, -0.15f);
            if (dir.sqrMagnitude < 1e-6f) dir = Vector2.down;
            // Le sprite pointe vers le HAUT (+Y) → offset de -90° pour l'aligner sur 'dir'.
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f;
            _arrowGfx.localRotation = Quaternion.Euler(0f, 0f, angle);
        }
        else
        {
            _arrow.gameObject.SetActive(false);
        }
    }

    // ── Résolution caméra / cible ──────────────────────────────────────────────
    private Camera ResolveCamera()
    {
        if (_cam != null && _cam.isActiveAndEnabled) return _cam;
        _cam = Camera.main;
        return _cam;
    }

    /// <summary>
    /// Cible = objet interactif/ramassé/porté du scénario courant si présent
    /// (pickupItem → carriedItem), sinon la hazardZone. <paramref name="neutralised"/>
    /// vaut true dès que le danger est neutralisé (chat récupéré, fenêtre fermée, échange SdB).
    /// </summary>
    private Transform ResolveTarget(out bool neutralised)
    {
        neutralised = false;
        var sm = ScenarioManager.Instance;
        var cfg = sm != null ? sm.CurrentScenario : null;
        if (cfg == null) return null;

        HazardZone hz = cfg.hazardZone;
        if (hz != null && hz.IsNeutralised) { neutralised = true; return null; }

        if (cfg.pickupItem != null && cfg.pickupItem.activeInHierarchy) return cfg.pickupItem.transform;
        if (cfg.carriedItem != null && cfg.carriedItem.activeInHierarchy) return cfg.carriedItem.transform;
        if (hz != null) return hz.transform;
        return null;
    }

    private void SetVisible(bool on)
    {
        if (_marker != null && _marker.gameObject.activeSelf != on) _marker.gameObject.SetActive(on);
        if (!on && _arrow != null && _arrow.gameObject.activeSelf) _arrow.gameObject.SetActive(false);
    }

    private void EnsureArrowParent(Camera cam)
    {
        if (_arrow == null) return;
        if (_camParent == cam.transform) return;
        _camParent = cam.transform;
        _arrow.SetParent(_camParent, false);
        // Enfant rigide de la caméra : axes locaux = axes caméra (X=droite, Y=haut, Z=avant),
        // donc pas d'ambiguïté de sens pour la flèche. En bas-centre du champ de vision.
        _arrow.localPosition = new Vector3(0f, -arrowDrop, arrowDistance);
        _arrow.localRotation = Quaternion.identity;
    }

    // ── Construction UI ────────────────────────────────────────────────────────
    private void BuildMarker()
    {
        var go = new GameObject("ObjectiveMarker");
        go.transform.SetParent(transform, false);
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 31000;
        var rt = (RectTransform)go.transform;
        rt.sizeDelta = new Vector2(100f, 100f);
        // Pas de GraphicRaycaster → ne capte jamais l'input.

        _markerImg = MakeArrowImage(go.transform, "Chevron");
        // Pointe vers le BAS (vers la cible située en dessous).
        _markerImg.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 180f);
        _marker = go.transform;
    }

    private void BuildArrow()
    {
        var go = new GameObject("ObjectiveArrow");
        go.transform.SetParent(transform, false);
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 31000;
        var rt = (RectTransform)go.transform;
        rt.sizeDelta = new Vector2(100f, 100f);
        rt.localScale = Vector3.one * 0.0016f; // ~0.16 m de large devant les yeux

        _arrowImg = MakeArrowImage(go.transform, "Pointer");
        _arrowGfx = _arrowImg.rectTransform;
        _arrow = go.transform;
    }

    private Image MakeArrowImage(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.sprite = _triangle;
        img.color = beaconColor;
        img.raycastTarget = false;                 // ne bloque jamais les raycasts
        img.material = _overlayUI;                  // ZTest Always → visible à travers les murs
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(100f, 100f);
        return img;
    }

    // Matériau UI toujours-au-dessus (même motif que ProximityPrompt/DangerVignette).
    private Material BuildOverlayUIMaterial()
    {
        var sh = Shader.Find("UI/Default");
        if (sh == null) return null;
        var mat = new Material(sh) { name = "ObjectiveBeaconOverlayUI" };
        mat.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
        return mat;
    }

    // Sprite triangle plein pointant vers le HAUT (apex en haut-centre), généré une fois.
    private static Sprite BuildTriangleSprite()
    {
        const int S = 64;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        var clear = new Color(1f, 1f, 1f, 0f);
        var solid = Color.white;
        for (int y = 0; y < S; y++)
        {
            // apex en haut (y = S-1, demi-largeur 0) → base en bas (y = 0, demi-largeur max).
            float halfW = (S - 1 - y) * 0.5f;
            float cx = (S - 1) * 0.5f;
            for (int x = 0; x < S; x++)
                tex.SetPixel(x, y, Mathf.Abs(x - cx) <= halfW ? solid : clear);
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
    }
}
