using UnityEngine;

// Shapes generated at runtime for things that have no art file (badges, sky decor, win rays).
public static class SpriteFactory
{
    static Sprite circle, glow, cloud, rays;

    // Anti-aliased white disc; tint it with the Image color.
    public static Sprite Circle => circle ??= MakeCircle(128);

    // Soft radial light: full at the middle, fading to nothing at the edge (sun, moon halo).
    public static Sprite Glow => glow ??= Make(128, 128, (u, v) =>
    {
        float d = Mathf.Clamp01(Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f)) * 2f);
        return (1f - d) * (1f - d);
    });

    // A fluffy cloud from a few soft puffs on a flat base.
    public static Sprite Cloud => cloud ??= Make(256, 128, (u, v) =>
    {
        var puffs = new[] { (0.22f, 0.38f, 0.17f), (0.40f, 0.52f, 0.24f), (0.62f, 0.50f, 0.21f), (0.80f, 0.38f, 0.15f), (0.50f, 0.32f, 0.22f) };
        float a = 0f;
        foreach (var (x, y, r) in puffs)
        {
            float d = Vector2.Distance(new Vector2(u, v * 0.5f), new Vector2(x, y * 0.5f)) / r;
            a = Mathf.Max(a, Mathf.Clamp01((1f - d) * 3.5f));
        }
        return a * Mathf.Clamp01((v - 0.12f) * 6f); // flat-ish bottom
    });

    // Sunburst for the win screen: 14 soft wedges fading out from the middle.
    public static Sprite Rays => rays ??= Make(512, 512, (u, v) =>
    {
        Vector2 p = new Vector2(u - 0.5f, v - 0.5f);
        float d = p.magnitude * 2f;
        if (d > 1f) return 0f;
        float wedge = Mathf.Cos(Mathf.Atan2(p.y, p.x) * 14f) * 0.5f + 0.5f;
        return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((wedge - 0.35f) * 3f)) * (1f - d) * Mathf.Clamp01(d * 4f);
    });

    // White texture whose alpha comes from alpha(u, v), u and v in 0..1.
    static Sprite Make(int w, int h, System.Func<float, float, float> alpha)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color32[w * h];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
            pixels[y * w + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(alpha((x + 0.5f) / w, (y + 0.5f) / h)) * 255f));
        tex.SetPixels32(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
    }

    static Sprite MakeCircle(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        var pixels = new Color32[size * size];
        float r = size * 0.5f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));
            byte a = (byte)(Mathf.Clamp01(r - d) * 255f);
            pixels[y * size + x] = new Color32(255, 255, 255, a);
        }
        tex.SetPixels32(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
    }
}
