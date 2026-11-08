using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// One kite: its body in the sky, its string (3 passes so crossings read clearly) and its charkhi.
// The string is a dark outline in its own color, the cord, and a thin shine, so it reads as a round dyed
// cord on every sky (a light outline glowed like a white border on the dusk and night skies).
// Once freed, the body flies to a dock (a chhat spot or a waiting slot) and follows it; Undo flies it back.
// Special looks: a second string + charkhi (double-string patang), an ice layer, a gray "?" body and string
// while the color is hidden, a white streamer tail on wind kites, and the player's chosen skin pattern.
public class Kite : MonoBehaviour
{
    // String passes in design px (see Art.DesignPx).
    const float OutlineWidth = 5.6f * Art.DesignPx;
    const float CordWidth = 3.6f * Art.DesignPx;
    const float ShineWidth = 1.1f * Art.DesignPx;
    static readonly Vector3 StringShadowOffset = new Vector3(2f, -4f, 0f) * Art.DesignPx;
    static readonly Vector3 KiteShadowOffset = new Vector3(4f, -8f, 0f) * Art.DesignPx;

    public const float SkyWidth = 34f * Art.DesignPx; // kite body width while flying
    public const float DockWidth = 0.4f;             // kite body width on a chhat or in a slot

    // Body sorting order in the sky: shadow -1, streamer -2, skin +1, ice +2 (5 apart per layer).
    public static int SkyOrder(int layer) => 160 + layer * 5;
    const int DockedOrder = 400;

    static readonly Color HiddenCord = Art.Hex("A39A8F"), HiddenShine = Art.Hex("CFC8BE"), HiddenDark = Art.Hex("6E665C");

    public int Id { get; private set; }
    public int Layer { get; private set; }

    // One string: [shadow, outline, cord, shine].
    readonly List<LineRenderer[]> strings = new();
    readonly List<SpriteRenderer> spools = new();
    Transform body;
    SpriteRenderer bodyRenderer, shadowRenderer, skinRenderer, iceRenderer;
    LineRenderer streamer;
    Collider2D bodyCollider;
    int colorId;
    bool hidden;
    Color outlineColor, cordColor, shineColor;
    Vector3 skyScale, dockScale, homePosition;
    Coroutine flashRoutine, releaseRoutine;
    bool docked, returning;

    static Material lineMaterial;

    public struct Look
    {
        public bool iced, hidden, wind;
        public Sprite skin; // null = classic
    }

    public void Init(int id, LevelDefs.KiteDef def, Func<Vector2, Vector2> toWorld, float spoolWidth, Look look)
    {
        Id = id;
        Layer = def.layer;
        colorId = def.colorId;

        var sprite = Art.KiteSprite(def.colorId);
        skyScale = Vector3.one * Art.ScaleForWidth(sprite, SkyWidth);
        dockScale = Vector3.one * Art.ScaleForWidth(sprite, DockWidth);

        // The kite sprite's pivot is where the string meets it, so the body sits right on the anchor.
        body = new GameObject("Body").transform;
        body.SetParent(transform, false);
        homePosition = toWorld(def.anchor);
        body.position = homePosition;
        body.localScale = skyScale;
        bodyRenderer = body.gameObject.AddComponent<SpriteRenderer>();
        bodyRenderer.sprite = sprite;

        var shadow = new GameObject("Shadow").transform;
        shadow.SetParent(body, false);
        shadow.localPosition = KiteShadowOffset / skyScale.x;
        shadowRenderer = shadow.gameObject.AddComponent<SpriteRenderer>();
        shadowRenderer.sprite = Art.Load("kites/kite_shadow");

        if (look.skin != null) skinRenderer = Overlay("Skin", look.skin);
        iceRenderer = Overlay("Ice", Art.Load("kites/kite_ice"));
        iceRenderer.enabled = look.iced;

        // Generous tap area for fingers, centered on the diamond (in sprite space).
        var circle = body.gameObject.AddComponent<CircleCollider2D>();
        circle.offset = new Vector2(0f, sprite.bounds.size.y * 0.375f);
        circle.radius = sprite.bounds.size.x * 0.65f;
        bodyCollider = circle;

        AddString(def.spool, def.anchor, def.bend, toWorld, spoolWidth);
        if (def.twin) AddString(def.spool2, def.anchor, def.bend2, toWorld, spoolWidth);

        if (look.wind)
        {
            streamer = new GameObject("Streamer").AddComponent<LineRenderer>();
            streamer.transform.SetParent(transform, false);
            streamer.positionCount = 8;
            streamer.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0.3f);
            streamer.widthMultiplier = 0.07f;
            streamer.numCapVertices = 3;
            streamer.material = lineMaterial ??= new Material(Shader.Find("Sprites/Default"));
            SetLineColor(streamer, new Color(0.9f, 0.97f, 1f, 0.95f));
        }

