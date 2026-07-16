using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// STOP IT! — VRMenuPointer
/// Controller laser pointer for the world-space main menu in VR.
///
/// Uses the OpenXR AIM (pointer) pose — NOT the grip pose — so the ray
/// points correctly when holding the Quest 3 controller naturally.
/// Both hands are evaluated; the one pointing closer to the menu panel wins.
/// Falls back to gaze (camera forward) if no controller aim data is available.
///
/// A trigger press (either hand) clicks the hovered button, or starts story
/// mode if no button is targeted (safety net so the demo never gets stuck).
/// </summary>
[DefaultExecutionOrder(1000)]
public class VRMenuPointer : MonoBehaviour
{
    [Header("References")]
    public Canvas    menuCanvas;
    public Transform gazeRayFallback;       // camera transform — set by VRUIWorldSpace
    public Transform rightControllerForLine; // grip anchor for visual line start
    public Transform leftControllerForLine;

    [Header("Ray")]
    public float maxDistance = 6f;

    [Header("Visuals")]
    public Color lineIdle  = new Color(0.5f, 0.8f, 1f, 0.55f);
    public Color lineHover = new Color(0.35f, 0.95f, 0.45f, 0.95f);
    [Range(1f, 1.3f)] public float hoverScale = 1.08f;
    [Min(0.001f)] public float lineWidth = 0.004f;

    // ── Runtime ──────────────────────────────────────────────────────────────
    private LineRenderer _line;
    private GameObject   _dot;
    private Button[]     _buttons;
    private Button       _hovered;
    private Vector3      _hoveredOrigScale;

    // OpenXR AIM pose InputActions (pointer space = correct pointing direction).
    private InputAction _rAimPos, _rAimRot, _lAimPos, _lAimRot;

    // ── Unity ─────────────────────────────────────────────────────────────────
    private void Awake()
    {
        // ── Line renderer (fades from bright at grip to transparent at tip) ──
        _line = gameObject.AddComponent<LineRenderer>();
        _line.useWorldSpace  = true;
        _line.positionCount  = 2;
        _line.numCapVertices = 4;
        _line.textureMode    = LineTextureMode.Stretch;
        _line.widthCurve     = AnimationCurve.Linear(0f, 1f, 1f, 0.15f);
        _line.widthMultiplier = lineWidth;
        var sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
        if (sh != null) _line.material = new Material(sh);
        _line.enabled = false;

        // ── Hit-point dot ──
        _dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        _dot.name = "PointerDot";
        Destroy(_dot.GetComponent<Collider>());
        _dot.transform.SetParent(transform, false);
        _dot.transform.localScale = Vector3.one * 0.018f;
        var mr = _dot.GetComponent<MeshRenderer>();
        if (mr != null && sh != null)
        {
            var m = new Material(sh);
            m.color = lineHover;
            m.SetColor("_BaseColor", lineHover);
            mr.sharedMaterial = m;
        }
        _dot.SetActive(false);

        // ── InputActions — OpenXR aim/pointer space ──────────────────────────
        // pointerPosition / pointerRotation resolve to the OpenXR aim pose on Quest 3:
        // the ray origin and direction that correspond to "pointing at a screen" with
        // the controller held naturally — NOT the grip pose which is ~30° off.
        _rAimPos = MakeAction("RAimPos", "Vector3",    "{RightHand}", "pointerPosition");
        _rAimRot = MakeAction("RAimRot", "Quaternion", "{RightHand}", "pointerRotation");
        _lAimPos = MakeAction("LAimPos", "Vector3",    "{LeftHand}",  "pointerPosition");
        _lAimRot = MakeAction("LAimRot", "Quaternion", "{LeftHand}",  "pointerRotation");
        _rAimPos.Enable(); _rAimRot.Enable(); _lAimPos.Enable(); _lAimRot.Enable();
    }

    private static InputAction MakeAction(string name, string type, string hand, string ctrl)
    {
        var a = new InputAction(name, InputActionType.Value, expectedControlType: type);
        a.AddBinding($"<XRController>{hand}/{ctrl}");
        return a;
    }

    /// <summary>Re-scan the menu canvas for buttons (call after the canvas rebuilds or moves).</summary>
    public void RefreshButtons()
    {
        _buttons = menuCanvas != null ? menuCanvas.GetComponentsInChildren<Button>(true) : null;
    }

    // ── LateUpdate ────────────────────────────────────────────────────────────
    private void LateUpdate()
    {
        bool active = menuCanvas != null && menuCanvas.enabled && menuCanvas.isActiveAndEnabled;
        if (!active) { ClearHover(); HideVisuals(); return; }
        if (_buttons == null) RefreshButtons();

        GetActiveRay(out Vector3 lineStart, out Vector3 rayOrigin, out Vector3 dir);

        // Intersect the canvas plane.
        Vector3 endPoint = rayOrigin + dir * maxDistance;
        Button  hit      = null;

        float denom = Vector3.Dot(dir, menuCanvas.transform.forward);
        if (Mathf.Abs(denom) > 1e-5f)
        {
            float t = Vector3.Dot(menuCanvas.transform.position - rayOrigin, menuCanvas.transform.forward) / denom;
            if (t > 0f && t < maxDistance)
            {
                Vector3 p = rayOrigin + dir * t;
                endPoint  = p;
                hit       = ButtonAt(p);
            }
        }

        SetHover(hit);

        // Draw beam from controller grip to hit/end point.
        _line.enabled = true;
        _line.SetPosition(0, lineStart);
        _line.SetPosition(1, endPoint);
        Color c = hit != null ? lineHover : lineIdle;
        _line.startColor = c;
        _line.endColor   = new Color(c.r, c.g, c.b, 0f);

        _dot.SetActive(hit != null);
        if (hit != null) _dot.transform.position = endPoint;

        // Click on any trigger press.
        if (VRInput.AnyTriggerDown())
        {
            if (hit != null) hit.onClick.Invoke();
            else FindAnyObjectByType<StoryModeDirector>()?.StartStoryMode();
        }
    }

