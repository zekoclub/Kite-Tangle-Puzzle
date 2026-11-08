using System.Collections.Generic;

// The puzzle rules with no visuals, so the game, the level generator and the hint all use the same logic.
//
// A freed kite goes to the active chhat if the colors match; otherwise it waits in a slot.
// A chhat leaves once it holds ChhatSize kites. The next chhat then takes matching kites from the slots.
// Freeing a kite when it can't go on the chhat and every slot is full loses the level.
//
// Special kites (LevelData.ice / hidden / wind, KiteDef.twin):
// - Ice: the first tap on an untangled iced kite only cracks the ice; the next tap frees it.
// - Hidden: the kite's color shows only once nothing lies on its string (it then stays shown).
// - Double string: both strings must be clear. The generator folds both into the kite's blockers.
// - Wind: each time a chhat leaves the wind turns; while it blows, each wind pair swaps layers.
//   It blows after an odd number of chhats, so the state still depends only on which kites are freed.
public class PuzzleModel
{
    public const int ChhatSize = 3;

    readonly int[] colors;
    readonly int[] calmBlockers, windBlockers; // windBlockers is null when the level has no wind
    readonly int[] calmLayers, windLayers;
    readonly int[] queue;
    readonly int hiddenMask;
    int slotCount;

    public int FreedMask { get; private set; }
    public int IceMask { get; private set; }
    public int RevealedMask { get; private set; }
    public int QueueIndex { get; private set; }
    public List<int> Slots { get; private set; } = new();
    public int[] KiteChhat { get; private set; } // queue index the kite landed on, or -1
    public int[] KiteDock { get; private set; }  // dock position 0..ChhatSize-1 on that chhat

    int onChhat;

    public int KiteCount => colors.Length;
    public int QueueLength => queue.Length;
    public int SlotCount => slotCount;
    public bool Won => QueueIndex >= queue.Length;
    public int ActiveColor => Won ? -1 : queue[QueueIndex];
    public bool HasWind => windBlockers != null;
    public bool Windy => windBlockers != null && (QueueIndex & 1) == 1;

    int[] Blockers => Windy ? windBlockers : calmBlockers;

    public PuzzleModel(LevelDefs.LevelData level)
    {
        colors = new int[level.kites.Length];
        calmLayers = new int[colors.Length];
        for (int i = 0; i < colors.Length; i++)
        {
            colors[i] = level.kites[i].colorId;
            calmLayers[i] = level.kites[i].layer;
        }
        calmBlockers = level.blockerMask;
        if (level.wind != null && level.wind.Length >= 2 && level.crossMask != null && level.crossMask.Length == colors.Length)
        {
            windLayers = (int[])calmLayers.Clone();
            for (int p = 0; p + 1 < level.wind.Length; p += 2)
                (windLayers[level.wind[p]], windLayers[level.wind[p + 1]]) = (windLayers[level.wind[p + 1]], windLayers[level.wind[p]]);
            windBlockers = BlockersFrom(level.crossMask, windLayers);
        }
        queue = level.queue;
        slotCount = level.slots;
        IceMask = level.ice;
        hiddenMask = level.hidden;
        KiteChhat = new int[colors.Length];
        KiteDock = new int[colors.Length];
        for (int i = 0; i < colors.Length; i++) KiteChhat[i] = -1;
        Reveal();
    }

    PuzzleModel(PuzzleModel other)
    {
        colors = other.colors;
        calmBlockers = other.calmBlockers;
        windBlockers = other.windBlockers;
        calmLayers = other.calmLayers;
        windLayers = other.windLayers;
        queue = other.queue;
        hiddenMask = other.hiddenMask;
        slotCount = other.slotCount;
        FreedMask = other.FreedMask;
        IceMask = other.IceMask;
        RevealedMask = other.RevealedMask;
        QueueIndex = other.QueueIndex;
        onChhat = other.onChhat;
        Slots = new List<int>(other.Slots);
        KiteChhat = (int[])other.KiteChhat.Clone();
        KiteDock = (int[])other.KiteDock.Clone();
    }

    public PuzzleModel Clone() => new PuzzleModel(this);

    // Bit j of result[i] set = kite j's string lies on top of kite i's string (they cross and j is higher).
    public static int[] BlockersFrom(int[] crossMask, int[] layers)
    {
        var result = new int[crossMask.Length];
        for (int i = 0; i < crossMask.Length; i++)
        for (int j = 0; j < crossMask.Length; j++)
            if ((crossMask[i] & (1 << j)) != 0 && layers[j] > layers[i]) result[i] |= 1 << j;
        return result;
    }

    public bool IsFreed(int k) => (FreedMask & (1 << k)) != 0;
    public bool IsIced(int k) => (IceMask & (1 << k)) != 0;
    public bool IsHidden(int k) => (hiddenMask & ~RevealedMask & (1 << k)) != 0;
    public int ColorOf(int k) => colors[k];
    public int LayerOf(int k) => Windy ? windLayers[k] : calmLayers[k];
    public int BlockersOf(int k) => Blockers[k];

    // No remaining string lies on top of this kite's string.
    public bool IsUntangled(int k) => !IsFreed(k) && (Blockers[k] & ~FreedMask) == 0;

    public bool CanPlace(int k) => colors[k] == ActiveColor || Slots.Count < slotCount;

    // First tap on an untangled iced kite.
    public void Crack(int k) => IceMask &= ~(1 << k);

    // Frees kite k. The board checks IsUntangled first, except for the Cut booster.
    public void Free(int k)
    {
        FreedMask |= 1 << k;
        IceMask &= ~(1 << k);
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
        Reveal();
    }

    // The +1 slot reward (rewarded ad, coins or a continue).
    public void AddSlot() => slotCount++;

    // Undo puts an older state back but keeps slots bought since.
    public void KeepSlots(int count) { if (count > slotCount) slotCount = count; }

    void Dock(int k)
    {
        KiteChhat[k] = QueueIndex;
        KiteDock[k] = onChhat++;
    }

    // A hidden kite shows its color once nothing lies on its string, and stays shown.
    void Reveal()
    {
        if (hiddenMask == 0) return;
        for (int k = 0; k < colors.Length; k++)
            if ((hiddenMask & (1 << k)) != 0 && (IsFreed(k) || IsUntangled(k))) RevealedMask |= 1 << k;
    }

    // Returns a winning sequence of kites to free from this state, or null if there is none.
    // Ice doesn't change what can be won (cracking is always allowed), so the path lists frees only.
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
