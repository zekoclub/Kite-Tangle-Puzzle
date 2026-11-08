using System;
using UnityEngine;

// Everything the player keeps between sessions, stored in PlayerPrefs.
public static class Progress
{
    const string LevelKey = "level"; // the next level to play (highest unlocked)
    const string CoinsKey = "coins";
    const string HintsKey = "hints";
    const string SoundKey = "sound";
    const string VibrationKey = "vibration";
    const string StarsKey = "stars_"; // + level number: best stars earned
    const string TutorialKey = "tut_";  // + level number: 1 once its "How to play" card was shown
    const string SignatureKey = "sig";  // checksum of level, coins, hints and stats (catches casual save editing)
    const string SignatureVersionKey = "sig_v"; // 2 once the checksum covers the stats too
    const uint Salt = 0x6B697465;       // "kite"

    // Lifetime stats for the Stats screen. Saved with the rest and sent to the cloud save.
    public enum Stat { Played, Wins, Losses, Streak, BestStreak, KitesFreed, HintsUsed }
    static readonly string[] StatKeys = { "st_played", "st_wins", "st_losses", "st_streak", "st_best_streak", "st_kites", "st_hints" };

    public const int StartingHints = 3;
    public const int StartingCoins = 100;

    public static event Action Changed;

    public static int Level => Mathf.Max(1, PlayerPrefs.GetInt(LevelKey, 1));
    public static int Coins => PlayerPrefs.GetInt(CoinsKey, StartingCoins);
    public static int Hints => PlayerPrefs.GetInt(HintsKey, StartingHints);
    public static bool Sound { get => PlayerPrefs.GetInt(SoundKey, 1) == 1; set => SetInt(SoundKey, value ? 1 : 0); }
    public static bool Vibration { get => PlayerPrefs.GetInt(VibrationKey, 1) == 1; set => SetInt(VibrationKey, value ? 1 : 0); }

    public static int Stars(int level) => PlayerPrefs.GetInt(StarsKey + level, 0);

    public static int Get(Stat stat) => PlayerPrefs.GetInt(StatKeys[(int)stat], 0);

    // Best stars over all levels played, and how many levels have 3.
    public static int TotalStars { get { int sum = 0; for (int n = 1; n < Level; n++) sum += Stars(n); return sum; } }
    public static int ThreeStarLevels { get { int count = 0; for (int n = 1; n < Level; n++) if (Stars(n) >= 3) count++; return count; } }

    public static bool TutorialSeen(int level) => PlayerPrefs.GetInt(TutorialKey + level, 0) == 1;
    public static void MarkTutorialSeen(int level) => SetInt(TutorialKey + level, 1);

    // Call once at startup. Saves from before the checksum existed get signed; a save whose level, coins or
    // hints were edited by hand gets its coins and hints set back to the starting amounts.
    public static void Validate()
    {
        if (!PlayerPrefs.HasKey(SignatureKey)) { Sign(); PlayerPrefs.Save(); return; }
        bool legacy = PlayerPrefs.GetInt(SignatureVersionKey, 1) < 2; // signed before the stats existed
        if (PlayerPrefs.GetInt(SignatureKey) == (legacy ? LegacySignature() : Signature()))
        {
            if (legacy) { Sign(); PlayerPrefs.Save(); }
            return;
        }
        Debug.LogWarning("[Progress] Save checksum mismatch: coins and hints reset.");
        PlayerPrefs.SetInt(CoinsKey, StartingCoins);
        PlayerPrefs.SetInt(HintsKey, StartingHints);
        Sign();
        PlayerPrefs.Save();
        Changed?.Invoke();
    }

    // FNV-1a over the values that matter. Not cryptography: it only stops edits made by hand in the XML.
    static int Signature()
    {
        var values = new int[3 + StatKeys.Length];
        values[0] = Level; values[1] = Coins; values[2] = Hints;
        for (int i = 0; i < StatKeys.Length; i++) values[3 + i] = Get((Stat)i);
        return Hash(values);
    }

    static int LegacySignature() => Hash(Level, Coins, Hints);

    static int Hash(params int[] values)
    {
        uint h = 2166136261u ^ Salt;
        foreach (int v in values)
            for (int b = 0; b < 4; b++) { h ^= (uint)(v >> (b * 8)) & 0xFF; h *= 16777619u; }
        return (int)h;
    }

    static void Sign()
    {
        PlayerPrefs.SetInt(SignatureKey, Signature());
        PlayerPrefs.SetInt(SignatureVersionKey, 2);
    }

    // Records a win. Returns the coins it earned: the full reward on the first win, and on a replay
    // only 5 per star above the best so far, so replaying an easy level can't farm coins.
    public static int CompleteLevel(int level, int stars)
    {
        bool firstWin = level >= Level;
        int best = Stars(level);
        if (level + 1 > Level) PlayerPrefs.SetInt(LevelKey, level + 1);
        if (stars > best) PlayerPrefs.SetInt(StarsKey + level, stars);
        Bump(Stat.Wins);
        Bump(Stat.Streak);
        PlayerPrefs.SetInt(StatKeys[(int)Stat.BestStreak], Mathf.Max(Get(Stat.BestStreak), Get(Stat.Streak)));
        int reward = firstWin ? 10 + stars * 5 : Mathf.Max(0, stars - best) * 5;
        SetInt(CoinsKey, Coins + reward);
        return reward;
    }

