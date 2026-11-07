using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Small helpers for building the uGUI screens from code, in the 1080x1920 canvas reference.
public static class UIKit
{
    // The 4x PNGs are drawn for a 390-wide design; this maps their 9-slice borders to canvas px.
    public const float SlicePpu = 1.76f;

    public enum Style { Primary, Secondary }

    public static RectTransform Node(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform)) { layer = 5 }; // UI layer
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        return rt;
    }

    // Stretch to the parent, inset by the given margins.
    public static RectTransform Fill(this RectTransform rt, float left = 0, float bottom = 0, float right = 0, float top = 0)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = new Vector2(left, bottom);
        rt.offsetMax = new Vector2(-right, -top);
        return rt;
    }

    // Fixed size, anchored to a point of the parent (0..1). The pivot defaults to the same point,
    // so (0,1) with pos (40,-30) puts the top-left corner 40 px in and 30 px down.
    public static RectTransform At(this RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size, Vector2? pivot = null)
    {
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = pivot ?? anchor;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        return rt;
    }

    public static Image Img(Transform parent, string name, Sprite sprite, Color color, bool sliced = false, bool raycast = false)
    {
        var img = Node(name, parent).gameObject.AddComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        img.raycastTarget = raycast;
        if (sliced)
        {
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = SlicePpu;
        }
        else img.preserveAspect = sprite != null;
        return img;
    }

    public static Image Panel(Transform parent, string name = "Panel") =>
        Img(parent, name, Art.Load("ui/button_secondary"), Color.white, sliced: true, raycast: true);

    public static Text Label(Transform parent, string text, int size, Color color, Font font = null,
        TextAnchor align = TextAnchor.MiddleCenter, string name = "Label")
    {
        var t = Node(name, parent).gameObject.AddComponent<Text>();
        t.font = font != null ? font : Art.Display;
        t.text = text;
        t.fontSize = size;
        t.color = color;
        t.alignment = align;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }

    public static Text Glyph(Transform parent, string icon, int size, Color color) =>
        Label(parent, icon, size, color, Art.Icons, name: "Icon");

    public static T Shadowed<T>(this T graphic, Color color, float distance = 4f) where T : Graphic
    {
        var shadow = graphic.gameObject.AddComponent<Shadow>();
        shadow.effectColor = color;
        shadow.effectDistance = new Vector2(0f, -distance);
        return graphic;
    }

    public static T Outlined<T>(this T graphic, Color color, float distance = 4f) where T : Graphic
    {
        var outline = graphic.gameObject.AddComponent<Outline>();
        outline.effectColor = color;
        outline.effectDistance = new Vector2(distance, -distance);
        return graphic;
    }

    // A 3D-style button from the mockup sprites: [icon] text, centered above its darker bottom edge.
    public static Button MakeButton(Transform parent, Style style, string text, string icon, int fontSize,
        Action onClick, Sprite iconSprite = null)
    {
        var img = Img(parent, "Button", Art.Load(style == Style.Primary ? "ui/button_primary" : "ui/button_secondary"),
            Color.white, sliced: true, raycast: true);
        var button = img.gameObject.AddComponent<Button>();
        button.targetGraphic = img;
        button.transition = Selectable.Transition.None;
        img.gameObject.AddComponent<PressScale>();
        button.onClick.AddListener(() =>
        {
            Sfx.Play(Sfx.Sound.Click);
            onClick?.Invoke();
        });

        Color ink = style == Style.Primary ? Color.white : Art.Ink;
        var content = Node("Content", img.transform).Fill(16, 18, 16, 4);
        var row = content.gameObject.AddComponent<HorizontalLayoutGroup>();
        row.childAlignment = TextAnchor.MiddleCenter;
        row.spacing = 12;
        row.childControlWidth = row.childControlHeight = true;
        row.childForceExpandWidth = row.childForceExpandHeight = false;

        if (iconSprite != null)
        {
            var pic = Img(content, "Image", iconSprite, Color.white);
            var le = pic.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = le.preferredHeight = fontSize * 1.1f;
        }
        if (icon != null)
        {
            var glyph = Glyph(content, icon, Mathf.RoundToInt(fontSize * 1.15f), ink);
            if (style == Style.Primary) glyph.Shadowed(Art.SaffronShade);
        }
        if (!string.IsNullOrEmpty(text))
        {
            var label = Label(content, text, fontSize, ink);
            if (style == Style.Primary) label.Shadowed(Art.SaffronShade);
        }
        return button;
    }

    public static Text LabelOf(Button button) => button.transform.Find("Content/Label")?.GetComponent<Text>();
    public static Text IconOf(Button button) => button.transform.Find("Content/Icon")?.GetComponent<Text>();

    public static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f, c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
    }
}

