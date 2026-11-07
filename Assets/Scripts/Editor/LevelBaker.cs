using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

// Pre-builds levels 1..N into Assets/Resources/Levels/levels.json, so the game never waits for them on a phone.
// Re-run it after any change to LevelGenerator, PuzzleModel or StringGeometry (LogicTests fails while it is stale).
// Menu: Kite Tangle > Bake Levels. Batch: -executeMethod LevelBaker.Run [-count 200].
public static class LevelBaker
{
    public const int DefaultCount = 200;
    const string OutputPath = "Assets/Resources/Levels/levels.json";

    [MenuItem("Kite Tangle/Bake Levels")]
    public static void BakeFromMenu() => Bake(DefaultCount);

    public static void Run()
    {
        var args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, "-count");
        int count = i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out int n) ? n : DefaultCount;
        Bake(count);
    }

    static void Bake(int count)
    {
        var sw = Stopwatch.StartNew();
        var levels = new LevelDefs.LevelData[count];
        // The generator is pure and seeded, so levels can be built in parallel and still come out the same.
        Parallel.For(0, count, i => levels[i] = LevelGenerator.Generate(i + 1));

        Directory.CreateDirectory(Path.GetDirectoryName(OutputPath));
        File.WriteAllText(OutputPath, JsonUtility.ToJson(new LevelDefs.LevelPack { levels = levels }));
        AssetDatabase.ImportAsset(OutputPath);
        Debug.Log($"[LevelBaker] Baked levels 1-{count} into {OutputPath} " +
                  $"({new FileInfo(OutputPath).Length / 1024} KB) in {sw.Elapsed.TotalSeconds:F0} s");
    }
}