    // A level was started (also on Restart and Retry).
    public static void RecordStart() { Bump(Stat.Played); Commit(); }

    // Ran out of hearts or space. Breaks the win streak.
    public static void RecordLoss()
    {
        Bump(Stat.Losses);
        PlayerPrefs.SetInt(StatKeys[(int)Stat.Streak], 0);
        Commit();
    }

    // Every tap that frees a kite. Not written to disk each time: the next save (or leaving the app) stores it.
    public static void RecordKiteFreed()
    {
        Bump(Stat.KitesFreed);
        Sign();
    }

    static void Bump(Stat stat) => PlayerPrefs.SetInt(StatKeys[(int)stat], Get(stat) + 1);

    public static void AddCoins(int amount) => SetInt(CoinsKey, Coins + amount);
    public static void AddHints(int amount) => SetInt(HintsKey, Hints + amount);

    public static bool TrySpendCoins(int amount)
    {
        if (Coins < amount) return false;
        SetInt(CoinsKey, Coins - amount);
        return true;
    }

    public static bool TryUseHint()
    {
        if (Hints <= 0) return false;
        Bump(Stat.HintsUsed);
        SetInt(HintsKey, Hints - 1);
        return true;
    }

    // Wipes level progress, stars, coins, hints and stats; keeps the sound settings.
    public static void Reset()
    {
        for (int n = 1; n <= Level; n++) PlayerPrefs.DeleteKey(StarsKey + n);
        PlayerPrefs.DeleteKey(LevelKey);
        PlayerPrefs.DeleteKey(CoinsKey);
        PlayerPrefs.DeleteKey(HintsKey);
        foreach (var key in StatKeys) PlayerPrefs.DeleteKey(key);
        Commit();
    }

    // ── Cloud save (CloudSave.cs sends and fetches this as JSON) ──

    [Serializable]
    class Snapshot
    {
        public int version = 1;
        public int level, coins, hints;
        public int[] stars;     // stars[i] = best stars on level i + 1
        public int[] stats;     // in Stat order
        public int[] tutorials; // levels whose "How to play" card was shown
    }

    public static string ToCloudJson()
    {
        var snap = new Snapshot { level = Level, coins = Coins, hints = Hints, stars = new int[Level - 1], stats = new int[StatKeys.Length] };
        for (int n = 1; n < Level; n++) snap.stars[n - 1] = Stars(n);
        for (int i = 0; i < StatKeys.Length; i++) snap.stats[i] = Get((Stat)i);
        var seen = new System.Collections.Generic.List<int>();
        for (int n = 1; n <= 3; n++) if (TutorialSeen(n)) seen.Add(n);
        snap.tutorials = seen.ToArray();
        return JsonUtility.ToJson(snap);
    }

    // Merges a save from the cloud into this phone's save. The save that got further (higher level, then more wins)
    // keeps its level, coins, hints and stats; stars keep the best of both, and seen tutorials stay seen.
    public static void MergeCloud(string json)
    {
        Snapshot cloud;
        try { cloud = JsonUtility.FromJson<Snapshot>(json); }
        catch (Exception e) { Debug.LogWarning("[Progress] Bad cloud save: " + e.Message); return; }
        if (cloud == null || cloud.level < 1) return;

        int cloudWins = cloud.stats != null && cloud.stats.Length > (int)Stat.Wins ? cloud.stats[(int)Stat.Wins] : 0;
        if (cloud.level > Level || (cloud.level == Level && cloudWins > Get(Stat.Wins)))
        {
            PlayerPrefs.SetInt(LevelKey, cloud.level);
            PlayerPrefs.SetInt(CoinsKey, cloud.coins);
            PlayerPrefs.SetInt(HintsKey, cloud.hints);
            for (int i = 0; i < StatKeys.Length; i++)
                PlayerPrefs.SetInt(StatKeys[i], cloud.stats != null && i < cloud.stats.Length ? cloud.stats[i] : 0);
        }
        if (cloud.stars != null)
            for (int i = 0; i < cloud.stars.Length; i++)
                if (cloud.stars[i] > Stars(i + 1)) PlayerPrefs.SetInt(StarsKey + (i + 1), cloud.stars[i]);
        if (cloud.tutorials != null)
            foreach (int n in cloud.tutorials) PlayerPrefs.SetInt(TutorialKey + n, 1);
        Commit();
    }

    static void SetInt(string key, int value)
    {
        PlayerPrefs.SetInt(key, value);
        Commit();
    }

    static void Commit()
    {
        Sign();
        PlayerPrefs.Save();
        Changed?.Invoke();
    }
}
