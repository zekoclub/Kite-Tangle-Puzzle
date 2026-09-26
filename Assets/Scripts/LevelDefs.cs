using UnityEngine;

// Level data shared by the generator, the puzzle model and the board.
// Positions are normalized: x 0..1 across the field, y 0 = charkhi row, 1 = highest kite anchor.
// Only straight-line scaling maps them to the screen, so string crossings stay the same on every phone.
public static class LevelDefs
{
    public static readonly Color[] Palette =
    {
        new Color(0.90f, 0.20f, 0.22f), // red
        new Color(0.20f, 0.42f, 0.95f), // blue
        new Color(0.20f, 0.75f, 0.32f), // green
        new Color(1.00f, 0.78f, 0.10f), // yellow
        new Color(0.62f, 0.30f, 0.86f), // purple
    };

    public struct KiteDef
    {
        public int colorId;
        public int layer;       // higher layer = string lies on top
        public Vector2 spool;   // charkhi
        public Vector2 anchor;  // where the string meets the kite
        public Vector2 bend;    // pushes the string curve sideways
    }

    public class LevelData
    {
        public int number;
        public KiteDef[] kites;
        public int[] blockerMask; // bit j set = kite j's string lies on top of kite i's string
        public int[] queue;       // chhat colors in arrival order; each chhat takes PuzzleModel.ChhatSize kites
        public int slots;         // waiting slots
    }
}
