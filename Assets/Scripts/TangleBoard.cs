using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;

// The game screen: builds a level, turns taps into PuzzleModel moves and keeps the visuals in sync.
// The uGUI screens (GameUI) read the public state below and call the public actions; they never decide rules.
public class TangleBoard : MonoBehaviour
{
    public const int MaxHearts = 3;
    public const float HudHeightPx = 190f;  // top HUD height in the 1080x1920 canvas reference
    public const float RoofBandPx = 250f;   // bottom roof band (Hint/Restart buttons) in the same reference
    public const float RefHeight = 1920f;
    public const float RefWidth = 1080f;

    /// Screen pixels per canvas unit; matches GameUI's CanvasScaler (Expand mode).
    public static float UiScale => Mathf.Min(Screen.width / RefWidth, Screen.height / RefHeight);
    const float MinVisibleWidth = 6.2f;     // world units that must always fit horizontally

    public enum GameState { Idle, Playing, Won, Lost }
    public enum LoseReason { None, OutOfHearts, NoSpace }

    public GameState State { get; private set; } = GameState.Idle;
    public LoseReason Reason { get; private set; }
    public int LevelNumber => level?.number ?? 0;
    public int Hearts { get; private set; }
    public int ChhatsLeft => level == null ? 0 : level.queue.Length - model.QueueIndex;
    public int ChhatsTotal => level?.queue.Length ?? 0;
    public bool UsedContinue { get; private set; }
    public bool BonusSlotAvailable { get; private set; } // the "+1 FREE" (rewarded ad) slot is still locked
    public int Stars => UsedContinue ? 1 : Mathf.Clamp(Hearts, 1, 3);
    public int CoinsEarned { get; private set; }

    public bool InputBlocked { get; set; } // set by the UI while a popup is open

    public enum HintResult { Shown, Busy, NoMoves }

    /// Free slots right now (not counting the locked bonus slot).
    public int SlotsFree => model == null ? 0 : model.SlotCount - model.Slots.Count;
    /// True once a background check proves the level can't be won from here with the current slots.
    public bool Stuck { get; private set; }
    public bool HintActive => hintKite != null && hintKite.Hinting;
    /// Seconds since the player last did something on the board (or a hint was shown).
    public float IdleTime => Time.time - lastAction;

    public event Action Changed;          // hearts, chhats or state changed
    public event Action BonusSlotTapped;  // the player tapped the locked "+1 FREE" slot

    readonly List<Kite> kites = new();
    readonly List<ChhatView> chhats = new();
    readonly List<Transform> slots = new();
    readonly List<SpriteRenderer> slotFrames = new();
    readonly List<RaycastResult> uiHits = new();
    Transform levelRoot, bonusSlot;
    Camera cam;
    SpriteRenderer sky, roof;
    SkyDecor skyDecor;
    string skyName;
    Vector2Int fittedFor;

    LevelDefs.LevelData level;
    PuzzleModel model;

    // Background solve of the current state: finds dead ends and makes the hint instant.
    Task<List<int>> solveTask;
    int moves, solveForMove = -1;
    Kite hintKite;
    float lastAction;

    // Layout (world units), computed from the camera and the screen's safe area.
    float halfW, halfH, chhatY, slotsY, fieldTop, spoolY, roofTop, fieldHalfW;

    void Awake()
    {
        cam = Camera.main;
        ComputeLayout();
        BuildBackground();
    }

    void ComputeLayout()
    {
        cam.orthographicSize = Mathf.Max(5f, MinVisibleWidth * 0.5f / cam.aspect);
        float s = cam.orthographicSize;
        halfH = s;
        halfW = s * cam.aspect;
        float pxToWorld = 2f * s / Screen.height;
        Rect safe = Screen.safeArea;
        float topInset = (Screen.height - safe.yMax) * pxToWorld;
        float bottomInset = safe.yMin * pxToWorld;

        float hud = HudHeightPx * UiScale * pxToWorld + topInset;
        chhatY = s - hud - 0.55f;
        slotsY = chhatY - 1.2f;
        fieldTop = slotsY - 1.05f;     // highest kite anchor; the kite body rises above it
        roofTop = -s + bottomInset + RoofBandPx * UiScale * pxToWorld;
        spoolY = roofTop - 0.08f;      // charkhis stand on the roof edge
        fieldHalfW = Mathf.Min(halfW, 3.1f) - 0.35f;
    }

