using System.Collections.Generic;

// The puzzle rules with no visuals, so the game, the level generator and the hint all use the same logic.
//
// A freed kite goes to the active chhat if the colors match; otherwise it waits in a slot.
// A chhat leaves once it holds ChhatSize kites. The next chhat then takes matching kites from the slots.
// Freeing a kite when it can't go on the chhat and every slot is full loses the level.
public class PuzzleModel
{
    public const int ChhatSize = 3;

    readonly int[] colors;
    readonly int[] blockerMask;
    readonly int[] queue;
    readonly int slotCount;

    public int FreedMask { get; private set; }
    public int QueueIndex { get; private set; }
    public List<int> Slots { get; private set; } = new();
    public int[] KiteChhat { get; private set; } // queue index the kite landed on, or -1
    public int[] KiteDock { get; private set; }  // dock position 0..ChhatSize-1 on that chhat

    int onChhat;

    public int KiteCount => colors.Length;
    public int QueueLength => queue.Length;
    public bool Won => QueueIndex >= queue.Length;
    public int ActiveColor => Won ? -1 : queue[QueueIndex];

    public PuzzleModel(LevelDefs.LevelData level)
    {
        colors = new int[level.kites.Length];
        for (int i = 0; i < colors.Length; i++) colors[i] = level.kites[i].colorId;
        blockerMask = level.blockerMask;
        queue = level.queue;
        slotCount = level.slots;
        KiteChhat = new int[colors.Length];
        KiteDock = new int[colors.Length];
        for (int i = 0; i < colors.Length; i++) KiteChhat[i] = -1;
    }

    PuzzleModel(PuzzleModel other)
    {
        colors = other.colors;
        blockerMask = other.blockerMask;
        queue = other.queue;
        slotCount = other.slotCount;
        FreedMask = other.FreedMask;
        QueueIndex = other.QueueIndex;
        onChhat = other.onChhat;
        Slots = new List<int>(other.Slots);
        KiteChhat = (int[])other.KiteChhat.Clone();
        KiteDock = (int[])other.KiteDock.Clone();
    }

    public PuzzleModel Clone() => new PuzzleModel(this);

    public bool IsFreed(int k) => (FreedMask & (1 << k)) != 0;

    // No remaining string lies on top of this kite's string.
    public bool IsUntangled(int k) => !IsFreed(k) && (blockerMask[k] & ~FreedMask) == 0;

    public bool CanPlace(int k) => colors[k] == ActiveColor || Slots.Count < slotCount;

    public void Free(int k)
    {
        FreedMask |= 1 << k;
        if (colors[k] == ActiveColor) Dock(k);
        else Slots.Add(k);

        while (onChhat == ChhatSize)
        {
            QueueIndex++;
            onChhat = 0;
            if (Won) break;
            for (int i = 0; i < Slots.Count && onChhat < ChhatSize;)
            {
                if (colors[Slots[i]] == ActiveColor)
                {
                    Dock(Slots[i]);
                    Slots.RemoveAt(i);
                }
                else i++;
            }
        }
    }

    void Dock(int k)
    {
        KiteChhat[k] = QueueIndex;
        KiteDock[k] = onChhat++;
    }

    // Returns a winning sequence of kites to free from this state, or null if there is none.
    public static List<int> Solve(PuzzleModel start)
    {
        var path = new List<int>();
        return Search(start, path, new HashSet<int>()) ? path : null;
    }

    // The game state depends only on which kites are freed, so FreedMask identifies a visited state.
    static bool Search(PuzzleModel m, List<int> path, HashSet<int> visited)
    {
        if (m.Won) return true;
        if (!visited.Add(m.FreedMask)) return false;

        for (int k = 0; k < m.KiteCount; k++)
        {
            if (!m.IsUntangled(k) || !m.CanPlace(k)) continue;
            var next = m.Clone();
            next.Free(k);
            path.Add(k);
            if (Search(next, path, visited)) return true;
            path.RemoveAt(path.Count - 1);
        }
        return false;
    }
}
