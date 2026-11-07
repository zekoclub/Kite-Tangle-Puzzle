using System.Collections.Generic;
using UnityEngine;

// Life in the sky behind the kites: a sun glow and drifting clouds by day, warm clouds at dusk,
// a moon and twinkling stars at night. Pure decoration (sorting -99..-95, behind every string).
public class SkyDecor : MonoBehaviour
{
    struct Cloud { public Transform t; public float speed, width; }

    readonly List<Cloud> clouds = new();
    readonly List<(SpriteRenderer sr, float phase, float alpha)> stars = new();
    float halfW;
    string shown;
    Vector2 shownSize;

    public void Show(string sky, float halfWidth, float halfHeight)
    {
        if (sky == shown && shownSize == new Vector2(halfWidth, halfHeight)) return;
        shown = sky;
        shownSize = new Vector2(halfWidth, halfHeight);
        halfW = halfWidth;
        foreach (Transform child in transform) Destroy(child.gameObject);
        clouds.Clear();
        stars.Clear();
        var rng = new System.Random(sky == "sky_day" ? 11 : sky == "sky_dusk" ? 23 : 37); // same layout every time
        float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
        float h = halfHeight;

        switch (sky)
        {
            case "sky_day":
                Sprite("Sun", SpriteFactory.Glow, new Vector2(halfW * 0.55f, h * 0.55f), 6f, new Color(1f, 0.97f, 0.82f, 0.7f), -99);
                for (int i = 0; i < 5; i++)
                    AddCloud(new Vector2(R(-halfW, halfW), R(-h * 0.25f, h * 0.75f)), R(2.0f, 3.6f), new Color(1f, 1f, 1f, R(0.55f, 0.85f)), R(0.05f, 0.13f));
                break;
            case "sky_dusk":
                Sprite("Sun", SpriteFactory.Glow, new Vector2(-halfW * 0.35f, -h * 0.2f), 9f, new Color(1f, 0.78f, 0.45f, 0.55f), -99);
                for (int i = 0; i < 4; i++)
                    AddCloud(new Vector2(R(-halfW, halfW), R(-h * 0.1f, h * 0.7f)), R(2.2f, 3.8f), new Color(1f, 0.86f, 0.78f, R(0.45f, 0.7f)), R(0.04f, 0.1f));
                break;
            default: // night
                Sprite("MoonGlow", SpriteFactory.Glow, new Vector2(halfW * 0.62f, h * 0.28f), 3.4f, new Color(0.85f, 0.9f, 1f, 0.35f), -98);
                Sprite("Moon", SpriteFactory.Circle, new Vector2(halfW * 0.62f, h * 0.28f), 0.95f, Art.Hex("FFF4D6"), -96);
                Sprite("MoonShade", SpriteFactory.Circle, new Vector2(halfW * 0.62f + 0.13f, h * 0.28f + 0.07f), 0.72f, new Color(0.93f, 0.86f, 0.7f, 0.35f), -95);
                for (int i = 0; i < 46; i++)
                {
                    float size = R(0.035f, 0.09f), alpha = R(0.4f, 0.95f);
                    var sr = Sprite("Star", SpriteFactory.Circle, new Vector2(R(-halfW, halfW), R(-h * 0.3f, h)), size, new Color(1f, 1f, 0.95f, alpha), -97);
                    stars.Add((sr, R(0f, 6.28f), alpha));
                }
                for (int i = 0; i < 2; i++)
                    AddCloud(new Vector2(R(-halfW, halfW), R(0f, h * 0.5f)), R(2.6f, 3.8f), new Color(0.55f, 0.6f, 0.85f, 0.18f), R(0.03f, 0.06f));
                break;
        }
    }

    SpriteRenderer Sprite(string name, Sprite sprite, Vector2 pos, float width, Color color, int order)
    {
        var sr = new GameObject(name).AddComponent<SpriteRenderer>();
        sr.transform.SetParent(transform, false);
        sr.sprite = sprite;
        sr.color = color;
        sr.sortingOrder = order;
        sr.transform.localPosition = pos;
        sr.transform.localScale = Vector3.one * Art.ScaleForWidth(sprite, width);
        return sr;
    }

    void AddCloud(Vector2 pos, float width, Color color, float speed)
    {
        var sr = Sprite("Cloud", SpriteFactory.Cloud, pos, width, color, -95);
        clouds.Add(new Cloud { t = sr.transform, speed = speed, width = width });
    }

    void Update()
    {
        foreach (var c in clouds)
        {
            var p = c.t.localPosition;
            p.x += c.speed * Time.deltaTime;
            if (p.x - c.width * 0.5f > halfW) p.x = -halfW - c.width * 0.5f;
            c.t.localPosition = p;
        }
        float t = Time.time;
        foreach (var (sr, phase, alpha) in stars)
        {
            var col = sr.color;
            col.a = alpha * (0.6f + 0.4f * Mathf.Sin(t * 1.7f + phase));
            sr.color = col;
        }
    }
}