    Vector2 ToWorld(Vector2 n) => new Vector2(
        Mathf.Lerp(-fieldHalfW, fieldHalfW, n.x),
        Mathf.Lerp(spoolY, fieldTop, n.y));

    void BuildBackground()
    {
        sky = new GameObject("Sky").AddComponent<SpriteRenderer>();
        sky.transform.SetParent(transform, false);
        sky.sortingOrder = -100;
        skyDecor = new GameObject("SkyDecor").AddComponent<SkyDecor>();
        skyDecor.transform.SetParent(transform, false);
        roof = new GameObject("RoofBand").AddComponent<SpriteRenderer>();
        roof.transform.SetParent(transform, false);
        roof.sprite = Art.Load("backgrounds/roof_band");
        roof.sortingOrder = -10;
        SetSky(Progress.Level);
    }

    // Stretches the sky and roof band over the whole screen. Runs again whenever the screen size changes
    // (the first frames can report a different size), so the camera color never shows at the edges.
    void FitBackground()
    {
        fittedFor = new Vector2Int(Screen.width, Screen.height);
        Vector2 size = sky.sprite.bounds.size;
        sky.transform.localScale = new Vector3(halfW * 2f * 1.02f / size.x, halfH * 2f * 1.02f / size.y, 1f);

        size = roof.sprite.bounds.size;
        float height = roofTop + halfH;
        roof.transform.position = new Vector3(0f, roofTop - height * 0.5f, 0f);
        roof.transform.localScale = new Vector3((halfW * 2f + 0.2f) / size.x, height / size.y, 1f);
        skyDecor.Show(skyName, halfW, halfH);
    }

    // Camera color behind each sky = the sky's edge color, so even a gap would not read as a border.
    static Color SkyEdgeColor(string skyName) => skyName switch
    {
        "sky_dusk" => Art.Hex("F8D2A8"),
        "sky_night" => Art.Hex("2C2958"),
        _ => Art.Hex("E4F1F2"),
    };

    // Day, dusk and night skies take turns every 5 levels.
    public static string SkyFor(int levelNumber) =>
        new[] { "sky_day", "sky_dusk", "sky_night" }[(levelNumber - 1) / 5 % 3];

    public void SetSky(int levelNumber)
    {
        skyName = SkyFor(levelNumber);
        sky.sprite = Art.Load("backgrounds/" + skyName);
        cam.backgroundColor = SkyEdgeColor(skyName);
        FitBackground();
    }

    // Removes the level (for the home screen and level map).
    public void Clear(int skyLevel)
    {
        if (levelRoot != null) Destroy(levelRoot.gameObject);
        levelRoot = null;
        level = null;
        model = null;
        State = GameState.Idle;
        SetSky(skyLevel);
        Changed?.Invoke();
    }

