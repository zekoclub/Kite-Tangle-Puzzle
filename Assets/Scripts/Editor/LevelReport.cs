using System.Diagnostics;
using UnityEditor;
using Debug = UnityEngine.Debug;

// Generates levels 1-30 and logs size, slots, blocker count, solution length and time.
// Menu: Kite Tangle > Level Report (also runnable from batchmode).
public static class LevelReport
{
    [MenuItem("Kite Tangle/Level Report")]
    public static void Run()
    {
        for (int n = 1; n <= 30; n++)
        {
            var sw = Stopwatch.StartNew();
            var level = LevelGenerator.Generate(n);
            long genMs = sw.ElapsedMilliseconds;
            var path = PuzzleModel.Solve(new PuzzleModel(level));
            int crossings = 0;
            foreach (int mask in level.blockerMask)
                for (int m = mask; m != 0; m &= m - 1) crossings++;
            float win = LevelGenerator.RandomWinRate(level, new System.Random(1));
            Debug.Log($"[LevelReport] L{n}: kites={level.kites.Length} slots={level.slots} crossings={crossings} randomWin={win:P0} " +
                      $"solvable={(path != null)} solutionLen={path?.Count} gen={genMs}ms");
        }
    }
}