        hidden = look.hidden;
        SetColors();
        SetLayer(Layer);
    }

    SpriteRenderer Overlay(string name, Sprite sprite)
    {
        var sr = new GameObject(name).AddComponent<SpriteRenderer>();
        sr.transform.SetParent(body, false);
        sr.sprite = sprite;
        return sr;
    }

    void AddString(Vector2 spoolAt, Vector2 anchor, Vector2 bend, Func<Vector2, Vector2> toWorld, float spoolWidth)
    {
        Vector2[] curve = StringGeometry.Sample(spoolAt, anchor, bend);
        var points = new Vector3[curve.Length];
        for (int i = 0; i < curve.Length; i++) points[i] = toWorld(curve[i]);
        var shadow = MakeLine("StringShadow", points, OutlineWidth, Art.StringShadow);
        shadow.useWorldSpace = false; // shifted as a whole; the string points themselves stay put
        shadow.transform.position = StringShadowOffset;
        shadow.sortingOrder = -5;
        strings.Add(new[] { shadow, MakeLine("StringOutline", points, OutlineWidth, Color.white),
            MakeLine("String", points, CordWidth, Color.white), MakeLine("StringShine", points, ShineWidth, Color.white) });

        var spoolGo = new GameObject("Charkhi");
        spoolGo.transform.SetParent(transform, false);
        var spool = spoolGo.AddComponent<SpriteRenderer>();
        spool.sprite = Art.SpoolSprite(colorId);
        spoolGo.transform.position = toWorld(spoolAt);
        spoolGo.transform.localScale = Vector3.one * Art.ScaleForWidth(spool.sprite, spoolWidth);
        spool.sortingOrder = 150;
        spools.Add(spool);
    }

    // Colors for the real color, or gray while it is hidden.
    void SetColors()
    {
        cordColor = hidden ? HiddenCord : LevelDefs.Palette[colorId];
        shineColor = hidden ? HiddenShine : LevelDefs.PaletteLight[colorId];
        outlineColor = Color.Lerp(hidden ? HiddenDark : LevelDefs.PaletteDark[colorId], Art.Ink, 0.45f);
        bodyRenderer.sprite = hidden ? Art.Load("kites/kite_hidden") : Art.KiteSprite(colorId);
        if (skinRenderer != null) skinRenderer.enabled = !hidden;
        foreach (var spool in spools) spool.sprite = hidden ? Art.Load("spools/spool_hidden") : Art.SpoolSprite(colorId);
        if (docked) return;
        foreach (var s in strings)
        {
            SetLineColor(s[1], outlineColor);
            SetLineColor(s[2], cordColor);
            SetLineColor(s[3], shineColor);
        }
    }

    // A higher layer draws on top (also used when the wind swaps two kites).
    public void SetLayer(int layer)
    {
        Layer = layer;
        foreach (var s in strings)
        {
            s[1].sortingOrder = layer * 3;
            s[2].sortingOrder = layer * 3 + 1;
            s[3].sortingOrder = layer * 3 + 2;
        }
        if (!docked) SetBodyOrder(SkyOrder(layer));
    }

    void SetBodyOrder(int order)
    {
        bodyRenderer.sortingOrder = order;
        shadowRenderer.sortingOrder = order - 1;
        if (skinRenderer != null) skinRenderer.sortingOrder = order + 1;
        iceRenderer.sortingOrder = order + 2;
        if (streamer != null) streamer.sortingOrder = order - 2;
    }

    public void SetHidden(bool value)
    {
        if (hidden == value) return;
        hidden = value;
        SetColors();
        if (!value) StartCoroutine(Pulse(0.25f, 0.3f)); // a little pop as the color shows
    }

    public void SetIced(bool value)
    {
        if (iceRenderer.enabled == value) return;
        if (value) iceRenderer.enabled = true;
        else StartCoroutine(Crack());
    }

    IEnumerator Crack()
    {
        Vector3 start = iceRenderer.transform.localScale;
        for (float t = 0; t < 0.25f; t += Time.deltaTime)
        {
            float k = t / 0.25f;
            iceRenderer.transform.localScale = start * (1f + 0.35f * k);
            iceRenderer.color = new Color(1f, 1f, 1f, 1f - k);
            yield return null;
        }
        iceRenderer.enabled = false;
        iceRenderer.transform.localScale = start;
        iceRenderer.color = Color.white;
    }

    LineRenderer MakeLine(string name, Vector3[] points, float width, Color color)
    {
        var lr = new GameObject(name).AddComponent<LineRenderer>();
        lr.transform.SetParent(transform, false);
        lr.useWorldSpace = true;
        lr.positionCount = points.Length;
        lr.SetPositions(points);
        lr.widthMultiplier = width;
        lr.numCapVertices = 4;
        lr.numCornerVertices = 2;
        lr.material = lineMaterial ??= new Material(Shader.Find("Sprites/Default"));
        SetLineColor(lr, color);
        return lr;
    }

    // Move the kite onto a chhat dock or a waiting slot. The first call cuts it loose from its string.
    public void DockTo(Transform dock)
    {
        if (body.parent == dock) return;
        if (!docked)
        {
            docked = true;
            returning = false;
            bodyCollider.enabled = false;
            if (flashRoutine != null) StopCoroutine(flashRoutine);
            iceRenderer.enabled = false;
            if (streamer != null) streamer.enabled = false;
            SetBodyOrder(DockedOrder);
            releaseRoutine = StartCoroutine(ReleaseString());
        }
        body.SetParent(dock, true);
    }

    // Undo: back into the sky on its string(s).
    public void Undock()
    {
        if (!docked) return;
        docked = false;
        returning = true;
        if (releaseRoutine != null) StopCoroutine(releaseRoutine);
        body.SetParent(transform, true);
        bodyCollider.enabled = true;
        if (streamer != null) streamer.enabled = true;
        foreach (var s in strings)
        {
            foreach (var line in s) line.enabled = true;
            SetLineColor(s[0], Art.StringShadow);
        }
        foreach (var spool in spools) spool.color = Color.white;
        SetColors();
        SetLayer(Layer);
    }

    void Update()
    {
        float t = 1f - Mathf.Exp(-8f * Time.deltaTime);
        if (docked)
        {
            body.localPosition = Vector3.Lerp(body.localPosition, Vector3.zero, t);
            body.localRotation = Quaternion.Slerp(body.localRotation, Quaternion.identity, t);
            body.localScale = Vector3.Lerp(body.localScale, dockScale, t);
        }
        else if (returning)
        {
            body.position = Vector3.Lerp(body.position, homePosition, t);
            body.localRotation = Quaternion.Slerp(body.localRotation, Quaternion.identity, t);
            body.localScale = Vector3.Lerp(body.localScale, skyScale, t);
            if ((body.position - homePosition).sqrMagnitude < 1e-5f) { body.position = homePosition; body.localScale = skyScale; returning = false; }
        }

        if (streamer != null && streamer.enabled)
        {
            Vector3 top = body.position + Vector3.down * 0.05f;
            for (int i = 0; i < streamer.positionCount; i++)
            {
                float k = i / (streamer.positionCount - 1f);
                streamer.SetPosition(i, top + new Vector3(Mathf.Sin(Time.time * 6f - k * 5f) * 0.09f * k + k * 0.12f, -k * 0.6f, 0f));
            }
        }
    }

    void OnDestroy()
    {
        // A docked body lives under a chhat or slot, not under this object.
        if (body != null && body.parent != transform) Destroy(body.gameObject);
    }

    IEnumerator ReleaseString()
    {
        var lines = new List<LineRenderer>();
        foreach (var s in strings) lines.AddRange(s);
        var start = lines.ConvertAll(l => l.startColor);
        for (float t = 0; t < 0.4f; t += Time.deltaTime)
        {
            float a = 1f - t / 0.4f;
            for (int i = 0; i < lines.Count; i++)
                SetLineColor(lines[i], new Color(start[i].r, start[i].g, start[i].b, start[i].a * a));
            foreach (var spool in spools) spool.color = new Color(1f, 1f, 1f, Mathf.Lerp(0.35f, 1f, a));
            yield return null;
        }
        foreach (var line in lines) line.enabled = false;
        releaseRoutine = null;
    }

    public IEnumerator Shake()
    {
        Vector3 start = body.position;
        for (float t = 0; t < 0.35f; t += Time.deltaTime)
        {
            body.position = start + Vector3.right * Mathf.Sin(t * 60f) * 0.12f;
            body.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 60f) * 8f);
            yield return null;
        }
        body.position = start;
        body.localRotation = Quaternion.identity;
    }

    // The wind turning: the kite sways for a moment.
    public IEnumerator Sway()
    {
        for (float t = 0; t < 0.9f && !docked; t += Time.deltaTime)
        {
            body.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 14f) * 14f * (1f - t / 0.9f));
            yield return null;
        }
        if (!docked) body.localRotation = Quaternion.identity;
    }

    IEnumerator Pulse(float seconds, float amount)
    {
        for (float t = 0; t < seconds && !docked; t += Time.deltaTime)
        {
            body.localScale = skyScale * (1f + amount * Mathf.Sin(t / seconds * Mathf.PI));
            yield return null;
        }
        if (!docked) body.localScale = skyScale;
    }

    // Blink this string white so the player sees what is lying on top.
    public void Flash()
    {
        if (docked) return;
        if (flashRoutine != null) StopCoroutine(flashRoutine);
        flashRoutine = StartCoroutine(FlashRoutine());
    }

    IEnumerator FlashRoutine()
    {
        for (int i = 0; i < 3; i++)
        {
            foreach (var s in strings)
            {
                SetLineColor(s[2], Color.white);
                SetLineColor(s[3], Color.white);
                s[1].widthMultiplier = OutlineWidth * 1.7f;
                s[2].widthMultiplier = CordWidth * 1.8f;
            }
            yield return new WaitForSeconds(0.12f);
            foreach (var s in strings)
            {
                SetLineColor(s[2], cordColor);
                SetLineColor(s[3], shineColor);
                s[1].widthMultiplier = OutlineWidth;
                s[2].widthMultiplier = CordWidth;
            }
            yield return new WaitForSeconds(0.12f);
        }
        flashRoutine = null;
    }

    // Pulse the kite body and show a ring around it to point the player at it.
    public bool Hinting { get; private set; }

    public IEnumerator HintPulse()
    {
        Hinting = true;
        var ring = new GameObject("HintRing").AddComponent<SpriteRenderer>();
        ring.transform.SetParent(transform, false); // goes away with the level if it is reloaded mid-pulse
        ring.sprite = Art.Load("ui/hint_ring");
        ring.sortingOrder = 250;
        ring.transform.position = bodyRenderer.bounds.center;
        float ringScale = Art.ScaleForWidth(ring.sprite, SkyWidth * 2.4f);

        for (float t = 0; t < 1.8f && !docked; t += Time.deltaTime)
        {
            float pulse = Mathf.Abs(Mathf.Sin(t * 5f));
            body.localScale = skyScale * (1f + 0.22f * pulse);
            ring.transform.localScale = Vector3.one * ringScale * (0.85f + 0.3f * pulse);
            ring.color = new Color(1f, 1f, 1f, 1f - t / 1.8f * 0.6f);
            yield return null;
        }
        Destroy(ring.gameObject);
        if (!docked) body.localScale = skyScale;
        Hinting = false;
    }

    static void SetLineColor(LineRenderer lr, Color c)
    {
        lr.startColor = c;
        lr.endColor = c;
    }
}