    // Shows a level made by LevelGenerator (GameUI gets it from LevelCache, off the main thread).
    public void LoadLevel(LevelDefs.LevelData data)
    {
        int number = data.number;
        if (levelRoot != null) Destroy(levelRoot.gameObject);
        levelRoot = new GameObject("Level").transform;
        levelRoot.SetParent(transform, false);
        kites.Clear();
        chhats.Clear();
        slots.Clear();
        slotFrames.Clear();
        bonusSlot = null;
        ComputeLayout(); // a new level always uses the current screen size
        SetSky(number);

        level = data;
        model = new PuzzleModel(level);
        moves = 0;
        hintKite = null;
        lastAction = Time.time;
        StartSolve();

        float spoolGap = 0.92f * 2f * fieldHalfW / Mathf.Max(1, level.kites.Length - 1);
        float spoolWidth = Mathf.Min(26f * Art.DesignPx, spoolGap * 0.95f);
        for (int i = 0; i < level.kites.Length; i++)
        {
            var kite = new GameObject($"Kite_{i}").AddComponent<Kite>();
            kite.transform.SetParent(levelRoot, false);
            kite.Init(i, level.kites[i], ToWorld, spoolWidth);
            kites.Add(kite);
        }

        foreach (int colorId in level.queue)
        {
            var view = new GameObject("Chhat").AddComponent<ChhatView>();
            view.transform.SetParent(levelRoot, false);
            view.Init(colorId);
            chhats.Add(view);
        }

        Hearts = MaxHearts;
        UsedContinue = false;
        BonusSlotAvailable = true;
        CoinsEarned = 0;
        Reason = LoseReason.None;
        State = GameState.Playing;
        LayoutSlots();
        Sync(snap: true);
        Changed?.Invoke();
    }

    // Creates any missing slot objects and spaces them evenly; the locked bonus slot goes last.
    void LayoutSlots()
    {
        while (slots.Count < model.SlotCount) AddSlotObject("slot_empty");
        if (BonusSlotAvailable && bonusSlot == null) bonusSlot = AddSlotObject("slot_ad_locked", addCollider: true);
        if (!BonusSlotAvailable && bonusSlot != null) { Destroy(bonusSlot.gameObject); bonusSlot = null; }

        int count = slots.Count + (bonusSlot != null ? 1 : 0);
        float gap = Mathf.Min(0.86f, (halfW * 2f - 0.4f) / count);
        for (int i = 0; i < count; i++)
        {
            Transform slot = i < slots.Count ? slots[i] : bonusSlot;
            slot.position = new Vector3((i - (count - 1) * 0.5f) * gap, slotsY, 0f);
            var frame = slot.GetChild(0);
            var sr = frame.GetComponent<SpriteRenderer>();
            frame.localScale = Vector3.one * Art.ScaleForWidth(sr.sprite, gap * 0.86f);
        }
    }

    Transform AddSlotObject(string sprite, bool addCollider = false)
    {
        var slot = new GameObject(addCollider ? "BonusSlot" : $"Slot_{slots.Count}").transform;
        slot.SetParent(levelRoot, false);

        var frame = new GameObject("Frame").AddComponent<SpriteRenderer>();
        frame.transform.SetParent(slot, false);
        frame.sprite = Art.Load("ui/" + sprite);
        frame.sortingOrder = 300;
        if (addCollider)
        {
            frame.gameObject.AddComponent<BoxCollider2D>();
            return slot;
        }

        // Same centering as a chhat dock: kite sprites pivot on their string point.
        var empty = Art.Load("kites/kite_dock_empty");
        float scale = Art.ScaleForWidth(empty, Kite.DockWidth);
        var dock = new GameObject("Dock").transform;
        dock.SetParent(slot, false);
        dock.localPosition = new Vector3(0f, -empty.bounds.size.y * scale * 0.5f - empty.bounds.min.y * scale, 0f);

        slots.Add(slot);
        slotFrames.Add(frame);
        return slot;
    }

    // Put every chhat and freed kite where the model says it belongs.
    void Sync(bool snap = false)
    {
        float activeX = -halfW + 0.25f + ChhatView.Width * 0.5f;
        float nextX = activeX + ChhatView.Width * 0.5f + 0.25f + ChhatView.Width * 0.3f;
        for (int q = 0; q < chhats.Count; q++)
        {
            int d = q - model.QueueIndex;
            Vector3 pos;
            float scale = 1f, delay = 0f;
            if (d < 0) { pos = new Vector3(-halfW - 2.5f, chhatY, 0f); delay = 0.5f; } // done: slides away after the last kite lands
            else if (d == 0) pos = new Vector3(activeX, chhatY, 0f);
            else
            {
                scale = 0.6f;
                pos = new Vector3(nextX + (d - 1) * (ChhatView.Width * 0.6f + 0.12f), chhatY, 0f);
                if (pos.x + ChhatView.Width * 0.3f > halfW) pos.x = halfW + 2.5f; // wait off-screen
            }
            if (snap) chhats[q].Snap(pos, scale);
            else chhats[q].MoveTo(pos, scale, delay);
        }

        for (int k = 0; k < kites.Count; k++)
        {
            if (!model.IsFreed(k)) continue;
            if (model.KiteChhat[k] >= 0)
                kites[k].DockTo(chhats[model.KiteChhat[k]].Docks[model.KiteDock[k]]);
            else
                kites[k].DockTo(slots[model.Slots.IndexOf(k)].Find("Dock"));
        }
    }

