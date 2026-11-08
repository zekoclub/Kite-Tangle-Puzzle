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

    // Special kites, each introduced on its own (a "How to play" card shows on its first level):
    // ice from IceFrom, hidden colors from HiddenFrom, double strings from TwinFrom, wind from WindFrom.
    public const int IceFrom = 21, HiddenFrom = 31, TwinFrom = 41, WindFrom = 51;

    struct Specials
    {
        public int ice, hidden, twins, windPairs;
    }

    static Specials SpecialsFor(int level) => new Specials
    {
        ice = level < IceFrom ? 0 : level < 35 ? 1 : level < 70 ? 2 : 3,
        hidden = level < HiddenFrom ? 0 : level < 60 ? 2 : 3,
        twins = level < TwinFrom ? 0 : level < 80 ? 1 : 2,
        windPairs = level < WindFrom ? 0 : level < 100 ? 1 : 2,
    };

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
        // This always ends: with a slot per kite every layout is solvable (the top-most string is never covered).
        for (int slots = settings.slots; ; slots++)
        {
            LevelDefs.LevelData best = null;
            float bestWin = float.MaxValue;
            for (int attempt = 0; attempt < AttemptsPerSlotCount; attempt++)
            {
                var rng = new System.Random(level * 7919 + slots * 104729 + attempt);
                var data = Build(level, kiteCount, settings.colors, slots, rng,
                    new System.Random(level * 6271 + slots * 337 + attempt * 13));
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

    // `special` is a separate random stream, so adding special kites never changed levels 1-20.
    static LevelDefs.LevelData Build(int level, int kiteCount, int colorCount, int slots, System.Random rng, System.Random special)
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

        var specials = SpecialsFor(level);
        var twinCurves = new Vector2[kiteCount][];
        if (specials.twins > 0) AddTwins(kites, twinCurves, curves, specials.twins, special);

        var crossMask = new int[kiteCount];
        for (int i = 0; i < kiteCount; i++)
        for (int j = i + 1; j < kiteCount; j++)
        {
            if (!Crosses(curves[i], twinCurves[i], curves[j], twinCurves[j])) continue;
            crossMask[i] |= 1 << j;
            crossMask[j] |= 1 << i;
        }
        var layerOf = new int[kiteCount];
        for (int i = 0; i < kiteCount; i++) layerOf[i] = kites[i].layer;
        var blockerMask = PuzzleModel.BlockersFrom(crossMask, layerOf);

        var queue = new List<int>();
        for (int c = 0; c < colorCount; c++) queue.Add(c);
        Shuffle(queue, rng);

        var data = new LevelDefs.LevelData
        {
            number = level,
            kites = kites,
            blockerMask = blockerMask,
            queue = queue.ToArray(),
            slots = slots,
        };
        if (level >= IceFrom) AddSpecials(data, crossMask, specials, special);
        return data;
    }

    static bool Crosses(Vector2[] a, Vector2[] a2, Vector2[] b, Vector2[] b2) =>
        StringGeometry.Cross(a, b) || (a2 != null && StringGeometry.Cross(a2, b)) ||
        (b2 != null && StringGeometry.Cross(a, b2)) || (a2 != null && b2 != null && StringGeometry.Cross(a2, b2));

    // Gives `count` kites a second string. All charkhis are re-spaced along the roof so the extra ones fit.
    static void AddTwins(LevelDefs.KiteDef[] kites, Vector2[][] twinCurves, Vector2[][] curves, int count, System.Random rng)
    {
        int n = kites.Length, spools = n + count;
        var order = new List<int>();
        for (int i = 0; i < spools; i++) order.Add(i);
        Shuffle(order, rng);
        float X(int slot) => 0.04f + 0.92f * slot / (spools - 1f);

        var twins = new List<int>();
        for (int i = 0; i < n; i++) twins.Add(i);
        Shuffle(twins, rng);
        twins.RemoveRange(count, n - count);

        for (int i = 0; i < n; i++)
        {
            kites[i].spool = new Vector2(X(order[i]), 0f);
            curves[i] = StringGeometry.Sample(kites[i].spool, kites[i].anchor, kites[i].bend);
        }
        for (int t = 0; t < count; t++)
        {
            int i = twins[t];
            kites[i].twin = true;
            kites[i].spool2 = new Vector2(X(order[n + t]), 0f);
            kites[i].bend2 = new Vector2(Range(rng, -0.08f, 0.08f), 0f);
            twinCurves[i] = StringGeometry.Sample(kites[i].spool2, kites[i].anchor, kites[i].bend2);
        }
    }

    // Ice on random kites, hidden colors on kites that start covered (an uncovered one would show at once),
    // and wind pairs on strings that cross.
    static void AddSpecials(LevelDefs.LevelData data, int[] crossMask, Specials specials, System.Random rng)
    {
        int n = data.kites.Length;
        data.crossMask = crossMask;
        var pool = new List<int>();
        for (int i = 0; i < n; i++) pool.Add(i);

        Shuffle(pool, rng);
        for (int t = 0; t < specials.ice && t < n; t++) data.ice |= 1 << pool[t];

        Shuffle(pool, rng);
        int hidden = 0;
        foreach (int k in pool)
            if (hidden < specials.hidden && data.blockerMask[k] != 0) { data.hidden |= 1 << k; hidden++; }

        var pairs = new List<int>();
        int used = 0;
        Shuffle(pool, rng);
        foreach (int a in pool)
        {
            if (pairs.Count / 2 >= specials.windPairs) break;
            if ((used & (1 << a)) != 0) continue;
            for (int b = 0; b < n; b++)
            {
                if (b == a || (used & (1 << b)) != 0 || (crossMask[a] & (1 << b)) == 0) continue;
                pairs.Add(a);
                pairs.Add(b);
                used |= (1 << a) | (1 << b);
                break;
            }
        }
        if (pairs.Count > 0) data.wind = pairs.ToArray();
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
                if (pick < 0 || m.LayerOf(k) > m.LayerOf(pick)) pick = k;
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
