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
        SpecialKites();
        GeneratedLevels(levels);
        GeneratedSpecials();
        BakedLevels(levels);
        SaveAndStats();
        Debug.Log($"[LogicTests] {checks} checks, {failed} failed");
        return failed == 0;
    }

    // Progress: stats, the save checksum and the cloud merge. Works on the Editor's own save, so it backs that up
    // first and puts it back afterwards.
    static void SaveAndStats()
    {
        var keys = new List<string> { "level", "coins", "hints", "sound", "vibration", "music", "sig", "sig_v",
            "st_played", "st_wins", "st_losses", "st_streak", "st_best_streak", "st_kites", "st_hints", "st_boosters",
            "bo_undo", "bo_cut", "bo_freeze", "skin", "skins", "no_ads", "free_day", "free_n" };
        for (int n = 1; n <= Math.Max(Progress.Level, 3) + 1; n++) { keys.Add("stars_" + n); keys.Add("tut_" + n); }
        var backup = keys.ToDictionary(k => k, k => PlayerPrefs.HasKey(k) ? (int?)PlayerPrefs.GetInt(k) : null);
        try
        {
            Progress.Reset();
            Check(Progress.Level == 1 && Progress.Coins == Progress.StartingCoins && Progress.Hints == Progress.StartingHints, "reset: starting level, coins and hints");
            Check(Enum.GetValues(typeof(Progress.Stat)).Cast<Progress.Stat>().All(st => Progress.Get(st) == 0), "reset: every stat is 0");

            Progress.RecordStart();
            int reward = Progress.CompleteLevel(1, 3);
            Check(reward == 25 && Progress.Coins == Progress.StartingCoins + 25, "first win pays 10 + 5 x stars");
            Check(Progress.Level == 2 && Progress.Stars(1) == 3, "a win unlocks the next level and keeps the stars");
            Check(Progress.Get(Progress.Stat.Played) == 1 && Progress.Get(Progress.Stat.Wins) == 1, "played and wins counted");
            Check(Progress.Get(Progress.Stat.Streak) == 1 && Progress.Get(Progress.Stat.BestStreak) == 1, "a win starts a streak");
            Progress.RecordLoss();
            Check(Progress.Get(Progress.Stat.Losses) == 1 && Progress.Get(Progress.Stat.Streak) == 0, "a loss is counted and breaks the streak");
            Check(Progress.Get(Progress.Stat.BestStreak) == 1, "a loss keeps the best streak");
            Progress.RecordKiteFreed();
            Progress.RecordKiteFreed();
            Check(Progress.TryUseHint() && Progress.Get(Progress.Stat.HintsUsed) == 1, "hints used counted");
            Check(Progress.Get(Progress.Stat.KitesFreed) == 2, "kites freed counted");
            Check(Progress.TotalStars == 3 && Progress.ThreeStarLevels == 1, "total stars and 3-star levels");

            int coins = Progress.Coins;
            Progress.Validate();
            Check(Progress.Coins == coins, "an untouched save passes the checksum");
            PlayerPrefs.SetInt("st_wins", 999);
            Progress.Validate();
            Check(Progress.Coins == Progress.StartingCoins, "a stat edited by hand fails the checksum");

            string cloud = Progress.ToCloudJson();
            Progress.Reset();
            Progress.MergeCloud(cloud);
            Check(Progress.Level == 2 && Progress.Stars(1) == 3 && Progress.Get(Progress.Stat.Wins) == 999,
                "a fresh install takes the cloud save");
            Progress.Validate();
            Check(Progress.Coins == Progress.StartingCoins && Progress.Get(Progress.Stat.Wins) == 999, "a merged save is signed");

            int before = Progress.Coins;
            Progress.MergeCloud("{\"level\":1,\"coins\":5,\"hints\":0,\"stars\":[1,2],\"stats\":[0,0,0,0,0,0,0]}");
            Check(Progress.Level == 2 && Progress.Coins == before, "an older cloud save doesn't take the phone back");
            Check(Progress.Stars(1) == 3 && Progress.Stars(2) == 2, "stars keep the best of both saves");
            Progress.MergeCloud("not json");
            Check(Progress.Level == 2, "a broken cloud save is ignored");

            // Boosters, skins and Remove Ads.
            Progress.Reset();
            Check(Progress.Boosters(Progress.Booster.Undo) == Progress.StartingBoosters, "one of each booster to start");
            Check(Progress.TryUseBooster(Progress.Booster.Cut) && Progress.Boosters(Progress.Booster.Cut) == 0, "using a booster takes one");
            Check(!Progress.TryUseBooster(Progress.Booster.Cut), "no booster left: can't use it");
            Check(Progress.Get(Progress.Stat.BoostersUsed) == 1, "boosters used counted");
            Progress.AddBoosters(Progress.Booster.Cut, 3);
            Check(Progress.Boosters(Progress.Booster.Cut) == 3, "booster packs add up");
            Check(Progress.OwnsSkin(0) && !Progress.OwnsSkin(1), "only the classic skin to start");
            Check(!Progress.TryBuySkin(4, 10000), "a skin needs enough coins");
            Progress.AddCoins(1000);
            int coinsBefore = Progress.Coins;
            Check(Progress.TryBuySkin(2, 300) && Progress.OwnsSkin(2) && Progress.Skin == 2 && Progress.Coins == coinsBefore - 300,
                "buying a skin takes its price and puts it on");
            Progress.UseSkin(3);
            Check(Progress.Skin == 2, "a skin you don't own can't be used");
            Check(!Progress.NoAds, "ads are on until Remove Ads is bought");
            Progress.GrantNoAds();
            Check(Progress.NoAds && Progress.FreeRewardsLeft == Progress.FreeRewardsPerDay, "Remove Ads: free rewards every day");
            for (int i = 0; i < Progress.FreeRewardsPerDay; i++) Progress.TryClaimFreeReward();
            Check(Progress.FreeRewardsLeft == 0 && !Progress.TryClaimFreeReward(), "free rewards stop at the daily limit");
            Progress.Validate();
            Check(Progress.NoAds && Progress.OwnsSkin(2) && Progress.Boosters(Progress.Booster.Cut) == 3, "boosters, skins and No Ads are signed");
            PlayerPrefs.SetInt("bo_undo", 99);
            Progress.Validate();
            Check(Progress.Boosters(Progress.Booster.Undo) == Progress.StartingBoosters && !Progress.NoAds, "boosters edited by hand fail the checksum");

            // A save signed by the previous version (v2: level, coins, hints, 7 stats) is accepted and re-signed.
            Progress.Reset();
            Progress.AddCoins(55);
            int v2Coins = Progress.Coins;
            var v2 = new List<int> { Progress.Level, Progress.Coins, Progress.Hints };
            for (int i = 0; i < 7; i++) v2.Add(Progress.Get((Progress.Stat)i));
            PlayerPrefs.SetInt("sig", OldHash(v2.ToArray()));
            PlayerPrefs.SetInt("sig_v", 2);
            Progress.Validate();
            Check(Progress.Coins == v2Coins && PlayerPrefs.GetInt("sig_v") == 3, "an older save is upgraded, not reset");

            Progress.AddCoins(500);
            Progress.TryBuySkin(2, 300);
            Progress.AddBoosters(Progress.Booster.Freeze, 4);
            string withBoosters = Progress.ToCloudJson();
            Progress.Reset();
            PlayerPrefs.SetInt("skins", 1);
            PlayerPrefs.SetInt("skin", 0);
            Progress.MergeCloud(withBoosters.Replace("\"level\":1", "\"level\":9"));
            Check(Progress.Level == 9 && Progress.OwnsSkin(2) && Progress.Skin == 2 &&
                  Progress.Boosters(Progress.Booster.Freeze) == Progress.StartingBoosters + 4, "the cloud save carries boosters and skins");
        }
        finally
        {
            foreach (var pair in backup)
                if (pair.Value.HasValue) PlayerPrefs.SetInt(pair.Key, pair.Value.Value);
                else PlayerPrefs.DeleteKey(pair.Key);
            PlayerPrefs.Save();
        }
    }

    // The save checksum (FNV-1a with the "kite" salt), to build a save signed by an older version.
    static int OldHash(params int[] values)
    {
        uint h = 2166136261u ^ 0x6B697465;
        foreach (int v in values)
            for (int b = 0; b < 4; b++) { h ^= (uint)(v >> (b * 8)) & 0xFF; h *= 16777619u; }
        return (int)h;
    }

    // Ice, hidden colors, double strings and wind on hand-made levels.
    static void SpecialKites()
    {
        // Ice: kite 1 (on top) is iced. Cracking is a separate step and doesn't change what can be won.
        var iced = HandMade(1);
        iced.ice = 1 << 1;
        var m = new PuzzleModel(iced);
        Check(m.IsIced(1) && m.IsUntangled(1), "an iced kite on top is untangled but iced");
        m.Crack(1);
        Check(!m.IsIced(1) && !m.IsFreed(1), "the first tap only cracks the ice");
        Check(PuzzleModel.Solve(new PuzzleModel(iced)) != null, "ice doesn't make a level unsolvable");

        // Hidden: kite 0 lies under kite 1, so its color shows only once kite 1 is gone.
        var hidden = HandMade(1);
        hidden.hidden = 1 << 0 | 1 << 1;
        m = new PuzzleModel(hidden);
        Check(m.IsHidden(0) && !m.IsHidden(1), "a covered hidden kite is hidden; an uncovered one shows at once");
        m.Free(1);
        Check(!m.IsHidden(0), "clearing its string shows the color");

        // Wind turns each time a chhat leaves.
        var windy = HandMade(1);
        windy.crossMask = new int[6];
        windy.crossMask[0] = 1 << 1;
        windy.crossMask[1] = 1 << 0;
        windy.wind = new[] { 0, 1 };
        var w = new PuzzleModel(windy);
        Check(w.HasWind && !w.Windy && w.LayerOf(0) == 0 && w.LayerOf(1) == 1, "calm before any chhat leaves");
        w.Free(1); w.Free(0); w.Free(2); // red chhat done -> blue arrives, the wind turns
        Check(w.QueueIndex == 1 && w.Windy && w.LayerOf(0) == 1 && w.LayerOf(1) == 0, "the wind turns when a chhat leaves");

        // Wind swaps layers: build a level where kites 3 and 4 (blue) cross and swap once red leaves.
        var swap = HandMade(2);
        swap.crossMask = new int[6];
        swap.crossMask[3] = 1 << 4;
        swap.crossMask[4] = 1 << 3;
        swap.wind = new[] { 3, 4 };
        swap.blockerMask = PuzzleModel.BlockersFrom(swap.crossMask, swap.kites.Select(k => k.layer).ToArray()); // 4 on 3
        m = new PuzzleModel(swap);
        Check(!m.IsUntangled(3) && m.IsUntangled(4), "calm: kite 4 lies on kite 3");
        m.Free(1); m.Free(0); m.Free(2);
        Check(m.Windy && m.IsUntangled(3) && !m.IsUntangled(4), "windy: kite 3 now lies on kite 4");
        Check(PuzzleModel.Solve(new PuzzleModel(swap)) != null, "a wind level is solvable");

        // Undo support: an older state keeps slots bought since.
        var undo = new PuzzleModel(HandMade(1));
        var before = undo.Clone();
        undo.Free(3);
        undo.AddSlot();
        before.KeepSlots(undo.SlotCount);
        Check(before.SlotCount == 2 && !before.IsFreed(3), "undo puts the state back and keeps the bought slot");

        // Cut: a tangled kite can be freed (the board skips the untangled check).
        var cut = new PuzzleModel(HandMade(1));
        cut.Free(0);
        Check(cut.IsFreed(0) && cut.KiteChhat[0] == 0, "Cut frees a tangled kite onto its chhat");
    }

    // Levels with special kites: each kind appears from its level, and the levels stay solvable.
    static void GeneratedSpecials()
    {
        foreach (int n in new[] { LevelGenerator.IceFrom, LevelGenerator.HiddenFrom, LevelGenerator.TwinFrom, LevelGenerator.WindFrom, 120 })
        {
            var level = LevelGenerator.Generate(n);
            Check(level.ice != 0, $"L{n}: has iced kites");
            if (n >= LevelGenerator.HiddenFrom) Check(level.hidden != 0, $"L{n}: has hidden kites");
            if (n >= LevelGenerator.TwinFrom) Check(level.kites.Any(k => k.twin), $"L{n}: has a double-string kite");
            if (n >= LevelGenerator.WindFrom) Check(level.wind != null && level.wind.Length >= 2, $"L{n}: has wind");
            Check(PuzzleModel.Solve(new PuzzleModel(level)) != null, $"L{n}: solvable with special kites");
            for (int k = 0; k < level.kites.Length; k++)
                if ((level.hidden & (1 << k)) != 0) Check(level.blockerMask[k] != 0, $"L{n}: hidden kite {k} starts covered");
        }
        var plain = LevelGenerator.Generate(LevelGenerator.IceFrom - 1);
        Check(plain.ice == 0 && plain.hidden == 0 && plain.wind == null && !plain.kites.Any(k => k.twin), "levels before 21 have no special kites");
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
        a.ice == b.ice && a.hidden == b.hidden && (a.wind ?? new int[0]).SequenceEqual(b.wind ?? new int[0]) &&
        a.kites.Select(k => k.twin).SequenceEqual(b.kites.Select(k => k.twin)) &&
        a.kites.Select(k => (k.colorId, k.layer)).SequenceEqual(b.kites.Select(k => (k.colorId, k.layer))) &&
        a.kites.Zip(b.kites, (x, y) => (x.anchor - y.anchor).sqrMagnitude + (x.spool - y.spool).sqrMagnitude).All(d => d < 1e-8f);

    static bool Same(LevelDefs.LevelData a, LevelDefs.LevelData b) =>
        a.slots == b.slots && a.queue.SequenceEqual(b.queue) && a.blockerMask.SequenceEqual(b.blockerMask) &&
        a.kites.Select(k => (k.colorId, k.layer, k.spool, k.anchor, k.bend))
            .SequenceEqual(b.kites.Select(k => (k.colorId, k.layer, k.spool, k.anchor, k.bend)));
}
