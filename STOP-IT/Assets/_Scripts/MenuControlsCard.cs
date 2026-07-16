using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// STOP IT! — MenuControlsCard
/// Rides on top of the existing main menu (<see cref="MenuScenarioShowcase"/>) to show the
/// game's two brand images so a first-time VR player instantly understands the controls:
///   • LOGO   → a non-interactive header image at the top of the menu canvas.
///   • COMMANDES → a large, centered, aspect-preserved board of the control scheme.
///
/// It attaches its UI as CHILDREN of the menu's own Canvas, so it inherits the SAME
/// screen-space (desktop) / world-space (VR) conversion + placement that
/// <see cref="VRUIWorldSpace"/> already applies — no separate canvas, no scene wiring.
/// Self-bootstraps via <see cref="RuntimeInitializeOnLoadMethod"/> like VRUIWorldSpace /
/// DangerVignette. If no menu exists (a gameplay scene), it simply disables itself.
///
/// Show / dismiss / re-open:
///   • AUTO-SHOW once, when the menu first appears, so novices see the controls immediately.
///   • DISMISS on any trigger (VR) or mouse/keyboard (desktop). Dismissal is routed so it can
///     never fight <see cref="VRMenuPointer"/>: while the board is up, a giant transparent
///     ClickCatcher Button is the ONLY interactable button, so the laser resolves to it
///     (closes the board) instead of clicking a menu button underneath or firing its
///     "no target → start story mode" safety net. A raw <see cref="VRInput"/> poll in
///     LateUpdate (order 1100, AFTER the laser at 1000) is a race-free backstop for the brief
///     window before any laser exists.
///   • RE-OPEN via a "Revoir les commandes" uGUI Button. The VR laser only clicks Buttons that
///     <see cref="VRMenuPointer.RefreshButtons"/> has scanned; we build our Buttons up-front
///     AND explicitly re-scan every live pointer after building, so the laser always sees them
///     regardless of setup order.
/// </summary>
[DefaultExecutionOrder(1100)] // after VRMenuPointer (1000) so our dismiss poll runs once the laser has consumed the frame's trigger
public class MenuControlsCard : MonoBehaviour
{
    private const string LogoPath      = "UI/Logo";
    private const string CommandesPath = "UI/Commandes";

