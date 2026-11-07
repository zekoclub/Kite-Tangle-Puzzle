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
    const string SignatureKey = "sig";  // checksum of level, coins and hints (catches casual save editing)
    const uint Salt = 0x6B697465;       // "kite"

    public const int StartingHints = 3;
    public const int StartingCoins = 100;

    public static event Action Changed;

    public static int Level => Mathf.Max(1, PlayerPrefs.GetInt(LevelKey, 1));
    public static int Coins => PlayerPrefs.GetInt(CoinsKey, StartingCoins);
    public static int Hints => PlayerPrefs.GetInt(HintsKey, StartingHints);
    public static bool Sound { get => PlayerPrefs.GetInt(SoundKey, 1) == 1; set => SetInt(SoundKey, value ? 1 : 0); }
    public static bool Vibration { get => PlayerPrefs.GetInt(VibrationKey, 1) == 1; set => SetInt(VibrationKey, value ? 1 : 0); }

    public static int Stars(int level) => PlayerPrefs.GetInt(StarsKey + level, 0);

    public static bool TutorialSeen(int level) => PlayerPrefs.GetInt(TutorialKey + level, 0) == 1;
    public static void MarkTutorialSeen(int level) => SetInt(TutorialKey + level, 1);

    // Call once at startup. Saves from before the checksum existed get signed; a save whose level, coins or
    // hints were edited by hand gets its coins and hints set back to the starting amounts.
    public static void Validate()
    {
        if (!PlayerPrefs.HasKey(SignatureKey)) { Sign(); PlayerPrefs.Save(); return; }
        if (PlayerPrefs.GetInt(SignatureKey) == Signature()) return;
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
        uint h = 2166136261u ^ Salt;
        foreach (int v in new[] { Level, Coins, Hints })
            for (int b = 0; b < 4; b++) { h ^= (uint)(v >> (b * 8)) & 0xFF; h *= 16777619u; }
        return (int)h;
    }

    static void Sign() => PlayerPrefs.SetInt(SignatureKey, Signature());

    // Records a win. Returns the coins it earned: the full reward on the first win, and on a replay
    // only 5 per star above the best so far, so replaying an easy level can't farm coins.
    public static int CompleteLevel(int level, int stars)
    {
        bool firstWin = level >= Level;
        int best = Stars(level);
        if (level + 1 > Level) PlayerPrefs.SetInt(LevelKey, level + 1);
        if (stars > best) PlayerPrefs.SetInt(StarsKey + level, stars);
        int reward = firstWin ? 10 + stars * 5 : Mathf.Max(0, stars - best) * 5;
        SetInt(CoinsKey, Coins + reward);
        return reward;
    }

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
        SetInt(HintsKey, Hints - 1);
        return true;
    }

    // Wipes level progress, stars, coins and hints; keeps the sound settings.
    public static void Reset()
    {
        for (int n = 1; n <= Level; n++) PlayerPrefs.DeleteKey(StarsKey + n);
        PlayerPrefs.DeleteKey(LevelKey);
        PlayerPrefs.DeleteKey(CoinsKey);
        PlayerPrefs.DeleteKey(HintsKey);
        Sign();
        PlayerPrefs.Save();
        Changed?.Invoke();
    }

    static void SetInt(string key, int value)
    {
        PlayerPrefs.SetInt(key, value);
        Sign();
        PlayerPrefs.Save();
        Changed?.Invoke();
    }
}
