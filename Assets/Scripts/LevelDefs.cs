using System;
using UnityEngine;

// Level data shared by the generator, the puzzle model and the board.
// Positions are normalized: x 0..1 across the field, y 0 = charkhi row, 1 = highest kite anchor.
// Only straight-line scaling maps them to the screen, so string crossings stay the same on every phone.
public static class LevelDefs
{
    // Indexed by colorId. Names match the sprite files (kite_red, rooftop_red, spool_red...).
    public static readonly string[] ColorNames = { "red", "blue", "green", "yellow", "magenta" };

    public static readonly Color[] Palette =
    {
        Art.Hex("E0533D"), // red
        Art.Hex("3D86D9"), // blue
        Art.Hex("3FA66A"), // green
        Art.Hex("F2B831"), // yellow
        Art.Hex("C9509A"), // magenta
    };

    // Darker shade of each color; mixed with Ink it is a string's outline.
    public static readonly Color[] PaletteDark =
    {
        Art.Hex("B8392A"),
        Art.Hex("2B64A8"),
        Art.Hex("2C7A4C"),
        Art.Hex("C99018"),
        Art.Hex("9E3A77"),
    };

    // Lighter tint of each color (palette.json "light"), the shine line along a string.
    public static readonly Color[] PaletteLight =
    {
        Art.Hex("F08672"),
        Art.Hex("74A9EA"),
        Art.Hex("6CC390"),
        Art.Hex("F8D170"),
        Art.Hex("DE7FB8"),
    };

    [Serializable]
    public struct KiteDef
    {
        public int colorId;
        public int layer;       // higher layer = string lies on top
        public Vector2 spool;   // charkhi
        public Vector2 anchor;  // where the string meets the kite
        public Vector2 bend;    // pushes the string curve sideways
    }

    [Serializable]
    public class LevelData
    {
        public int number;
        public KiteDef[] kites;
        public int[] blockerMask; // bit j set = kite j's string lies on top of kite i's string
        public int[] queue;       // chhat colors in arrival order; each chhat takes PuzzleModel.ChhatSize kites
        public int slots;         // waiting slots
    }

    // Levels pre-built in the editor (Kite Tangle > Bake Levels) and shipped as Resources/Levels/levels.json.
    [Serializable]
    public class LevelPack
    {
        public LevelData[] levels;
    }
}