    private static bool _spawned;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (_spawned) return;
        _spawned = true;
        var go = new GameObject("Menu Controls Card");
        go.AddComponent<MenuControlsCard>();
    }

    private Canvas _menuCanvas;
    private GameObject _board;          // ControlsBoard root (scrim + click-catcher + image + hint)
    private Button _clickCatcher;       // giant transparent button the laser/mouse hits to close
    private TextMeshProUGUI _hint;
    private bool _boardVisible;

    // Buttons we temporarily neutralised while the board is up (restored on close).
    private readonly List<Button> _disabledButtons = new();

    private void Start() => StartCoroutine(Setup());

    private IEnumerator Setup()
    {
        // Attendre que le menu principal ait construit son canvas (MenuScenarioShowcase, ordre 25).
        MenuScenarioShowcase showcase = null;
        float t = 0f;
        while (t < 8f)
        {
            if (showcase == null)
                showcase = FindAnyObjectByType<MenuScenarioShowcase>(FindObjectsInactive.Include);
            if (showcase != null)
            {
                // Même résolution robuste que VRUIWorldSpace.GetCanvas (même GameObject, sinon parent/enfant).
                _menuCanvas = showcase.GetComponent<Canvas>()
                           ?? showcase.GetComponentInParent<Canvas>()
                           ?? showcase.GetComponentInChildren<Canvas>();
                if (_menuCanvas != null) break;
            }
            t += Time.unscaledDeltaTime;
            yield return null;
        }
        if (_menuCanvas == null) { enabled = false; yield break; } // pas de menu → scène de jeu, rien à faire

        BuildLogoHeader();
        BuildReopenButton();
        BuildControlsBoard();

        // Faire re-scanner TOUS les lasers déjà en place pour qu'ils voient nos boutons
        // (zone de fermeture + "Revoir les commandes"). Robuste quel que soit l'ordre de setup :
        // les lasers créés APRÈS nous scanneront de toute façon à leur création.
        foreach (var p in FindObjectsByType<VRMenuPointer>(FindObjectsInactive.Include))
            if (p != null) p.RefreshButtons();

        // Auto-affichage unique : le novice voit les commandes dès l'entrée dans le menu.
        bool inMenu = GameManager.Instance == null ||
                      GameManager.Instance.State == GameManager.GameState.Menu;
        if (inMenu) Show();
    }

    // ── Public API (câblé sur les boutons uGUI) ───────────────────────────────
    public void Show()
    {
        if (_board == null) return;
        _board.transform.SetAsLastSibling();       // rester au-dessus du reste du menu
        _board.SetActive(true);
        _boardVisible = true;

        if (_hint != null)
            _hint.text = InputHints.IsVRActive()
                ? "Appuie sur une gâchette pour continuer"
                : "Clique ou appuie sur une touche pour continuer";

        // Neutraliser tous les AUTRES boutons : ainsi le laser ne peut résoudre QUE la zone de
        // fermeture, jamais un bouton du menu en dessous ni le filet "aucune cible → lancer l'histoire".
        _disabledButtons.Clear();
        foreach (var b in _menuCanvas.GetComponentsInChildren<Button>(true))
        {
            if (b == null || b == _clickCatcher || !b.interactable) continue;
            b.interactable = false;
            _disabledButtons.Add(b);
        }
    }

    public void Hide()
    {
        if (!_boardVisible) return;
        _boardVisible = false;
        if (_board != null) _board.SetActive(false);
        foreach (var b in _disabledButtons)
            if (b != null) b.interactable = true;
        _disabledButtons.Clear();
    }

    // Fermeture par input brut. En LateUpdate (ordre 1100) : le laser (ordre 1000) a déjà traité la
    // gâchette de CE frame en cliquant la zone de fermeture, donc le panneau est en général déjà
    // fermé ici — fermer APRÈS le laser évite toute course d'input (pas de bouton exposé à la même
    // pression). Ce poll reste le SEUL moyen de fermer tant qu'aucun laser n'existe encore.
    private void LateUpdate()
    {
        if (!_boardVisible) return;
        bool dismiss = InputHints.IsVRActive()
            ? VRInput.AnyTriggerDown()
            : DesktopDismissed();
        if (dismiss) Hide();
    }

    private static bool DesktopDismissed()
    {
        var kb = Keyboard.current;
        if (kb != null && kb.anyKey.wasPressedThisFrame) return true;
        var mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.wasPressedThisFrame) return true;
        return false;
    }

    // ── Construction UI (résolution de référence 1920×1080) ───────────────────
    private void BuildLogoHeader()
    {
        var tex = Resources.Load<Texture2D>(LogoPath);
        if (tex == null)
        {
            Debug.LogWarning($"[MenuControlsCard] Logo introuvable à Resources/{LogoPath} — en-tête ignorée.");
            return;
        }

        var go = new GameObject("MenuLogo", typeof(RectTransform));
        go.transform.SetParent(_menuCanvas.transform, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -30f);
        rt.sizeDelta = new Vector2(100f, 150f); // la largeur est recalculée par l'AspectRatioFitter

        var raw = go.AddComponent<RawImage>();
        raw.texture = tex;
        raw.raycastTarget = false;

        // Ratio conservé à partir des dimensions réelles de la texture.
        var arf = go.AddComponent<AspectRatioFitter>();
        arf.aspectMode = AspectRatioFitter.AspectMode.HeightControlsWidth;
        arf.aspectRatio = (float)tex.width / Mathf.Max(1, tex.height);
    }

    private void BuildReopenButton()
    {
        var go = new GameObject("Btn_Commandes", typeof(RectTransform));
        go.transform.SetParent(_menuCanvas.transform, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-60f, -50f);
        rt.sizeDelta = new Vector2(380f, 70f);

        var bg = go.AddComponent<Image>();
        bg.color = new Color(0.18f, 0.45f, 0.85f, 0.95f);
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = bg;
        btn.onClick.AddListener(Show);

        var txt = MakeText(go.transform, "Text", "Revoir les commandes", 26,
                           FontStyles.Bold, Color.white, TextAlignmentOptions.Center);
        Fill((RectTransform)txt.transform);
        txt.enableAutoSizing = true; txt.fontSizeMax = 26; txt.fontSizeMin = 14;
    }

    private void BuildControlsBoard()
    {
        _board = new GameObject("ControlsBoard", typeof(RectTransform));
        _board.transform.SetParent(_menuCanvas.transform, false);
        _board.transform.SetAsLastSibling(); // rendu (et clic desktop) au-dessus du reste du menu
        Fill((RectTransform)_board.transform);

        // Voile sombre plein écran (décoratif, ne capte pas les raycasts).
        var scrim = MakeImage(_board.transform, "Scrim", new Color(0f, 0f, 0f, 0.82f));
        Fill((RectTransform)scrim.transform);
        scrim.raycastTarget = false;

        // Zone cliquable géante et transparente : c'est ELLE que le laser (ou la souris) frappe pour
        // fermer. Débordant largement le canvas, elle absorbe presque toutes les visées vers l'avant,
        // ce qui empêche le laser de tomber sur "aucune cible → lancer l'histoire".
        var catcherGO = new GameObject("ClickCatcher", typeof(RectTransform));
        catcherGO.transform.SetParent(_board.transform, false);
        var crt = (RectTransform)catcherGO.transform;
        crt.anchorMin = crt.anchorMax = new Vector2(0.5f, 0.5f);
        crt.pivot = new Vector2(0.5f, 0.5f);
        crt.anchoredPosition = Vector2.zero;
        crt.sizeDelta = new Vector2(8000f, 6000f);
        var catcherImg = catcherGO.AddComponent<Image>();
        catcherImg.color = new Color(0f, 0f, 0f, 0f); // invisible, mais raycastTarget actif
        _clickCatcher = catcherGO.AddComponent<Button>();
        _clickCatcher.transition = Selectable.Transition.None;
        _clickCatcher.targetGraphic = catcherImg;
        _clickCatcher.onClick.AddListener(Hide);

        // Image des commandes : ajustée au ratio réel de la texture dans une zone centrale (FitInParent).
        var holder = new GameObject("CommandesHolder", typeof(RectTransform));
        holder.transform.SetParent(_board.transform, false);
        var hrt = (RectTransform)holder.transform;
        hrt.anchorMin = hrt.anchorMax = new Vector2(0.5f, 0.5f);
        hrt.pivot = new Vector2(0.5f, 0.5f);
        hrt.anchoredPosition = new Vector2(0f, 40f);
        hrt.sizeDelta = new Vector2(1600f, 860f);

        var tex = Resources.Load<Texture2D>(CommandesPath);
        if (tex != null)
        {
            var imgGO = new GameObject("CommandesImage", typeof(RectTransform));
            imgGO.transform.SetParent(holder.transform, false);
            var raw = imgGO.AddComponent<RawImage>();
            raw.texture = tex;
            raw.raycastTarget = false;
            Fill((RectTransform)imgGO.transform);
            var arf = imgGO.AddComponent<AspectRatioFitter>();
            arf.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            arf.aspectRatio = (float)tex.width / Mathf.Max(1, tex.height);
        }
        else
        {
            Debug.LogWarning($"[MenuControlsCard] Image des commandes introuvable à Resources/{CommandesPath} — panneau sans visuel.");
        }

        // Indice de fermeture, sous l'image (texte réévalué à chaque ouverture selon VR/desktop).
        var hintGO = new GameObject("Hint", typeof(RectTransform));
        hintGO.transform.SetParent(_board.transform, false);
        _hint = hintGO.AddComponent<TextMeshProUGUI>();
        _hint.text = "Appuie sur une gâchette pour continuer";
        _hint.fontSize = 34;
        _hint.fontStyle = FontStyles.Bold;
        _hint.color = new Color(1f, 0.81f, 0.25f);
        _hint.alignment = TextAlignmentOptions.Center;
        _hint.raycastTarget = false;
        var trt = (RectTransform)hintGO.transform;
        trt.anchorMin = new Vector2(0.1f, 0f);
        trt.anchorMax = new Vector2(0.9f, 0f);
        trt.pivot = new Vector2(0.5f, 0f);
        trt.anchoredPosition = new Vector2(0f, 40f);
        trt.sizeDelta = new Vector2(0f, 60f);

        _board.SetActive(false);
        _boardVisible = false;
    }

    // ── Helpers bas niveau ────────────────────────────────────────────────────
    private static Image MakeImage(Transform parent, string name, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = color;
        return img;
    }

    private static TextMeshProUGUI MakeText(Transform parent, string name, string text, float size,
                                            FontStyles style, Color color, TextAlignmentOptions align)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.color = color;
        tmp.alignment = align;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.raycastTarget = false;
        return tmp;
    }

    private static void Fill(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
