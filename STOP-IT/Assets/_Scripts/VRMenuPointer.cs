using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// STOP IT! — VRMenuPointer
/// A laser pointer for the world-space main menu in VR. Casts a ray from the (tracked)
/// right controller, intersects the menu canvas plane, finds the uGUI Button under the
/// hit point, highlights it (slight scale-up), and on a trigger press invokes that
/// button's onClick. A trigger press with NO button targeted starts story mode — a
/// safety net so the demo can never get stuck on the menu.
///
/// Deliberately does NOT use the EventSystem / GraphicRaycaster path: the scene's
/// InputSystemUIInputModule isn't wired for tracked-device UI, and a direct ray→rect
/// test is fully under our control (and reasons identically on desktop). Added at
/// runtime by <see cref="VRUIWorldSpace"/>, only in VR.
/// </summary>
[DefaultExecutionOrder(1000)]
public class VRMenuPointer : MonoBehaviour
{
    public Transform rayOrigin;     // right controller (falls back to left / camera)
    public Canvas menuCanvas;
    public float maxDistance = 6f;
    [Tooltip("Tilt the laser down from the controller's forward, to match how a controller is naturally held.")]
    public float rayAngleDownDeg = 0f;

    public Color lineIdle  = new Color(0.5f, 0.8f, 1f, 0.5f);
    public Color lineHover = new Color(0.35f, 0.95f, 0.45f, 0.95f);
    [Range(1f, 1.3f)] public float hoverScale = 1.15f;

    private LineRenderer _line;
    private Button[] _buttons;
    private Button _hovered;
    private Vector3 _hoveredScale;

    private void Awake()
    {
        _line = gameObject.AddComponent<LineRenderer>();
        _line.widthMultiplier = 0.005f;
        var sh = Shader.Find("Sprites/Default");
        if (sh != null) _line.material = new Material(sh);
        _line.positionCount = 2;
        _line.useWorldSpace = true;
        _line.numCapVertices = 4;
        _line.textureMode = LineTextureMode.Stretch;
        SetLineColor(lineIdle);
        _line.enabled = false;
    }

    /// <summary>Re-scan the menu for buttons (call after the menu (re)builds or repositions).</summary>
    public void RefreshButtons()
    {
        _buttons = menuCanvas != null ? menuCanvas.GetComponentsInChildren<Button>(true) : null;
    }

    private void LateUpdate()
    {
        bool active = rayOrigin != null && menuCanvas != null
                   && menuCanvas.enabled && menuCanvas.isActiveAndEnabled;
        if (!active)
        {
            ClearHover();
            if (_line) _line.enabled = false;
            return;
        }

        if (_buttons == null) RefreshButtons();

        Vector3 origin = rayOrigin.position;
        Vector3 dir    = Quaternion.AngleAxis(rayAngleDownDeg, rayOrigin.right) * rayOrigin.forward;

        Vector3 planePoint  = menuCanvas.transform.position;
        Vector3 planeNormal = menuCanvas.transform.forward;
        float denom = Vector3.Dot(dir, planeNormal);

        Vector3 endPoint = origin + dir * maxDistance;
        Button hit = null;
        if (Mathf.Abs(denom) > 1e-5f)
        {
            float t = Vector3.Dot(planePoint - origin, planeNormal) / denom;
            if (t > 0f && t < maxDistance)
            {
                Vector3 p = origin + dir * t;
                endPoint = p;
                hit = ButtonAt(p);
            }
        }

        SetHover(hit);

        if (_line)
        {
            _line.enabled = true;
            _line.SetPosition(0, origin);
            _line.SetPosition(1, endPoint);
            SetLineColor(hit != null ? lineHover : lineIdle);
        }

        if (VRInput.AnyTriggerDown())
        {
            if (hit != null) hit.onClick.Invoke();
            else FindAnyObjectByType<StoryModeDirector>()?.StartStoryMode();
        }
    }

    private Button ButtonAt(Vector3 worldPoint)
    {
        if (_buttons == null) return null;
        foreach (var b in _buttons)
        {
            if (b == null || !b.isActiveAndEnabled || !b.interactable) continue;
            var rt = b.transform as RectTransform;
            if (rt == null) continue;
            Vector3 local = rt.InverseTransformPoint(worldPoint);
            if (rt.rect.Contains(local)) return b;
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
            _hoveredScale = _hovered.transform.localScale;
            _hovered.transform.localScale = _hoveredScale * hoverScale;
        }
    }

    private void ClearHover()
    {
        if (_hovered != null) _hovered.transform.localScale = _hoveredScale;
        _hovered = null;
    }

    private void SetLineColor(Color c)
    {
        if (_line == null) return;
        _line.startColor = c;
        _line.endColor = c;
    }
}
