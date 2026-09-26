using System;
using System.Collections;
using UnityEngine;

// One kite: its body in the sky, its string (with a dark outline so crossings read clearly) and its charkhi.
// Once freed, the body flies to a dock (a chhat spot or a waiting slot) and follows it.
public class Kite : MonoBehaviour
{
    const float StringWidth = 0.08f;
    const float OutlineWidth = 0.14f;
    const float BodyScale = 0.6f;
    const float DockScale = 0.45f;
    const float BodyAboveAnchor = BodyScale * 0.5f; // the string attaches to the bottom tip
    static readonly Vector3 BodyShape = new Vector3(0.75f, 1f, 1f);

    public int Id { get; private set; }
    public int Layer { get; private set; }

    Transform body;
    SpriteRenderer bodyRenderer, stickV, stickH;
    Collider2D bodyCollider;
    LineRenderer line, outline;
    SpriteRenderer spool;
    Color stringColor;
    Coroutine flashRoutine;
    bool docked;

    public void Init(int id, LevelDefs.KiteDef def, Func<Vector2, Vector2> toWorld)
    {
        Id = id;
        Layer = def.layer;
        Color color = LevelDefs.Palette[def.colorId];
        stringColor = Color.Lerp(color, Color.black, 0.2f);

        Vector2 anchor = toWorld(def.anchor);

        body = new GameObject("Body").transform;
        body.SetParent(transform, false);
        body.position = anchor + Vector2.up * BodyAboveAnchor;
        body.localScale = BodyShape * BodyScale;
        bodyRenderer = body.gameObject.AddComponent<SpriteRenderer>();
        bodyRenderer.sprite = SpriteFactory.Diamond;
        bodyRenderer.color = color;
        stickV = AddStick(new Vector3(0.05f, 1f, 1f));
        stickH = AddStick(new Vector3(1f, 0.05f, 1f));
        SetBodyOrder(200 + Layer * 2);
        var circle = body.gameObject.AddComponent<CircleCollider2D>();
        circle.radius = 0.75f; // generous tap area for fingers (in body space)
        bodyCollider = circle;

        Vector2[] curve = StringGeometry.Sample(def.spool, def.anchor, def.bend);
        var points = new Vector3[curve.Length];
        for (int i = 0; i < curve.Length; i++) points[i] = toWorld(curve[i]);
        outline = MakeLine("Outline", points, OutlineWidth, Layer * 3);
        line = MakeLine("String", points, StringWidth, Layer * 3 + 1);
        SetLineColor(outline, new Color(0.1f, 0.1f, 0.15f, 0.8f));
        SetLineColor(line, stringColor);

        var spoolGo = new GameObject("Charkhi");
        spoolGo.transform.SetParent(transform, false);
        spoolGo.transform.position = toWorld(def.spool);
        spoolGo.transform.localScale = Vector3.one * 0.3f;
        spool = spoolGo.AddComponent<SpriteRenderer>();
        spool.sprite = SpriteFactory.Circle;
        spool.color = color;
        spool.sortingOrder = 150;
    }

    SpriteRenderer AddStick(Vector3 scale)
    {
        var stick = new GameObject("Stick").AddComponent<SpriteRenderer>();
        stick.transform.SetParent(body, false);
        stick.transform.localScale = scale;
        stick.sprite = SpriteFactory.Pixel;
        stick.color = new Color(0f, 0f, 0f, 0.35f);
        return stick;
    }

    void SetBodyOrder(int order)
    {
        bodyRenderer.sortingOrder = order;
        stickV.sortingOrder = order + 1;
        stickH.sortingOrder = order + 1;
    }

    LineRenderer MakeLine(string name, Vector3[] points, float width, int order)
    {
        var lr = new GameObject(name).AddComponent<LineRenderer>();
        lr.transform.SetParent(transform, false);
        lr.useWorldSpace = true;
        lr.positionCount = points.Length;
        lr.SetPositions(points);
        lr.widthMultiplier = width;
        lr.numCapVertices = 3;
        lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.sortingOrder = order;
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
        body.localScale = Vector3.Lerp(body.localScale, BodyShape * DockScale, t);
    }

    void OnDestroy()
    {
        // A docked body lives under a chhat or slot, not under this object.
        if (body != null && body.parent != transform) Destroy(body.gameObject);
    }

    IEnumerator ReleaseString()
    {
        Color outlineStart = outline.startColor;
        for (float t = 0; t < 0.4f; t += Time.deltaTime)
        {
            float a = 1f - t / 0.4f;
            SetLineColor(line, new Color(stringColor.r, stringColor.g, stringColor.b, a));
            SetLineColor(outline, new Color(outlineStart.r, outlineStart.g, outlineStart.b, outlineStart.a * a));
            spool.color = new Color(spool.color.r, spool.color.g, spool.color.b, Mathf.Lerp(0.25f, 1f, a));
            yield return null;
        }
        line.enabled = false;
        outline.enabled = false;
    }

    public IEnumerator Shake()
    {
        Vector3 start = body.position;
        for (float t = 0; t < 0.35f; t += Time.deltaTime)
        {
            body.position = start + Vector3.right * Mathf.Sin(t * 60f) * 0.12f;
            yield return null;
        }
        body.position = start;
    }

    // Blink this string so the player sees what is lying on top.
    public void Flash()
    {
        if (flashRoutine != null) StopCoroutine(flashRoutine);
        flashRoutine = StartCoroutine(FlashRoutine());
    }

    IEnumerator FlashRoutine()
    {
        for (int i = 0; i < 3; i++)
        {
            SetLineColor(line, Color.white);
            line.widthMultiplier = StringWidth * 1.8f;
            yield return new WaitForSeconds(0.12f);
            SetLineColor(line, stringColor);
            line.widthMultiplier = StringWidth;
            yield return new WaitForSeconds(0.12f);
        }
        flashRoutine = null;
    }

    // Pulse the kite body to point the player at it.
    public IEnumerator HintPulse()
    {
        Vector3 baseScale = body.localScale;
        for (float t = 0; t < 1.5f && !docked; t += Time.deltaTime)
        {
            body.localScale = baseScale * (1f + 0.25f * Mathf.Abs(Mathf.Sin(t * 6f)));
            yield return null;
        }
        if (!docked) body.localScale = baseScale;
    }

    static void SetLineColor(LineRenderer lr, Color c)
    {
        lr.startColor = c;
        lr.endColor = c;
    }
}
