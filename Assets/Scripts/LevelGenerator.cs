using System.Collections.Generic;
using UnityEngine;

// Builds level N from a fixed seed, so every player gets the same level N.
// Every level it returns has been checked with PuzzleModel.Solve.
public static class LevelGenerator
{
    const int AttemptsPerSlotCount = 250;
    const int RandomPlays = 300;
    const float FieldWidth = 5.2f;  // approximate world size of the kite field, used for spacing only
    const float FieldHeight = 6f;
    const float MinKiteGap = 0.8f;

    struct Settings
    {
        public int colors;
        public int slots;
        public float maxRandomWin; // share of random (but legal) plays allowed to win; lower = more thinking
    }

    static Settings For(int level)
    {
        if (level <= 1) return new Settings { colors = 2, slots = 5, maxRandomWin = 1f }; // tutorial
        if (level == 2) return new Settings { colors = 3, slots = 4, maxRandomWin = 0.6f };
        if (level <= 4) return new Settings { colors = 4, slots = 4, maxRandomWin = 0.4f };
        if (level <= 7) return new Settings { colors = 5, slots = 4, maxRandomWin = 0.3f };
        if (level <= 11) return new Settings { colors = 5, slots = 4, maxRandomWin = 0.2f };
        if (level <= 19) return new Settings { colors = 5, slots = 3, maxRandomWin = 0.12f };
        return new Settings { colors = 5, slots = 3, maxRandomWin = 0.06f };
    }

    public static LevelDefs.LevelData Generate(int level)
    {
        var settings = For(level);
        int kiteCount = settings.colors * PuzzleModel.ChhatSize;

        // Keep the hardest solvable layout seen. If none is solvable with this many slots, allow one more.
        for (int slots = settings.slots; ; slots++)
        {
            LevelDefs.LevelData best = null;
            float bestWin = float.MaxValue;
            for (int attempt = 0; attempt < AttemptsPerSlotCount; attempt++)
            {
                var rng = new System.Random(level * 7919 + slots * 104729 + attempt);
                var data = Build(level, kiteCount, settings.colors, slots, rng);
                if (PuzzleModel.Solve(new PuzzleModel(data)) == null) continue;
                if (level >= 3 && ObviousPlayWins(data)) continue;

                float win = RandomWinRate(data, new System.Random(level * 31 + attempt));
                if (win < bestWin) { best = data; bestWin = win; }
                if (win <= settings.maxRandomWin) return data;
            }
            if (best != null) return best;
        }
    }

    // Plays the level many times, each time tapping a random kite that is untangled and has somewhere to go.
    // A high win rate means the player doesn't need to think.
    public static float RandomWinRate(LevelDefs.LevelData data, System.Random rng)
    {
        int wins = 0;
        var options = new List<int>();
        for (int play = 0; play < RandomPlays; play++)
        {
            var m = new PuzzleModel(data);
            while (!m.Won)
            {
                options.Clear();
                for (int k = 0; k < m.KiteCount; k++)
                    if (m.IsUntangled(k) && m.CanPlace(k)) options.Add(k);
                if (options.Count == 0) break;
                m.Free(options[rng.Next(options.Count)]);
            }
            if (m.Won) wins++;
        }
        return wins / (float)RandomPlays;
    }

    static LevelDefs.LevelData Build(int level, int kiteCount, int colorCount, int slots, System.Random rng)
    {
        var colorIds = new List<int>();
        for (int c = 0; c < colorCount; c++)
        for (int i = 0; i < PuzzleModel.ChhatSize; i++)
            colorIds.Add(c);
        Shuffle(colorIds, rng);

        var spoolOrder = new List<int>();
        var layers = new List<int>();
        for (int i = 0; i < kiteCount; i++) { spoolOrder.Add(i); layers.Add(i); }
        Shuffle(spoolOrder, rng);
        Shuffle(layers, rng);

        var anchors = PlaceKites(kiteCount, rng);
        var kites = new LevelDefs.KiteDef[kiteCount];
        var curves = new Vector2[kiteCount][];
        for (int i = 0; i < kiteCount; i++)
        {
            float spoolX = 0.04f + 0.92f * spoolOrder[i] / (kiteCount - 1f);
            kites[i] = new LevelDefs.KiteDef
            {
                colorId = colorIds[i],
                layer = layers[i],
                spool = new Vector2(spoolX, 0f),
                anchor = anchors[i],
                bend = new Vector2(Range(rng, -0.08f, 0.08f), 0f),
            };
            curves[i] = StringGeometry.Sample(kites[i].spool, kites[i].anchor, kites[i].bend);
        }

        var blockerMask = new int[kiteCount];
        for (int i = 0; i < kiteCount; i++)
        for (int j = i + 1; j < kiteCount; j++)
        {
            if (!StringGeometry.Cross(curves[i], curves[j])) continue;
            if (kites[i].layer > kites[j].layer) blockerMask[j] |= 1 << i;
            else blockerMask[i] |= 1 << j;
        }

        var queue = new List<int>();
        for (int c = 0; c < colorCount; c++) queue.Add(c);
        Shuffle(queue, rng);

        return new LevelDefs.LevelData
        {
            number = level,
            kites = kites,
            blockerMask = blockerMask,
            queue = queue.ToArray(),
            slots = slots,
        };
    }

    // Random kite anchors in the upper part of the field, kept apart so kites don't overlap.
    static Vector2[] PlaceKites(int count, System.Random rng)
    {
        var result = new Vector2[count];
        float gap = MinKiteGap;
        for (int i = 0; i < count; i++)
        {
            for (int tries = 0; ; tries++)
            {
                if (tries > 200) { gap *= 0.9f; tries = 0; }
                var p = new Vector2(Range(rng, 0.06f, 0.94f), Range(rng, 0.45f, 1f));
                bool clear = true;
                for (int j = 0; j < i && clear; j++)
                {
                    var d = new Vector2((p.x - result[j].x) * FieldWidth, (p.y - result[j].y) * FieldHeight);
                    clear = d.magnitude >= gap;
                }
                if (clear) { result[i] = p; break; }
            }
        }
        return result;
    }

    // The play most beginners would try: free a kite of the needed color if one is untangled,
    // otherwise free the top-most kite. Levels this already solves are too easy.
    static bool ObviousPlayWins(LevelDefs.LevelData data)
    {
        var m = new PuzzleModel(data);
        while (!m.Won)
        {
            int pick = -1;
            for (int k = 0; k < m.KiteCount; k++)
            {
                if (!m.IsUntangled(k)) continue;
                if (data.kites[k].colorId == m.ActiveColor) { pick = k; break; }
                if (pick < 0 || data.kites[k].layer > data.kites[pick].layer) pick = k;
            }
            if (pick < 0 || !m.CanPlace(pick)) return false;
            m.Free(pick);
        }
        return true;
    }

    static float Range(System.Random rng, float min, float max) => min + (float)rng.NextDouble() * (max - min);

    static void Shuffle<T>(List<T> list, System.Random rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
