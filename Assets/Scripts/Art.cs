using System.Collections.Generic;
using UnityEngine;

// Loads the sprites and fonts from Assets/Resources and holds the design palette (MockUps/.../palette.json).
public static class Art
{
    // The mockups are drawn at 390 px wide; that width maps to the narrowest world width the board shows.
    public const float DesignWidth = 390f;
    public const float DesignPx = 6.2f / DesignWidth; // world units per design px

    public static readonly Color Ink = Hex("2A1A12");
    public static readonly Color Cream = Hex("FBF1DE");
    public static readonly Color Saffron = Hex("F2A12E");
    public static readonly Color SaffronShade = Hex("C77A12");
    public static readonly Color Roof = Hex("7A4A2E");
    public static readonly Color RoofEdge = Hex("9C6440");
    public static readonly Color Danger = Hex("B83A26");
    public static readonly Color Muted = Hex("6B5444");
    public static readonly Color StringShadow = new Color(42 / 255f, 26 / 255f, 18 / 255f, 0.16f);

    static readonly Dictionary<string, Sprite> sprites = new();
    static Font display, body, icons;

    public static Font Display => display ??= Resources.Load<Font>("Fonts/Baloo2-ExtraBold");
    public static Font Body => body ??= Resources.Load<Font>("Fonts/Baloo2-SemiBold");
    public static Font Icons => icons ??= Resources.Load<Font>("Fonts/MaterialSymbolsRounded-Subset");

    public static Sprite Load(string path)
    {
        if (!sprites.TryGetValue(path, out var sprite))
        {
            sprite = Resources.Load<Sprite>("Art/" + path);
            if (sprite == null) Debug.LogError("[Art] Missing sprite: " + path);
            sprites[path] = sprite;
        }
        return sprite;
    }

    public static Sprite KiteSprite(int colorId) => Load("kites/kite_" + LevelDefs.ColorNames[colorId]);
    public static Sprite RooftopSprite(int colorId) => Load("rooftops/rooftop_" + LevelDefs.ColorNames[colorId]);
    public static Sprite SpoolSprite(int colorId) => Load("spools/spool_" + LevelDefs.ColorNames[colorId]);

    // Uniform scale that makes the sprite `width` world units wide.
    public static float ScaleForWidth(Sprite sprite, float width) => width / sprite.bounds.size.x;

    public static Color Hex(string hex)
    {
        ColorUtility.TryParseHtmlString("#" + hex, out var c);
        return c;
    }
}

// Material Symbols Rounded code points. Only these glyphs are in the subset font
// (regenerate it with pyftsubset if you need more icons).
public static class Icon
{
    public const string Heart = "";
    public const string HeartBroken = "";
    public const string Hint = "";
    public const string Restart = "";
    public const string Settings = "";
    public const string Home = "";
    public const string Lock = "";
    public const string Star = "";
    public const string Play = "";
    public const string Back = "";
    public const string Close = "";
    public const string SoundOn = "";
    public const string SoundOff = "";
    public const string Music = "";
    public const string MusicOff = "";
    public const string Vibration = "";
    public const string VibrationOff = "";
    public const string Shop = "";
    public const string Add = "";
    public const string Check = "";
    public const string Pause = "";
    public const string Map = "";
    public const string Next = "";
    public const string Ad = "";
    public const string AddSlot = "";
    public const string Info = "";
}
