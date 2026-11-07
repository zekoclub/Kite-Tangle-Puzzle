using System;
using System.Collections;
using UnityEngine;

// One kite: its body in the sky, its string (3 passes so crossings read clearly) and its charkhi.
// The string is a dark outline in its own color, the cord, and a thin shine, so it reads as a round dyed
// cord on every sky (a light outline glowed like a white border on the dusk and night skies).
// Once freed, the body flies to a dock (a chhat spot or a waiting slot) and follows it.
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

    public int Id { get; private set; }
    public int Layer { get; private set; }

    Transform body;
    SpriteRenderer bodyRenderer, shadowRenderer;
    Collider2D bodyCollider;
    LineRenderer outline, cord, shine, stringShadow;
    SpriteRenderer spool;
    Color outlineColor, cordColor, shineColor;
    Vector3 skyScale, dockScale;
    Coroutine flashRoutine;
    bool docked;

    static Material lineMaterial;

    public void Init(int id, LevelDefs.KiteDef def, Func<Vector2, Vector2> toWorld, float spoolWidth)
    {
        Id = id;
        Layer = def.layer;
        cordColor = LevelDefs.Palette[def.colorId];
        shineColor = LevelDefs.PaletteLight[def.colorId];
        outlineColor = Color.Lerp(LevelDefs.PaletteDark[def.colorId], Art.Ink, 0.45f);

        var sprite = Art.KiteSprite(def.colorId);
        skyScale = Vector3.one * Art.ScaleForWidth(sprite, SkyWidth);
        dockScale = Vector3.one * Art.ScaleForWidth(sprite, DockWidth);

        // The kite sprite's pivot is where the string meets it, so the body sits right on the anchor.
        body = new GameObject("Body").transform;
        body.SetParent(transform, false);
        body.position = toWorld(def.anchor);
        body.localScale = skyScale;
        bodyRenderer = body.gameObject.AddComponent<SpriteRenderer>();
        bodyRenderer.sprite = sprite;

        var shadow = new GameObject("Shadow").transform;
        shadow.SetParent(body, false);
        shadow.localPosition = KiteShadowOffset / skyScale.x;
        shadowRenderer = shadow.gameObject.AddComponent<SpriteRenderer>();
        shadowRenderer.sprite = Art.Load("kites/kite_shadow");
        SetBodyOrder(200 + Layer * 2);

        // Generous tap area for fingers, centered on the diamond (in sprite space).
        var circle = body.gameObject.AddComponent<CircleCollider2D>();
        circle.offset = new Vector2(0f, sprite.bounds.size.y * 0.375f);
        circle.radius = sprite.bounds.size.x * 0.65f;
        bodyCollider = circle;

        Vector2[] curve = StringGeometry.Sample(def.spool, def.anchor, def.bend);
        var points = new Vector3[curve.Length];
        for (int i = 0; i < curve.Length; i++) points[i] = toWorld(curve[i]);
        stringShadow = MakeLine("StringShadow", points, OutlineWidth, -5, Art.StringShadow);
        stringShadow.useWorldSpace = false; // shifted as a whole; the string points themselves stay put
        stringShadow.transform.position = StringShadowOffset;
        outline = MakeLine("StringOutline", points, OutlineWidth, Layer * 3, outlineColor);
        cord = MakeLine("String", points, CordWidth, Layer * 3 + 1, cordColor);
        shine = MakeLine("StringShine", points, ShineWidth, Layer * 3 + 2, shineColor);

        var spoolGo = new GameObject("Charkhi");
        spoolGo.transform.SetParent(transform, false);
        spool = spoolGo.AddComponent<SpriteRenderer>();
        spool.sprite = Art.SpoolSprite(def.colorId);
        spoolGo.transform.position = toWorld(def.spool);
        spoolGo.transform.localScale = Vector3.one * Art.ScaleForWidth(spool.sprite, spoolWidth);
        spool.sortingOrder = 150;
    }

    void SetBodyOrder(int order)
    {
        bodyRenderer.sortingOrder = order;
        shadowRenderer.sortingOrder = order - 1;
    }

    LineRenderer MakeLine(string name, Vector3[] points, float width, int order, Color color)
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
        lr.sortingOrder = order;
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
            bodyCollider.enabled = false;
            if (flashRoutine != null) StopCoroutine(flashRoutine);
            SetBodyOrder(400);
            StartCoroutine(ReleaseString());
        }
        body.SetParent(dock, true);
    }

    void Update()
    {
        if (!docked) return;
        float t = 1f - Mathf.Exp(-8f * Time.deltaTime);
        body.localPosition = Vector3.Lerp(body.localPosition, Vector3.zero, t);
        body.localRotation = Quaternion.Slerp(body.localRotation, Quaternion.identity, t);
        body.localScale = Vector3.Lerp(body.localScale, dockScale, t);
    }

    void OnDestroy()
    {
        // A docked body lives under a chhat or slot, not under this object.
        if (body != null && body.parent != transform) Destroy(body.gameObject);
    }

    IEnumerator ReleaseString()
    {
        var lines = new[] { stringShadow, outline, cord, shine };
        var start = Array.ConvertAll(lines, l => l.startColor);
        Color spoolColor = spool.color;
        for (float t = 0; t < 0.4f; t += Time.deltaTime)
        {
            float a = 1f - t / 0.4f;
            for (int i = 0; i < lines.Length; i++)
                SetLineColor(lines[i], new Color(start[i].r, start[i].g, start[i].b, start[i].a * a));
            spool.color = new Color(spoolColor.r, spoolColor.g, spoolColor.b, Mathf.Lerp(0.35f, 1f, a));
            yield return null;
        }
        foreach (var line in lines) line.enabled = false;
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

    // Blink this string white so the player sees what is lying on top.
    public void Flash()
    {
        if (flashRoutine != null) StopCoroutine(flashRoutine);
        flashRoutine = StartCoroutine(FlashRoutine());
    }

    IEnumerator FlashRoutine()
    {
        for (int i = 0; i < 3; i++)
        {
            SetLineColor(cord, Color.white);
            SetLineColor(shine, Color.white);
            outline.widthMultiplier = OutlineWidth * 1.7f;
            cord.widthMultiplier = CordWidth * 1.8f;
            yield return new WaitForSeconds(0.12f);
            SetLineColor(cord, cordColor);
            SetLineColor(shine, shineColor);
            outline.widthMultiplier = OutlineWidth;
            cord.widthMultiplier = CordWidth;
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
