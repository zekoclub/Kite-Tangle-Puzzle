using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

// Serves levels without freezing the game: the pre-built ones shipped in Resources/Levels/levels.json
// (Kite Tangle > Bake Levels) load at once; any later level is generated on a worker thread and kept.
// LevelGenerator and PuzzleModel are plain C# (no Unity objects), so they are safe to run off the main thread.
// Level data is never changed after generation (PuzzleModel keeps its own slot count), so one copy can be reused.
public static class LevelCache
{
    const int MaxKept = 6;

    static readonly Dictionary<int, Task<LevelDefs.LevelData>> tasks = new();
    static readonly List<int> order = new();
    static Dictionary<int, LevelDefs.LevelData> baked;

    public const string BakedPath = "Levels/levels"; // under Assets/Resources

    public static int BakedCount => Baked().Count;

    // Starts generating level n if it isn't already; call from the main thread only.
    public static Task<LevelDefs.LevelData> Request(int n)
    {
        if (Baked().TryGetValue(n, out var ready)) return Task.FromResult(ready);
        if (tasks.TryGetValue(n, out var task) && !task.IsFaulted) return task;
        task = Task.Run(() => LevelGenerator.Generate(n));
        tasks[n] = task;
        order.Remove(n);
        order.Add(n);
        while (order.Count > MaxKept)
        {
            tasks.Remove(order[0]);
            order.RemoveAt(0);
        }
        return task;
    }

    public static void Prefetch(int n) => Request(n);

    static Dictionary<int, LevelDefs.LevelData> Baked()
    {
        if (baked != null) return baked;
        baked = new Dictionary<int, LevelDefs.LevelData>();
        var file = Resources.Load<TextAsset>(BakedPath);
        if (file == null) return baked;
        var pack = JsonUtility.FromJson<LevelDefs.LevelPack>(file.text);
        foreach (var level in pack.levels) baked[level.number] = level;
        Resources.UnloadAsset(file);
        return baked;
    }
}