    // Solve the current state on a worker thread. The model is cloned, so the board can keep changing.
    void StartSolve()
    {
        Stuck = false;
        solveForMove = moves;
        var snapshot = model.Clone();
        solveTask = Task.Run(() => PuzzleModel.Solve(snapshot));
    }

    // The solution for the current state, if the background solve has finished for it.
    bool TryGetSolution(out List<int> path)
    {
        path = null;
        if (solveTask == null || solveForMove != moves || !solveTask.IsCompleted || solveTask.IsFaulted) return false;
        path = solveTask.Result;
        return true;
    }

    void Update()
    {
        if (Screen.width != fittedFor.x || Screen.height != fittedFor.y)
        {
            // Without a level the whole layout can follow; mid-level only the background does (kites keep their places).
            if (level == null) ComputeLayout();
            else
            {
                cam.orthographicSize = Mathf.Max(5f, MinVisibleWidth * 0.5f / cam.aspect);
                halfH = cam.orthographicSize;
                halfW = halfH * cam.aspect;
            }
            FitBackground();
        }

        if (State == GameState.Playing && !Stuck && TryGetSolution(out var solution) && solution == null)
        {
            Stuck = true;
            Changed?.Invoke();
        }

        if (State != GameState.Playing || InputBlocked || !TryGetTap(out Vector2 screenPos) || OverUI(screenPos)) return;

        Vector2 worldPos = cam.ScreenToWorldPoint(screenPos);
        Kite tapped = KiteAt(worldPos, out bool hitBonusSlot);
        if (tapped == null)
        {
            if (hitBonusSlot) BonusSlotTapped?.Invoke();
            return;
        }
        Tap(tapped.Id);
    }

    // One player move on kite k (also used by the screenshot tour).
    public void Tap(int k)
    {
        if (State != GameState.Playing) return;
        var tapped = kites[k];
        lastAction = Time.time;

        if (!model.IsUntangled(k))
        {
            tapped.StartCoroutine(tapped.Shake());
            for (int j = 0; j < kites.Count; j++)
                if ((level.blockerMask[k] & (1 << j)) != 0 && !model.IsFreed(j))
                    kites[j].Flash();
            Hearts--;
            Sfx.Play(Sfx.Sound.Wrong);
            Sfx.Buzz();
            if (Hearts <= 0) Lose(LoseReason.OutOfHearts);
            else Changed?.Invoke();
            return;
        }

        if (!model.CanPlace(k))
        {
            tapped.StartCoroutine(tapped.Shake());
            foreach (var frame in slotFrames) frame.color = new Color(1f, 0.55f, 0.5f);
            Sfx.Buzz();
            Lose(LoseReason.NoSpace);
            return;
        }

        int queueBefore = model.QueueIndex;
        bool toChhat = level.kites[k].colorId == model.ActiveColor;
        model.Free(k);
        Progress.RecordKiteFreed();
        moves++;
        Sync();
        Sfx.Play(model.QueueIndex > queueBefore ? Sfx.Sound.ChhatDone : toChhat ? Sfx.Sound.Dock : Sfx.Sound.Free);

        if (model.Won)
        {
            State = GameState.Won;
            CoinsEarned = Progress.CompleteLevel(level.number, Stars);
            Sfx.Play(Sfx.Sound.Win);
        }
        else StartSolve();
        Changed?.Invoke();
    }

