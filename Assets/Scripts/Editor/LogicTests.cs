using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

// Checks the rules (PuzzleModel) and the generator without the Editor UI.
// Menu: Kite Tangle > Logic Tests. Batch: -executeMethod LogicTests.Run [-levels 40]; exits 1 on any failure.
// Every result line is tagged [LogicTests]; the last one says "N checks, M failed".
public static class LogicTests
{
    static int checks, failed;

    [MenuItem("Kite Tangle/Logic Tests")]
    public static void RunFromMenu() => RunAll(40);

    public static void Run()
    {
        var args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, "-levels");
        int levels = i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out int n) ? n : 40;
        bool ok = RunAll(levels);
        if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
    }

    static bool RunAll(int levels)
    {
        checks = failed = 0;
        RulesOnHandMadeLevel();
        GeneratedLevels(levels);
        BakedLevels(levels);
        Debug.Log($"[LogicTests] {checks} checks, {failed} failed");
        return failed == 0;
    }

    static void Check(bool condition, string what)
    {
        checks++;
        if (condition) return;
        failed++;
        Debug.LogError("[LogicTests] FAIL: " + what);
    }

    // Kites 0,1,2 red; 3,4,5 blue. Kite 1's string lies on kite 0's. Queue: red then blue. 1 slot.
    static LevelDefs.LevelData HandMade(int slots)
    {
        var kites = new LevelDefs.KiteDef[6];
        for (int k = 0; k < 6; k++) kites[k] = new LevelDefs.KiteDef { colorId = k < 3 ? 0 : 1, layer = k };
        var mask = new int[6];
        mask[0] = 1 << 1;
        return new LevelDefs.LevelData { number = 0, kites = kites, blockerMask = mask, queue = new[] { 0, 1 }, slots = slots };
    }

    static void RulesOnHandMadeLevel()
    {
        var m = new PuzzleModel(HandMade(1));
        Check(!m.IsUntangled(0), "a covered kite is not untangled");
        Check(m.IsUntangled(1), "the kite on top is untangled");

        m.Free(3); // blue while red is active -> slot
        Check(m.Slots.Count == 1 && m.Slots[0] == 3, "a wrong color waits in a slot");
        Check(!m.CanPlace(4), "no space: wrong color with every slot full");
        Check(m.CanPlace(2), "a matching color always fits");

        m.Free(1);
        Check(m.IsUntangled(0), "freeing the kite on top uncovers the one below");
        m.Free(0);
        m.Free(2); // red chhat full -> blue arrives and takes kite 3 from the slot
        Check(m.QueueIndex == 1, "a full chhat leaves");
        Check(m.Slots.Count == 0 && m.KiteChhat[3] == 1, "the next chhat takes its kites from the slots");
        m.Free(4);
        m.Free(5);
        Check(m.Won, "clearing every chhat wins");

        Check(PuzzleModel.Solve(new PuzzleModel(HandMade(1))) != null, "the hand-made level is solvable");

        // Blue chhat first, no slots, and every blue kite lies under red kite 0: no legal move at all.
        var stuck = HandMade(0);
        stuck.queue = new[] { 1, 0 };
        stuck.blockerMask = new int[6];
        for (int k = 3; k < 6; k++) stuck.blockerMask[k] = 1 << 0; // every blue lies under red kite 0
        Check(PuzzleModel.Solve(new PuzzleModel(stuck)) == null, "the solver reports an impossible level");

        var clone = new PuzzleModel(HandMade(1));
        var copy = clone.Clone();
        copy.Free(1);
        Check(!clone.IsFreed(1), "Clone doesn't share state");
    }

    static void GeneratedLevels(int levels)
    {
        long slowest = 0;
        int slowestLevel = 0;
        for (int n = 1; n <= levels; n++)
        {
            var sw = Stopwatch.StartNew();
            var level = LevelGenerator.Generate(n);
            long ms = sw.ElapsedMilliseconds;
            if (ms > slowest) { slowest = ms; slowestLevel = n; }

            Check(level.number == n, $"L{n}: number");
            Check(level.kites.Length == level.queue.Length * PuzzleModel.ChhatSize, $"L{n}: 3 kites per chhat");
            Check(level.kites.Length <= 31, $"L{n}: fits the 32-bit masks");
            foreach (int color in level.queue)
                Check(level.kites.Count(k => k.colorId == color) == PuzzleModel.ChhatSize, $"L{n}: 3 kites of color {color}");
            Check(level.queue.Distinct().Count() == level.queue.Length, $"L{n}: one chhat per color");
            Check(level.kites.Select(k => k.layer).Distinct().Count() == level.kites.Length, $"L{n}: layers are unique");
            for (int k = 0; k < level.kites.Length; k++)
                Check((level.blockerMask[k] & (1 << k)) == 0, $"L{n}: kite {k} doesn't block itself");

            // Replay the solver's answer move by move with the real rules.
            var path = PuzzleModel.Solve(new PuzzleModel(level));
            Check(path != null, $"L{n}: solvable");
            if (path != null)
            {
                var m = new PuzzleModel(level);
                bool legal = true;
                foreach (int k in path)
                {
                    legal &= m.IsUntangled(k) && m.CanPlace(k);
                    m.Free(k);
                }
                Check(legal && m.Won, $"L{n}: the solution is legal and wins");
            }

            if (n <= 10) Check(Same(level, LevelGenerator.Generate(n)), $"L{n}: generation is deterministic");
        }
        Debug.Log($"[LogicTests] Levels 1-{levels} generated; slowest L{slowestLevel} took {slowest} ms");
    }

    // The shipped levels must be exactly what the generator makes today (rules data), and all solvable.
    static void BakedLevels(int compareUpTo)
    {
        var file = Resources.Load<TextAsset>(LevelCache.BakedPath);
        Check(file != null, "baked levels exist (run Kite Tangle > Bake Levels)");
        if (file == null) return;
        var pack = JsonUtility.FromJson<LevelDefs.LevelPack>(file.text);
        for (int i = 0; i < pack.levels.Length; i++)
        {
            var level = pack.levels[i];
            Check(level.number == i + 1, $"baked L{i + 1}: levels are in order with no gaps");
            Check(PuzzleModel.Solve(new PuzzleModel(level)) != null, $"baked L{level.number}: solvable");
            if (level.number <= compareUpTo)
                Check(SameRules(level, LevelGenerator.Generate(level.number)),
                    $"baked L{level.number}: matches the generator (stale? re-run Kite Tangle > Bake Levels)");
        }
        Debug.Log($"[LogicTests] {pack.levels.Length} baked levels checked");
    }

    static bool SameRules(LevelDefs.LevelData a, LevelDefs.LevelData b) =>
        a.slots == b.slots && a.queue.SequenceEqual(b.queue) && a.blockerMask.SequenceEqual(b.blockerMask) &&
        a.kites.Select(k => (k.colorId, k.layer)).SequenceEqual(b.kites.Select(k => (k.colorId, k.layer))) &&
        a.kites.Zip(b.kites, (x, y) => (x.anchor - y.anchor).sqrMagnitude + (x.spool - y.spool).sqrMagnitude).All(d => d < 1e-8f);

    static bool Same(LevelDefs.LevelData a, LevelDefs.LevelData b) =>
        a.slots == b.slots && a.queue.SequenceEqual(b.queue) && a.blockerMask.SequenceEqual(b.blockerMask) &&
        a.kites.Select(k => (k.colorId, k.layer, k.spool, k.anchor, k.bend))
            .SequenceEqual(b.kites.Select(k => (k.colorId, k.layer, k.spool, k.anchor, k.bend)));
}