    // ── Ray resolution ────────────────────────────────────────────────────────

    /// <summary>
    /// Returns:
    ///   <paramref name="lineStart"/>  — visual anchor (controller grip position) for the line.
    ///   <paramref name="rayOrigin"/>  — aim pose position (accurate start for the plane intersection).
    ///   <paramref name="dir"/>        — aim pose forward (direction pointing at the screen).
    /// Falls back to gaze if no controller aim data is live.
    /// </summary>
    private void GetActiveRay(out Vector3 lineStart, out Vector3 rayOrigin, out Vector3 dir)
    {
        bool rOk = TryGetAim(_rAimPos, _rAimRot, rightControllerForLine, out Vector3 rLS, out Vector3 rO, out Vector3 rD);
        bool lOk = TryGetAim(_lAimPos, _lAimRot, leftControllerForLine,  out Vector3 lLS, out Vector3 lO, out Vector3 lD);

        if (rOk || lOk)
        {
            Vector3 menuPos = menuCanvas.transform.position;

            // Auto-correct direction: if the ray points AWAY from the menu hemisphere, flip it.
            // This handles two common Quest 3 / OpenXR edge-cases:
            //   • pointerRotation returns grip rotation whose +Z is toward the elbow (backward)
            //   • pointerRotation binding raises an exception → we fell back to gripAnchor.forward
            //     which can also point backward on some runtimes
            if (rOk && Vector3.Dot(rD, menuPos - rO) < 0f) rD = -rD;
            if (lOk && Vector3.Dot(lD, menuPos - lO) < 0f) lD = -lD;

            // Pick the hand whose corrected direction points most squarely at the menu.
            if (rOk && lOk)
            {
                float rAlign = Vector3.Dot(rD, (menuPos - rO).normalized);
                float lAlign = Vector3.Dot(lD, (menuPos - lO).normalized);
                if (rAlign >= lAlign) { lineStart = rLS; rayOrigin = rO; dir = rD; }
                else                  { lineStart = lLS; rayOrigin = lO; dir = lD; }
            }
            else if (rOk) { lineStart = rLS; rayOrigin = rO; dir = rD; }
            else          { lineStart = lLS; rayOrigin = lO; dir = lD; }
            return;
        }

        // Gaze fallback (camera forward) — used when no controller data is available at all.
        lineStart = rayOrigin = gazeRayFallback != null ? gazeRayFallback.position : Vector3.zero;
        dir = gazeRayFallback != null ? gazeRayFallback.forward : Vector3.forward;
    }

    private static bool TryGetAim(InputAction posAction, InputAction rotAction,
                                   Transform gripAnchor,
                                   out Vector3 lineStart, out Vector3 origin, out Vector3 dir)
    {
        lineStart = origin = Vector3.zero;
        dir = Vector3.forward;
        if (posAction == null || rotAction == null) return false;

        // Try OpenXR AIM pose first (pointerPosition / pointerRotation).
        // Use try-catch: on some platforms the binding resolves to a Pose compound control
        // rather than a standalone Vector3/Quaternion, causing ReadValue to throw.
        Vector3    pos = Vector3.zero;
        Quaternion rot = Quaternion.identity;
        bool aimValid = false;
        try
        {
            pos = posAction.ReadValue<Vector3>();
            rot = rotAction.ReadValue<Quaternion>();
            aimValid = pos.sqrMagnitude >= 0.01f; // zero = no device data
        }
        catch { /* binding type mismatch — will fall through to grip transform */ }

        if (aimValid)
        {
            origin    = pos;
            dir       = rot * Vector3.forward; // may still point backward — auto-flipped in GetActiveRay
            lineStart = gripAnchor != null ? gripAnchor.position : pos;
            return true;
        }

        // AIM pose not available → use the grip transform directly.
        // Direction may be backward (+Z toward elbow on Quest 3); GetActiveRay auto-flips it.
        if (gripAnchor == null) return false;
        origin    = gripAnchor.position;
        lineStart = gripAnchor.position;
        dir       = gripAnchor.forward;
        return true;
    }

    // ── UI helpers ────────────────────────────────────────────────────────────
    private Button ButtonAt(Vector3 worldPoint)
    {
        if (_buttons == null) return null;
        foreach (var b in _buttons)
        {
            if (b == null || !b.isActiveAndEnabled || !b.interactable) continue;
            var rt = b.transform as RectTransform;
            if (rt == null) continue;
            if (rt.rect.Contains(rt.InverseTransformPoint(worldPoint))) return b;
        }
        return null;
    }

    private void SetHover(Button b)
    {
        if (b == _hovered) return;
        ClearHover();
        _hovered = b;
        if (_hovered != null)
        {
            _hoveredOrigScale = _hovered.transform.localScale;
            _hovered.transform.localScale = _hoveredOrigScale * hoverScale;
        }
    }

    private void ClearHover()
    {
        if (_hovered != null) _hovered.transform.localScale = _hoveredOrigScale;
        _hovered = null;
    }

    private void HideVisuals()
    {
        if (_line) _line.enabled = false;
        if (_dot)  _dot.SetActive(false);
    }
}