    void Lose(LoseReason reason)
    {
        State = GameState.Lost;
        Reason = reason;
        Progress.RecordLoss();
        Sfx.Play(Sfx.Sound.Lose);
        Changed?.Invoke();
    }

    // Pulses the next correct kite. Busy = a hint is already showing (don't charge for it again).
    public HintResult ShowHint()
    {
        if (State != GameState.Playing) return HintResult.Busy;
        if (HintActive) return HintResult.Busy;
        if (!TryGetSolution(out var path)) path = PuzzleModel.Solve(model.Clone()); // background solve not done yet
        if (path == null || path.Count == 0)
        {
            lastAction = Time.time; // don't re-solve every frame from the tutorial's idle hint
            return HintResult.NoMoves;
        }
        hintKite = kites[path[0]];
        hintKite.StartCoroutine(hintKite.HintPulse());
        lastAction = Time.time;
        return HintResult.Shown;
    }

    // For the screenshot tour: the next solution move, a covered kite, or -1.
    public int NextGoodKite() => PuzzleModel.Solve(model.Clone()) is { Count: > 0 } path ? path[0] : -1;
    public int SomeCoveredKite()
    {
        for (int k = 0; k < kites.Count; k++)
            if (!model.IsFreed(k) && !model.IsUntangled(k)) return k;
        return -1;
    }

    // Opens the locked "+1 FREE" slot (after its rewarded ad).
    public void UnlockBonusSlot()
    {
        if (!BonusSlotAvailable || model == null) return;
        BonusSlotAvailable = false;
        model.AddSlot();
        LayoutSlots();
        StartSolve(); // an extra slot can rescue a stuck level
        Changed?.Invoke();
    }

    // Second chance after losing (after a rewarded ad): no space gives +1 slot, no hearts refills them.
    public void Continue()
    {
        if (State != GameState.Lost) return;
        UsedContinue = true;
        if (Reason == LoseReason.NoSpace)
        {
            if (BonusSlotAvailable) UnlockBonusSlot();
            else { model.AddSlot(); LayoutSlots(); StartSolve(); }
            foreach (var frame in slotFrames) frame.color = Color.white;
        }
        else Hearts = MaxHearts;
        lastAction = Time.time;
        Reason = LoseReason.None;
        State = GameState.Playing;
        Changed?.Invoke();
    }

    // If kite bodies overlap, pick the one drawn on top.
    Kite KiteAt(Vector2 worldPos, out bool hitBonusSlot)
    {
        Kite best = null;
        hitBonusSlot = false;
        foreach (var hit in Physics2D.OverlapPointAll(worldPos))
        {
            if (bonusSlot != null && hit.transform.IsChildOf(bonusSlot)) { hitBonusSlot = true; continue; }
            var kite = hit.GetComponentInParent<Kite>();
            if (kite != null && !model.IsFreed(kite.Id) && (best == null || kite.Layer > best.Layer))
                best = kite;
        }
        return best;
    }

    // True when the tap lands on a uGUI element that takes raycasts (a button), so the board ignores it.
    bool OverUI(Vector2 screenPos)
    {
        var events = EventSystem.current;
        if (events == null) return false;
        uiHits.Clear();
        events.RaycastAll(new PointerEventData(events) { position = screenPos }, uiHits);
        return uiHits.Count > 0;
    }

    static bool TryGetTap(out Vector2 screenPos)
    {
#if ENABLE_INPUT_SYSTEM
        var pointer = UnityEngine.InputSystem.Pointer.current;
        if (pointer != null && pointer.press.wasPressedThisFrame)
        {
            screenPos = pointer.position.ReadValue();
            return true;
        }
#else
        if (Input.GetMouseButtonDown(0))
        {
            screenPos = Input.mousePosition;
            return true;
        }
#endif
        screenPos = default;
        return false;
    }
}