// Shrinks a button a little while it is held.
public class PressScale : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    float target = 1f;

    public void OnPointerDown(PointerEventData e) => target = 0.93f;
    public void OnPointerUp(PointerEventData e) => target = 1f;
    public void OnPointerExit(PointerEventData e) => target = 1f;

    void OnDisable()
    {
        target = 1f;
        transform.localScale = Vector3.one;
    }

    void Update()
    {
        float s = Mathf.Lerp(transform.localScale.x, target, 1f - Mathf.Exp(-25f * Time.unscaledDeltaTime));
        transform.localScale = new Vector3(s, s, 1f);
    }
}

// Gentle floating motion for decorative kites.
public class Bob : MonoBehaviour
{
    public float amplitude = 18f, speed = 1.3f, sway = 6f, phase;
    Vector2 home;
    RectTransform rt;

    void Start()
    {
        rt = (RectTransform)transform;
        home = rt.anchoredPosition;
    }

    void Update()
    {
        float t = Time.unscaledTime * speed + phase;
        rt.anchoredPosition = home + new Vector2(Mathf.Sin(t * 0.7f) * amplitude * 0.5f, Mathf.Sin(t) * amplitude);
        rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 0.9f) * sway);
    }
}

// Turns slowly (win-screen rays). Unscaled time, so it keeps turning under popups.
public class Spin : MonoBehaviour
{
    public float degreesPerSecond = 12f;

    void Update() => transform.Rotate(0f, 0f, -degreesPerSecond * Time.unscaledDeltaTime);
}

// Paper confetti for the win and chapter screens: Burst() drops colored pieces that flutter down and are removed
// below the bottom edge. Unscaled time, like the other UI motion.
public class Confetti : MonoBehaviour
{
    class Piece { public RectTransform rt; public Vector2 velocity; public float spin, flutter, phase; }

    readonly List<Piece> pieces = new();
    static Color[] colors;

    public void Burst(int count)
    {
        colors ??= new[] { LevelDefs.Palette[0], LevelDefs.Palette[1], LevelDefs.Palette[2], LevelDefs.Palette[3], LevelDefs.Palette[4], Art.Saffron, Art.Cream };
        float width = ((RectTransform)transform).rect.width;
        for (int i = 0; i < count; i++)
        {
            var rt = UIKit.Img(transform, "Piece", null, colors[UnityEngine.Random.Range(0, colors.Length)]).rectTransform;
            rt.At(new Vector2(0.5f, 1f), new Vector2(UnityEngine.Random.Range(-0.5f, 0.5f) * width, UnityEngine.Random.Range(40f, 700f)),
                new Vector2(UnityEngine.Random.Range(16f, 26f), UnityEngine.Random.Range(26f, 42f)), new Vector2(0.5f, 0.5f));
            pieces.Add(new Piece
            {
                rt = rt,
                velocity = new Vector2(UnityEngine.Random.Range(-60f, 60f), -UnityEngine.Random.Range(260f, 520f)),
                spin = UnityEngine.Random.Range(-240f, 240f),
                flutter = UnityEngine.Random.Range(2f, 5f),
                phase = UnityEngine.Random.Range(0f, 6.3f),
            });
        }
    }

    public void Clear()
    {
        foreach (var p in pieces) Destroy(p.rt.gameObject);
        pieces.Clear();
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime, bottom = -((RectTransform)transform).rect.height - 80f;
        for (int i = pieces.Count - 1; i >= 0; i--)
        {
            var p = pieces[i];
            p.phase += dt * p.flutter;
            p.rt.anchoredPosition += (p.velocity + new Vector2(Mathf.Sin(p.phase) * 110f, 0f)) * dt;
            p.rt.localRotation = Quaternion.Euler(0f, 0f, p.rt.localEulerAngles.z + p.spin * dt);
            p.rt.localScale = new Vector3(Mathf.Cos(p.phase * 1.7f), 1f, 1f); // paper turning over
            if (p.rt.anchoredPosition.y > bottom) continue;
            Destroy(p.rt.gameObject);
            pieces.RemoveAt(i);
        }
    }
}

// Fits its RectTransform to Screen.safeArea (notches, rounded corners, gesture bars).
public class SafeArea : MonoBehaviour
{
    Rect applied;

    void Awake() => Update();

    void Update()
    {
        Rect safe = Screen.safeArea;
        if (safe == applied || Screen.width == 0 || Screen.height == 0) return;
        applied = safe;
        var rt = (RectTransform)transform;
        rt.anchorMin = new Vector2(safe.xMin / Screen.width, safe.yMin / Screen.height);
        rt.anchorMax = new Vector2(safe.xMax / Screen.width, safe.yMax / Screen.height);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }
}
