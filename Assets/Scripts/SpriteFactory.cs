using System;
using UnityEngine;

// Placeholder shapes generated at runtime so Stage 1 needs no art assets.
public static class SpriteFactory
{
    static Sprite diamond, circle, pixel;

    public static Sprite Diamond => diamond ??= Make(64, (u, v) => Mathf.Abs(u - 0.5f) + Mathf.Abs(v - 0.5f) <= 0.5f);
    public static Sprite Circle => circle ??= Make(64, (u, v) => (u - 0.5f) * (u - 0.5f) + (v - 0.5f) * (v - 0.5f) <= 0.25f);
    public static Sprite Pixel => pixel ??= Make(4, (u, v) => true);

    // Every sprite is 1 world unit wide; scale it on the transform.
    static Sprite Make(int size, Func<float, float, bool> inside)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float u = (x + 0.5f) / size, v = (y + 0.5f) / size;
            pixels[y * size + x] = inside(u, v) ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
        }
        tex.SetPixels32(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }
}
