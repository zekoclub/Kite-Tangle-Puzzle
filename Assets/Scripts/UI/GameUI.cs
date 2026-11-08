using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using static UIKit;

// All the screens, built from code on one Screen Space Overlay canvas (1080x1920 reference, Expand):
// Home, Level map, in-game HUD, Win, Lose, Pause, Settings, Shop and Stats.
// It only reads TangleBoard state and calls its public actions; game rules stay in PuzzleModel.
public partial class GameUI : MonoBehaviour
{
    const float WinDelay = 1.0f;  // let the last kite land first
    const float LoseDelay = 0.6f;
    const int HintPack3Price = 60;
    const int HintPack10Price = 180;
    const int FreeCoinsAmount = 50;
    // Coin prices for everything an ad can also pay for, so nothing needs an ad.
    const int BonusSlotPrice = 50;
    const int ContinuePrice = 80;
    static readonly int[] BoosterPrices = { 40, 80, 60 };      // one Undo / Cut / Freeze when you have none
    static readonly int[] BoosterPackPrices = { 100, 200, 150 }; // three of each in the shop
    const int BoosterPack = 3;
    static readonly string[] BoosterNames = { "Undo", "Cut", "Freeze" };
    static readonly string[] BoosterIcons = { Icon.Undo, Icon.Cut, Icon.Freeze };
    static readonly string[] BoosterInfo =
    {
        "Take back your last move.", "Cut any patang loose, even a tangled one.", "Tangled taps cost no hearts for the rest of this level.",
    };
    static readonly Color Icy = Art.Hex("6FB9E8");
    const int TutorialLevels = 2;          // free idle hints on these levels
    const float TutorialIdleSeconds = 5f;

    static readonly Color Dim = new Color(Art.Ink.r, Art.Ink.g, Art.Ink.b, 0.6f);
    static readonly Color EmptyStar = Art.Hex("E3D3B8");
    static readonly Color Chip = new Color(Art.Cream.r, Art.Cream.g, Art.Cream.b, 0.88f);

    TangleBoard board;
    RectTransform root, safe;

    // "How to play" cards: shown once before levels 1-3, and all together from the Pause help button (key 0).
    static readonly Dictionary<int, (string icon, string title, string body)> Lessons = new()
    {
        [1] = (Icon.Info, "How to play",
            "Strings cross. Tap a patang whose dor lies ON TOP of every string it crosses: it flies to the chhat of its color.\n\n" +
            "Tap one that is still tangled and you lose a heart."),
        [2] = (Icon.AddSlot, "Waiting slots",
            "Wrong color for this chhat? The patang waits in a slot. When its chhat arrives, it flies there by itself.\n\n" +
            "If every slot is full, only a patang of the chhat's color fits!"),
        [3] = (Icon.Ad, "+1 FREE slot",
            "Running out of room? Tap the locked +1 slot to open one more slot for this level: watch an ad or pay coins.\n\n" +
            "Stuck? Hint shows the next good move."),
        [5] = (Icon.Undo, "Boosters",
            "Undo takes back your last move.\n\nCut frees any patang, even a tangled one.\n\n" +
            "Freeze: tangled taps cost no hearts for the whole level.\n\nRun out? Watch an ad or pay coins."),
        [LevelGenerator.IceFrom] = (Icon.Freeze, "Iced patang",
            "Some kites are frozen in ice.\n\nTap an untangled iced kite once to crack the ice, then tap it again to free it."),
        [LevelGenerator.HiddenFrom] = (Icon.Help, "Hidden colors",
            "A gray ? patang hides its color.\n\nClear every string lying on its dor and its color shows."),
        [LevelGenerator.TwinFrom] = (Icon.Info, "Double dor",
            "Some patangs fly on two strings from two charkhis.\n\nBoth strings must be clear before you can free it."),
        [LevelGenerator.WindFrom] = (Icon.Wind, "Hawa (wind)",
            "Kites with a white streamer swap places when the wind turns.\n\nThe wind turns each time a chhat leaves, so plan ahead!"),
        [0] = (Icon.Info, "How to play",
            "Tap a patang whose dor lies on top of every string it crosses. Tangled tap = lose a heart.\n\n" +
            "It flies to the chhat of its color, or waits in a slot. A chhat leaves with 3 kites and the next one takes its kites from the slots.\n\n" +
            "All slots full and the wrong color = No Space! Use the +1 slot, Hint and the boosters.\n\n" +
            "Special kites: ice cracks on the first tap, gray ? kites show their color once clear, " +
            "double-string kites need both strings clear, and streamer kites swap when the wind turns."),
    };

    // Screens and popups.
    RectTransform home, map, hud, win, lose, pause, settings, shop, stats, loading, toast, tutorial, offer, skins;
    Text tutorialIcon, tutorialTitle, tutorialBody;
    readonly List<RectTransform> popups = new();
    bool busy; // a level is being generated

    // Things that get refreshed.
    readonly List<Text> coinLabels = new();
    Text playLabel, hudLevel, hudTip, hintBadge, winTitleLevel, winCoins, winRating, loseIcon, loseTitle, loseMessage, toastText;
    Text[] hearts, winStars;
    Button continueButton, continueCoinsButton, resetButton, privacyButton, cloudButton;
    Button[] boosterButtons;
    Text[] boosterBadges;
    Text offerIcon, offerTitle, offerBody;
    Button offerAd, offerCoins;
    int offerPrice;
    string offerPlacement;
    System.Action offerGrant;
    Button removeAdsButton, freeCoinsButton;
    Text removeAdsSubtitle, freeCoinsSubtitle;
    Text[] boosterShopSubtitles;
    Button[] skinButtons;
    Text[] statValues;
    Text statsFooter;
    RectTransform settingsCard;
    RectTransform mapContent, winReward;
    Confetti winConfetti;
    ScrollRect mapScroll;
    readonly List<Button> soundToggles = new(), vibrationToggles = new(), musicToggles = new();
    TangleBoard.GameState shownState;
    int shownHearts;
    Coroutine toastRoutine, resetRoutine;

    void Start()
    {
        board = FindAnyObjectByType<TangleBoard>();
        EnsureEventSystem();
        BuildCanvas();
        BuildHome();
        BuildMap();
        BuildHud();
        BuildWin();
        BuildLose();
        BuildPause();
        BuildSettings();
        BuildShop();
        BuildStats();
        BuildOffer();
        BuildSkins();
        BuildTutorial();
        BuildLoading();
        BuildToast();

        Progress.Validate();
        Ads.Init();
        Purchases.Init();
        Purchases.Changed += RefreshShop;
        Purchases.Message += Toast;
        CloudSave.Init();
        CloudSave.Changed += RefreshSettings;
        Sfx.RefreshMusic();
        board.Changed += OnBoardChanged;
        board.BonusSlotTapped += OnBonusSlotTapped;
        board.CutDone += OnCutDone;
        board.WindTurned += OnWindTurned;
        Progress.Changed += RefreshProgress;
        RefreshProgress();
        ShowHome();
        LevelCache.Prefetch(Progress.Level); // usually ready before Play is tapped
        StartTourIfAsked();
    }

    void OnDestroy()
    {
        Progress.Changed -= RefreshProgress;
        CloudSave.Changed -= RefreshSettings;
        Purchases.Changed -= RefreshShop;
        Purchases.Message -= Toast;
        if (board != null)
        {
            board.Changed -= OnBoardChanged;
            board.BonusSlotTapped -= OnBonusSlotTapped;
            board.CutDone -= OnCutDone;
            board.WindTurned -= OnWindTurned;
        }
    }

    // ───────────────────────────── Setup ─────────────────────────────

    static void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
        var go = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
        go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
        go.AddComponent<StandaloneInputModule>();
#endif
    }

    void BuildCanvas()
    {
        var go = new GameObject("UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        go.layer = 5;
        go.transform.SetParent(transform, false);
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(TangleBoard.RefWidth, TangleBoard.RefHeight);
        // Expand keeps the whole 1080x1920 design on screen: tall phones get extra
        // height instead of a blown-up UI that runs out of width.
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        root = (RectTransform)go.transform;

        safe = Node("SafeArea", root).Fill();
        safe.gameObject.AddComponent<SafeArea>();
    }

    RectTransform MakeScreen(string name, bool fullBleed = false)
    {
        var rt = Node(name, fullBleed ? root : safe).Fill();
        rt.gameObject.SetActive(false);
        return rt;
    }

    // A dimmed full-screen popup with a cream card in the middle. Returns the card.
    RectTransform Popup(string name, float height, out RectTransform popup, float width = 760f)
    {
        popup = Node(name, root).Fill();
        popup.gameObject.AddComponent<CanvasGroup>();
        Img(popup, "Dim", null, Dim, raycast: true).rectTransform.Fill(); // swallows taps behind the popup
        var card = Panel(popup, "Card").rectTransform.At(new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(width, height));
        popup.gameObject.SetActive(false);
        return card;
    }

    static readonly Color OnCard = Art.Hex("F3E3C4");

    // Secondary buttons are cream like the popup cards, so tint them a shade darker there.
    static Button OnCardButton(Transform card, string text, string icon, int fontSize, System.Action onClick)
    {
        var button = MakeButton(card, Style.Secondary, text, icon, fontSize, onClick);
        button.GetComponent<Image>().color = OnCard;
        return button;
    }

    Button CloseButton(RectTransform card, System.Action onClick)
    {
        var close = OnCardButton(card, null, Icon.Close, 60, onClick);
        close.GetComponent<RectTransform>().At(new Vector2(1f, 1f), new Vector2(34f, 34f), new Vector2(120f, 120f));
        return close;
    }

    RectTransform CoinPill(Transform parent, bool withPlus)
    {
        var pill = Panel(parent, "Coins");
        pill.raycastTarget = withPlus;
        var rt = pill.rectTransform;
        Img(rt, "Coin", Art.Load("ui/coin"), Color.white).rectTransform.At(new Vector2(0f, 0.5f), new Vector2(18f, 4f), new Vector2(76f, 76f));
        var label = Label(rt, "0", 50, Art.Ink, align: TextAnchor.MiddleLeft);
        label.rectTransform.Fill(106, 12, withPlus ? 70 : 20, 0);
        coinLabels.Add(label);
        if (withPlus)
        {
            var plus = Glyph(rt, Icon.Add, 52, Art.SaffronShade);
            plus.rectTransform.At(new Vector2(1f, 0.5f), new Vector2(-14f, 4f), new Vector2(56f, 56f));
            var button = pill.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            pill.gameObject.AddComponent<PressScale>();
            button.onClick.AddListener(() => { Sfx.Play(Sfx.Sound.Click); OpenPopup(shop); });
        }
        return rt;
    }

    // A square icon button that flips between on/off looks (sound, vibration).
    Button ToggleButton(Transform parent, System.Action onClick)
    {
        var button = OnCardButton(parent, "", Icon.SoundOn, 76, onClick);
        var label = Label(button.transform, "", 34, Art.Muted, Art.Body, name: "Caption");
        label.rectTransform.At(new Vector2(0.5f, 0f), new Vector2(0f, -8f), new Vector2(220f, 50f), new Vector2(0.5f, 1f));
        return button;
    }

    static void DecorKite(Transform parent, int colorId, Vector2 anchor, Vector2 pos, float width, float phase)
    {
        var img = Img(parent, "Kite", Art.KiteSprite(colorId), Color.white);
        img.rectTransform.At(anchor, pos, new Vector2(width, width * 358f / 256f), new Vector2(0.5f, 0.5f));
        var bob = img.gameObject.AddComponent<Bob>();
        bob.phase = phase;
    }

    // ───────────────────────────── Home ─────────────────────────────

    void BuildHome()
    {
        home = MakeScreen("Home");

        var settingsButton = MakeButton(home, Style.Secondary, null, Icon.Settings, 70, () => { RefreshSettings(); OpenPopup(settings); });
        settingsButton.GetComponent<RectTransform>().At(new Vector2(0f, 1f), new Vector2(30f, -30f), new Vector2(130f, 130f));
        var statsButton = MakeButton(home, Style.Secondary, null, Icon.Stats, 70, ShowStats);
        statsButton.GetComponent<RectTransform>().At(new Vector2(0f, 1f), new Vector2(30f, -180f), new Vector2(130f, 130f));
        CoinPill(home, withPlus: true).At(new Vector2(1f, 1f), new Vector2(-30f, -36f), new Vector2(300f, 116f));

        DecorKite(home, 0, new Vector2(0.5f, 0.72f), new Vector2(-280f, 150f), 150f, 0f);
        DecorKite(home, 1, new Vector2(0.5f, 0.72f), new Vector2(300f, 190f), 120f, 1.7f);
        DecorKite(home, 3, new Vector2(0.5f, 0.72f), new Vector2(40f, 290f), 170f, 3.1f);
        DecorKite(home, 2, new Vector2(0.5f, 0.72f), new Vector2(-330f, -260f), 100f, 4.4f);
        DecorKite(home, 4, new Vector2(0.5f, 0.72f), new Vector2(320f, -220f), 110f, 5.2f);

        var title = Label(home, "Kite Tangle", 170, Art.Cream);
        title.rectTransform.At(new Vector2(0.5f, 0.62f), new Vector2(0f, 60f), new Vector2(1000f, 220f), new Vector2(0.5f, 0.5f));
        title.Outlined(Art.Ink, 6f).Shadowed(Art.Ink, 12f);
        var tagline = Label(home, "Bo Kata!", 84, Art.Saffron);
        tagline.rectTransform.At(new Vector2(0.5f, 0.62f), new Vector2(0f, -90f), new Vector2(800f, 120f), new Vector2(0.5f, 0.5f));
        tagline.Outlined(Art.Ink, 4f).Shadowed(Art.Ink, 8f);

        var play = MakeButton(home, Style.Primary, "Level 1", Icon.Play, 80, PlayCurrent);
        play.GetComponent<RectTransform>().At(new Vector2(0.5f, 0f), new Vector2(0f, 560f), new Vector2(640f, 200f), new Vector2(0.5f, 0f));
        playLabel = LabelOf(play);

        var levels = MakeButton(home, Style.Secondary, "Levels", Icon.Map, 54, ShowMap);
        levels.GetComponent<RectTransform>().At(new Vector2(0.5f, 0f), new Vector2(-165f, 390f), new Vector2(310f, 140f), new Vector2(0.5f, 0f));
        var shopButton = MakeButton(home, Style.Secondary, "Shop", Icon.Shop, 54, () => OpenPopup(shop));
        shopButton.GetComponent<RectTransform>().At(new Vector2(0.5f, 0f), new Vector2(165f, 390f), new Vector2(310f, 140f), new Vector2(0.5f, 0f));
    }

    void ShowHome()
    {
        CloseAllPopups();
        board.Clear(Progress.Level);
        ShowOnly(home);
    }

    void PlayCurrent() => StartCoroutine(LoadLevel(Progress.Level));

    // ───────────────────────────── Level map ─────────────────────────────

    void BuildMap()
    {
        map = MakeScreen("LevelMap");

        var back = MakeButton(map, Style.Secondary, null, Icon.Back, 70, ShowHome);
        back.GetComponent<RectTransform>().At(new Vector2(0f, 1f), new Vector2(30f, -30f), new Vector2(130f, 130f));
        var title = Label(map, "Levels", 90, Art.Cream, align: TextAnchor.MiddleLeft);
        title.rectTransform.At(new Vector2(0f, 1f), new Vector2(190f, -30f), new Vector2(360f, 130f));
        title.Outlined(Art.Ink, 5f).Shadowed(Art.Ink, 8f);
        CoinPill(map, withPlus: false).At(new Vector2(1f, 1f), new Vector2(-30f, -36f), new Vector2(260f, 116f));

        var card = Panel(map, "Card").rectTransform.Fill(24, 40, 24, 190);
        var viewport = Node("Viewport", card).Fill(16, 36, 16, 16);
        viewport.gameObject.AddComponent<RectMask2D>();
        Img(viewport, "HitArea", null, Color.clear, raycast: true).rectTransform.Fill(); // lets drags scroll

        mapContent = Node("Content", viewport);
        mapContent.anchorMin = new Vector2(0f, 1f);
        mapContent.anchorMax = new Vector2(1f, 1f);
        mapContent.pivot = new Vector2(0.5f, 1f);
        mapContent.sizeDelta = Vector2.zero;
        var grid = mapContent.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(176f, 236f);
        grid.spacing = new Vector2(12f, 8f);
        grid.padding = new RectOffset(0, 0, 16, 16);
        grid.childAlignment = TextAnchor.UpperCenter;
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 4;
        mapContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        mapScroll = viewport.gameObject.AddComponent<ScrollRect>();
        mapScroll.content = mapContent;
        mapScroll.viewport = viewport;
        mapScroll.horizontal = false;
        mapScroll.movementType = ScrollRect.MovementType.Elastic;
        mapScroll.scrollSensitivity = 40f;
    }

    void ShowMap()
    {
        foreach (Transform child in mapContent) Destroy(child.gameObject);
        int unlocked = Progress.Level;
        int count = Mathf.CeilToInt(Mathf.Max(40, unlocked + 12) / 4f) * 4;
        for (int n = 1; n <= count; n++) MapNode(n, unlocked);

        ShowOnly(map);
        Canvas.ForceUpdateCanvases();
        int rows = count / 4, row = (unlocked - 1) / 4;
        mapScroll.verticalNormalizedPosition = rows <= 1 ? 1f : Mathf.Clamp01(1f - (row - 1f) / Mathf.Max(1f, rows - 3f));
    }

    void MapNode(int n, int unlocked)
    {
        var cell = Node($"Level_{n}", mapContent);
        bool done = n < unlocked, current = n == unlocked, locked = n > unlocked;

        Sprite tileSprite = done ? Art.RooftopSprite(2) : Art.Load(current ? "ui/button_primary" : "ui/button_secondary");
        var tile = Img(cell, "Tile", tileSprite, locked ? new Color(1f, 1f, 1f, 0.7f) : Color.white, sliced: true, raycast: !locked);
        tile.rectTransform.At(new Vector2(0.5f, 1f), Vector2.zero, new Vector2(164f, 164f), new Vector2(0.5f, 1f));

        if (locked)
        {
            Glyph(tile.transform, Icon.Lock, 64, Art.Muted).rectTransform.Fill(0, 14, 0, 0);
        }
        else
        {
            var number = Label(tile.transform, n.ToString(), 70, Color.white);
            number.rectTransform.Fill(0, 16, 0, 0);
            number.Shadowed(done ? LevelDefs.PaletteDark[2] : Art.SaffronShade);
            var button = tile.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            tile.gameObject.AddComponent<PressScale>();
            int level = n;
            button.onClick.AddListener(() => { Sfx.Play(Sfx.Sound.Click); StartCoroutine(LoadLevel(level)); });
            if (current)
            {
                var bob = tile.gameObject.AddComponent<Bob>();
                bob.amplitude = 6f;
                bob.sway = 2f;
            }
        }

        int stars = Progress.Stars(n);
        for (int i = 0; i < 3; i++)
        {
            var star = Glyph(cell, Icon.Star, 48, i < stars ? Art.Saffron : EmptyStar);
            star.rectTransform.At(new Vector2(0.5f, 0f), new Vector2((i - 1) * 48f, 6f), new Vector2(50f, 56f), new Vector2(0.5f, 0f));
            if (i < stars) star.Shadowed(Art.SaffronShade, 3f);
        }
    }

    // ───────────────────────────── In-game HUD ─────────────────────────────

    void BuildHud()
    {
        hud = MakeScreen("Hud");

        var pauseButton = MakeButton(hud, Style.Secondary, null, Icon.Pause, 64, () => OpenPopup(pause));
        pauseButton.GetComponent<RectTransform>().At(new Vector2(0f, 1f), new Vector2(26f, -24f), new Vector2(116f, 116f));

        var levelPill = Panel(hud, "LevelPill");
        levelPill.raycastTarget = false;
        levelPill.rectTransform.At(new Vector2(0.5f, 1f), new Vector2(0f, -24f), new Vector2(280f, 110f), new Vector2(0.5f, 1f));
        hudLevel = Label(levelPill.transform, "Level 1", 54, Art.Ink);
        hudLevel.rectTransform.Fill(0, 12, 0, 0);

        var heartPill = Panel(hud, "Hearts");
        heartPill.raycastTarget = false;
        heartPill.rectTransform.At(new Vector2(1f, 1f), new Vector2(-26f, -24f), new Vector2(230f, 110f));
        hearts = new Text[TangleBoard.MaxHearts];
        for (int i = 0; i < hearts.Length; i++)
        {
            hearts[i] = Glyph(heartPill.transform, Icon.Heart, 60, Art.Danger);
            hearts[i].rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2((i - 1) * 64f, 6f), new Vector2(64f, 64f), new Vector2(0.5f, 0.5f));
        }

        var tipChip = Img(hud, "TipChip", Art.Load("ui/button_secondary"), Chip, sliced: true);
        tipChip.rectTransform.At(new Vector2(0.5f, 1f), new Vector2(0f, -142f), new Vector2(760f, 48f), new Vector2(0.5f, 1f));
        tipChip.pixelsPerUnitMultiplier = SlicePpu * 2.2f;
        hudTip = Label(tipChip.transform, "", 32, Art.Ink, Art.Body);
        hudTip.rectTransform.Fill(0, 2, 0, 0);

        // Bottom row: Undo, Cut, Hint, Freeze, Restart.
        boosterButtons = new Button[BoosterNames.Length];
        boosterBadges = new Text[BoosterNames.Length];
        float[] boosterX = { -416f, -208f, 208f };
        for (int b = 0; b < BoosterNames.Length; b++)
        {
            var booster = (Progress.Booster)b;
            boosterButtons[b] = BarButton(Style.Secondary, BoosterNames[b], BoosterIcons[b], boosterX[b], () => OnBooster(booster), out boosterBadges[b]);
        }
        BarButton(Style.Primary, "Hint", Icon.Hint, 0f, OnHint, out hintBadge);
        BarButton(Style.Secondary, "Restart", Icon.Restart, 416f, () => StartCoroutine(LoadLevel(board.LevelNumber)), out _, withBadge: false);
    }

    // A square HUD button with the icon on top, a small caption under it and a count badge.
    Button BarButton(Style style, string caption, string icon, float x, System.Action onClick, out Text badge, bool withBadge = true)
    {
        var button = MakeButton(hud, style, null, icon, 64, onClick);
        button.GetComponent<RectTransform>().At(new Vector2(0.5f, 0f), new Vector2(x, 40f), new Vector2(196f, 168f), new Vector2(0.5f, 0f));
        ((RectTransform)button.transform.Find("Content")).Fill(16, 58, 16, 4);
        var label = Label(button.transform, caption, 32, style == Style.Primary ? Color.white : Art.Ink, name: "Caption");
        label.rectTransform.At(new Vector2(0.5f, 0f), new Vector2(0f, 22f), new Vector2(190f, 44f), new Vector2(0.5f, 0f));
        if (style == Style.Primary) label.Shadowed(Art.SaffronShade, 3f);
        badge = null;
        if (!withBadge) return button;
        var dot = Img(button.transform, "Badge", SpriteFactory.Circle, Art.Danger);
        dot.rectTransform.At(new Vector2(1f, 1f), new Vector2(-30f, -28f), new Vector2(52f, 52f), new Vector2(0.5f, 0.5f));
        badge = Label(dot.transform, "0", 32, Color.white);
        badge.rectTransform.Fill(0, 4, 0, 0);
        return button;
    }

    void RefreshHud()
    {
        if (board.State == TangleBoard.GameState.Idle) return;
        hudLevel.text = $"Level {board.LevelNumber}";
        for (int i = 0; i < hearts.Length; i++)
            hearts[i].color = i >= board.Hearts ? new Color(Art.Ink.r, Art.Ink.g, Art.Ink.b, 0.18f) : board.Frozen ? Icy : Art.Danger;
        RefreshBoosters();
        if (board.Hearts < shownHearts && board.Hearts >= 0 && board.Hearts < hearts.Length)
            StartCoroutine(Punch(hearts[board.Hearts].rectTransform));
        shownHearts = board.Hearts;

        bool warn = true;
        if (board.CutMode)
        {
            hudTip.text = "Cut: tap any kite (tap Cut again to cancel)";
            warn = false;
        }
        else if (board.Stuck)
            hudTip.text = board.BonusSlotAvailable ? "Stuck! Try Undo, the +1 slot or Restart" : "No way out! Undo, Cut or Restart";
        else if (board.LevelNumber > 3 && board.SlotsFree == 0)
            hudTip.text = "Slots full! Only a matching patang fits";
        else if (board.LevelNumber > 3 && board.SlotsFree == 1)
            hudTip.text = "Careful: 1 slot left!";
        else
        {
            warn = false;
            hudTip.text = board.LevelNumber switch
            {
                1 => "Tap a kite whose dor lies on top of the others",
                2 => "Wrong color? The patang waits in a slot",
                3 => "Tap the +1 FREE slot for an extra slot",
                5 => "New: Undo, Cut and Freeze boosters below",
                LevelGenerator.IceFrom => "Iced kite: tap once to crack, again to free",
                LevelGenerator.HiddenFrom => "Gray kite? Its color shows once its dor is clear",
                LevelGenerator.TwinFrom => "Double dor: clear both strings to free it",
                _ when board.Windy => "Hawa! Streamer kites have swapped",
                LevelGenerator.WindFrom => "Streamer kites swap when a chhat leaves",
                _ => board.ChhatsLeft == 1 ? "Last chhat!" : $"{board.ChhatsLeft} chhats left",
            };
        }
        hudTip.color = warn ? Art.Danger : Art.Ink;
    }

    void OnHint()
    {
        if (board.Stuck)
        {
            Toast(board.BonusSlotAvailable ? "No winning move. Try Undo or the +1 slot" : "No winning move left. Undo or Restart!");
            return;
        }
        if (Progress.Hints <= 0)
        {
            Toast("Out of hints! Get more in the shop");
            OpenPopup(shop);
            return;
        }
        switch (board.ShowHint())
        {
            case TangleBoard.HintResult.Shown: Progress.TryUseHint(); break;
            case TangleBoard.HintResult.NoMoves:
                Toast(board.BonusSlotAvailable ? "No winning move. Try Undo or the +1 slot" : "No winning move left. Undo or Restart!");
                break;
        }
    }

    void OnBonusSlotTapped() =>
        ShowOffer(Icon.AddSlot, "+1 Slot", "One more waiting slot for this level.", "bonus_slot", BonusSlotPrice, () =>
        {
            board.UnlockBonusSlot();
            Toast("+1 slot!");
        });

    // ───────────────────────────── Boosters ─────────────────────────────

    void OnBooster(Progress.Booster booster)
    {
        if (board.State != TangleBoard.GameState.Playing) return;
        int b = (int)booster;
        switch (booster)
        {
            case Progress.Booster.Undo when !board.CanUndo:
                Toast("Nothing to undo yet");
                return;
            case Progress.Booster.Cut when board.CutMode:
                board.CutMode = false; // cancel: the booster isn't used
                RefreshHud();
                return;
            case Progress.Booster.Freeze when board.Frozen:
                Toast("Hearts are already frozen");
                return;
        }
        if (Progress.Boosters(booster) > 0) { ApplyBooster(booster); return; }
        ShowOffer(BoosterIcons[b], BoosterNames[b], BoosterInfo[b], "booster_" + BoosterNames[b].ToLowerInvariant(), BoosterPrices[b], () =>
        {
            Progress.AddBoosters(booster, 1);
            ApplyBooster(booster);
        });
    }

    void ApplyBooster(Progress.Booster booster)
    {
        switch (booster)
        {
            case Progress.Booster.Undo:
                if (board.Undo()) Progress.TryUseBooster(booster);
                break;
            case Progress.Booster.Cut:
                board.CutMode = true; // used up when a kite is actually cut (OnCutDone)
                Toast("Tap any kite to cut its string");
                RefreshHud();
                break;
            case Progress.Booster.Freeze:
                board.Freeze();
                Progress.TryUseBooster(booster);
                Toast("Hearts frozen for this level!");
                break;
        }
    }

    void OnCutDone(bool cut)
    {
        if (cut) Progress.TryUseBooster(Progress.Booster.Cut);
        else Toast("No room for that kite. Pick another");
    }

    void OnWindTurned() => Toast("Hawa! The wind swapped the streamer kites");

    // Counts on the booster buttons; buttons that can't do anything right now are faded.
    void RefreshBoosters()
    {
        if (boosterButtons == null) return;
        for (int b = 0; b < boosterButtons.Length; b++)
        {
            var booster = (Progress.Booster)b;
            int count = Progress.Boosters(booster);
            boosterBadges[b].text = count > 0 ? count.ToString() : "+";
            bool usable = board.State == TangleBoard.GameState.Playing && booster switch
            {
                Progress.Booster.Undo => board.CanUndo,
                Progress.Booster.Freeze => !board.Frozen,
                _ => true,
            };
            bool active = booster == Progress.Booster.Cut && board.CutMode || booster == Progress.Booster.Freeze && board.Frozen;
            boosterButtons[b].GetComponent<Image>().color = active ? (booster == Progress.Booster.Freeze ? Icy : Art.Saffron)
                : usable ? Color.white : new Color(1f, 1f, 1f, 0.55f);
        }
    }

    // ───────────────────────────── Ads or coins ─────────────────────────────

    // Everything an ad can pay for can also be paid with coins. With Remove Ads the "ad" choice is free
    // (Progress.FreeRewardsPerDay times a day), then a normal optional ad.
    void Rewarded(string placement, System.Action grant)
    {
        if (Progress.NoAds && Progress.TryClaimFreeReward()) { grant(); return; }
        Ads.ShowRewarded(placement, ok => { if (ok) grant(); else NoAd(); });
    }

    static bool FreeReward => Progress.NoAds && Progress.FreeRewardsLeft > 0;
    static string RewardLabel => FreeReward ? "Free" : "Watch ad";
    static string RewardIcon => FreeReward ? Icon.Gift : Icon.Ad;
    static bool RewardReady => FreeReward || Ads.RewardedReady;

    void BuildOffer()
    {
        var card = Popup("Offer", 1000f, out offer);
        CloseButton(card, () => ClosePopup(offer));
        offerIcon = Glyph(card, Icon.AddSlot, 150, Art.SaffronShade);
        offerIcon.rectTransform.At(new Vector2(0.5f, 1f), new Vector2(0f, -50f), new Vector2(180f, 180f), new Vector2(0.5f, 1f));
        offerTitle = Label(card, "", 90, Art.Ink);
        offerTitle.rectTransform.At(new Vector2(0.5f, 1f), new Vector2(0f, -235f), new Vector2(680f, 120f), new Vector2(0.5f, 1f));
        offerBody = Label(card, "", 40, Art.Muted, Art.Body);
        offerBody.horizontalOverflow = HorizontalWrapMode.Wrap;
        offerBody.rectTransform.At(new Vector2(0.5f, 1f), new Vector2(0f, -360f), new Vector2(640f, 130f), new Vector2(0.5f, 1f));

        offerAd = MakeButton(card, Style.Primary, "Watch ad", Icon.Ad, 60, () => Rewarded(offerPlacement, () => { ClosePopup(offer); offerGrant?.Invoke(); }));
        offerAd.GetComponent<RectTransform>().At(new Vector2(0.5f, 0f), new Vector2(0f, 230f), new Vector2(620f, 160f), new Vector2(0.5f, 0f));
        offerCoins = OnCardButton(card, "50", null, 60, () =>
        {
            if (!Progress.TrySpendCoins(offerPrice)) { Sfx.Play(Sfx.Sound.Wrong); Toast("Not enough coins"); return; }
            Sfx.Play(Sfx.Sound.Coin);
            ClosePopup(offer);
            offerGrant?.Invoke();
        });
        AddCoinIcon(offerCoins);
        offerCoins.GetComponent<RectTransform>().At(new Vector2(0.5f, 0f), new Vector2(0f, 60f), new Vector2(620f, 144f), new Vector2(0.5f, 0f));
    }

    // A coin picture in front of a button's label.
    static void AddCoinIcon(Button button)
    {
        var content = button.transform.Find("Content");
        var pic = Img(content, "Image", Art.Load("ui/coin"), Color.white);
        pic.transform.SetAsFirstSibling();
        var le = pic.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth = le.preferredHeight = 66f;
    }

    void ShowOffer(string icon, string title, string body, string placement, int price, System.Action grant)
    {
        offerIcon.text = icon;
        offerTitle.text = title;
        offerBody.text = body;
        offerPlacement = placement;
        offerPrice = price;
        offerGrant = grant;
        LabelOf(offerAd).text = RewardLabel;
        IconOf(offerAd).text = RewardIcon;
        LabelOf(offerCoins).text = price.ToString();
        OpenPopup(offer);
    }

    // ───────────────────────────── Win ─────────────────────────────

    void BuildWin()
    {
        win = MakeScreen("Win", fullBleed: true);
        win.gameObject.AddComponent<CanvasGroup>();
        var background = Img(win, "Background", Art.Load("backgrounds/win_yellow"), Color.white, raycast: true);
        background.preserveAspect = false;
        background.rectTransform.Fill();
        var content = Node("Content", win).Fill();
        content.gameObject.AddComponent<SafeArea>();

        var rays = Img(content, "Rays", SpriteFactory.Rays, new Color(1f, 1f, 1f, 0.55f));
        rays.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0f, 150f), new Vector2(1500f, 1500f), new Vector2(0.5f, 0.5f));
        rays.gameObject.AddComponent<Spin>();
        var glow = Img(content, "Glow", SpriteFactory.Glow, new Color(1f, 1f, 0.9f, 0.8f));
        glow.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0f, 150f), new Vector2(900f, 900f), new Vector2(0.5f, 0.5f));

        DecorKite(content, 0, new Vector2(0.5f, 0.5f), new Vector2(-330f, 700f), 130f, 0f);
        DecorKite(content, 1, new Vector2(0.5f, 0.5f), new Vector2(320f, 740f), 110f, 1.2f);
        DecorKite(content, 2, new Vector2(0.5f, 0.5f), new Vector2(-370f, -680f), 100f, 2.5f);
        DecorKite(content, 4, new Vector2(0.5f, 0.5f), new Vector2(360f, -660f), 120f, 3.7f);

        var title = Label(content, "Bo Kata!", 190, Art.Cream);
        title.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0f, 470f), new Vector2(1000f, 240f), new Vector2(0.5f, 0.5f));
        title.Outlined(Art.Ink, 7f).Shadowed(Art.Ink, 14f);
        winTitleLevel = Label(content, "Level 1 complete", 56, Art.Roof);
        winTitleLevel.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0f, 330f), new Vector2(900f, 80f), new Vector2(0.5f, 0.5f));

        winStars = new Text[3];
        for (int i = 0; i < 3; i++)
        {
            winStars[i] = Glyph(content, Icon.Star, i == 1 ? 210 : 170, Art.Saffron);
            winStars[i].rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2((i - 1) * 220f, i == 1 ? 150f : 110f),
                new Vector2(220f, 220f), new Vector2(0.5f, 0.5f));
            winStars[i].Outlined(Art.Ink, 5f);
        }
        winRating = Label(content, "", 76, Art.Saffron);
        winRating.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0f, -30f), new Vector2(900f, 100f), new Vector2(0.5f, 0.5f));
        winRating.Outlined(Art.Ink, 5f).Shadowed(Art.Ink, 8f);

        var reward = Panel(content, "Reward");
        winReward = reward.rectTransform;
        reward.raycastTarget = false;
        reward.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0f, -160f), new Vector2(300f, 116f), new Vector2(0.5f, 0.5f));
        Img(reward.transform, "Coin", Art.Load("ui/coin"), Color.white).rectTransform.At(new Vector2(0f, 0.5f), new Vector2(24f, 4f), new Vector2(76f, 76f));
        winCoins = Label(reward.transform, "+25", 56, Art.Ink, align: TextAnchor.MiddleLeft);
        winCoins.rectTransform.Fill(116, 12, 10, 0);

        var homeButton = MakeButton(content, Style.Secondary, null, Icon.Home, 76, ShowHome);
        homeButton.GetComponent<RectTransform>().At(new Vector2(0.5f, 0.5f), new Vector2(-235f, -360f), new Vector2(170f, 170f), new Vector2(0.5f, 0.5f));
        var next = MakeButton(content, Style.Primary, "Next Level", Icon.Next, 64, OnWinNext);
        next.GetComponent<RectTransform>().At(new Vector2(0.5f, 0.5f), new Vector2(95f, -360f), new Vector2(460f, 170f), new Vector2(0.5f, 0.5f));

        winConfetti = Node("Confetti", win).Fill().gameObject.AddComponent<Confetti>();
    }

    // The only place an interstitial can show (Ads.AfterWin decides; most wins show none).
    void OnWinNext()
    {
        if (busy) return;
        int next = board.LevelNumber + 1;
        Ads.AfterWin(board.LevelNumber, () => StartCoroutine(LoadLevel(next)));
    }

    IEnumerator ShowWin()
    {
        winTitleLevel.text = $"Level {board.LevelNumber} complete";
        winRating.text = "";
        winCoins.text = "+0";
        winReward.gameObject.SetActive(board.CoinsEarned > 0); // replays without new stars pay nothing
        foreach (var star in winStars) star.rectTransform.localScale = Vector3.zero;
        winConfetti.Clear();
        win.gameObject.SetActive(true);
        RefreshBlock();
        yield return Fade(win.GetComponent<CanvasGroup>(), 0.3f);
        winConfetti.Burst(60);

        for (int i = 0; i < winStars.Length; i++)
        {
            bool earned = i < board.Stars;
            winStars[i].color = earned ? Art.Saffron : EmptyStar;
            winStars[i].GetComponent<Outline>().effectColor = earned ? Art.Ink : new Color(Art.Ink.r, Art.Ink.g, Art.Ink.b, 0.25f);
            if (earned) Sfx.Play(Sfx.Sound.Dock);
            yield return Pop(winStars[i].rectTransform, 0.28f);
        }

        winRating.text = board.Stars >= 3 ? "Perfect!" : board.Stars == 2 ? "Great!" : "Well done!";
        yield return Pop(winRating.rectTransform, 0.25f);
        if (board.CoinsEarned > 0) yield return CountUp(winCoins, board.CoinsEarned, 0.6f);
    }

    // "+0" → "+amount" with coin ticks.
    IEnumerator CountUp(Text label, int amount, float seconds)
    {
        int shown = -1;
        for (float t = 0; t < seconds; t += Time.unscaledDeltaTime)
        {
            int value = Mathf.RoundToInt(amount * Mathf.Sin(t / seconds * Mathf.PI * 0.5f));
            if (value != shown && (value - shown >= amount / 12 || value == amount)) { shown = value; label.text = $"+{value}"; Sfx.Play(Sfx.Sound.Coin); }
            yield return null;
        }
        label.text = $"+{amount}";
    }

    // ───────────────────────────── Lose ─────────────────────────────

    void BuildLose()
    {
        var card = Popup("Lose", 1180f, out lose);
        loseIcon = Glyph(card, Icon.HeartBroken, 170, Art.Danger);
        loseIcon.rectTransform.At(new Vector2(0.5f, 1f), new Vector2(0f, -50f), new Vector2(200f, 200f), new Vector2(0.5f, 1f));
        loseTitle = Label(card, "Out of Hearts", 96, Art.Ink);
        loseTitle.rectTransform.At(new Vector2(0.5f, 1f), new Vector2(0f, -250f), new Vector2(700f, 120f), new Vector2(0.5f, 1f));
        loseMessage = Label(card, "", 40, Art.Muted, Art.Body);
        loseMessage.horizontalOverflow = HorizontalWrapMode.Wrap;
        loseMessage.rectTransform.At(new Vector2(0.5f, 1f), new Vector2(0f, -370f), new Vector2(640f, 130f), new Vector2(0.5f, 1f));

        continueButton = MakeButton(card, Style.Primary, "+1 Slot", Icon.Ad, 56, OnContinue);
        continueButton.GetComponent<RectTransform>().At(new Vector2(0.5f, 0f), new Vector2(0f, 400f), new Vector2(620f, 150f), new Vector2(0.5f, 0f));
        continueCoinsButton = OnCardButton(card, "80", null, 56, OnContinueCoins);
        AddCoinIcon(continueCoinsButton);
        continueCoinsButton.GetComponent<RectTransform>().At(new Vector2(0.5f, 0f), new Vector2(0f, 236f), new Vector2(620f, 144f), new Vector2(0.5f, 0f));

        var homeButton = OnCardButton(card, null, Icon.Home, 70, ShowHome);
        homeButton.GetComponent<RectTransform>().At(new Vector2(0.5f, 0f), new Vector2(-238f, 60f), new Vector2(144f, 144f), new Vector2(0.5f, 0f));
        var retry = OnCardButton(card, "Retry", Icon.Restart, 60, () => StartCoroutine(LoadLevel(board.LevelNumber)));
        retry.GetComponent<RectTransform>().At(new Vector2(0.5f, 0f), new Vector2(82f, 60f), new Vector2(456f, 144f), new Vector2(0.5f, 0f));
    }

    void ShowLose()
    {
        bool noSpace = board.Reason == TangleBoard.LoseReason.NoSpace;
        loseIcon.text = noSpace ? Icon.AddSlot : Icon.HeartBroken;
        loseTitle.text = noSpace ? "No Space!" : "Try Again";
        loseMessage.text = noSpace
            ? "Every slot is full and that patang doesn't match the chhat."
            : "You tapped a patang whose dor was still tangled. Out of hearts!";
        continueButton.gameObject.SetActive(!board.UsedContinue);
        continueCoinsButton.gameObject.SetActive(!board.UsedContinue);
        LabelOf(continueButton).text = (noSpace ? "+1 Slot" : "Refill Hearts") + (FreeReward ? ": Free" : "");
        IconOf(continueButton).text = RewardIcon;
        LabelOf(continueCoinsButton).text = $"{ContinuePrice}  " + (noSpace ? "+1 Slot" : "Refill");
        OpenPopup(lose);
    }

    void OnContinue() => Rewarded("continue", () =>
    {
        ClosePopup(lose);
        board.Continue();
    });

    void OnContinueCoins()
    {
        if (!Progress.TrySpendCoins(ContinuePrice)) { Sfx.Play(Sfx.Sound.Wrong); Toast("Not enough coins"); return; }
        Sfx.Play(Sfx.Sound.Coin);
        ClosePopup(lose);
        board.Continue();
    }

    // ───────────────────────────── Pause, Settings, Shop ─────────────────────────────

    void BuildPause()
    {
        var card = Popup("Pause", 900f, out pause);
        Label(card, "Paused", 96, Art.Ink).rectTransform.At(new Vector2(0.5f, 1f), new Vector2(0f, -50f), new Vector2(600f, 120f), new Vector2(0.5f, 1f));
        CloseButton(card, () => ClosePopup(pause));
        var help = OnCardButton(card, null, Icon.Info, 60, () => ShowTutorial(0));
        help.GetComponent<RectTransform>().At(new Vector2(0f, 1f), new Vector2(-34f, 34f), new Vector2(120f, 120f)); // mirrors Close
        AddToggles(card, -210f);

        var resume = MakeButton(card, Style.Primary, "Resume", Icon.Play, 64, () => ClosePopup(pause));
        resume.GetComponent<RectTransform>().At(new Vector2(0.5f, 0f), new Vector2(0f, 230f), new Vector2(620f, 160f), new Vector2(0.5f, 0f));
        var homeButton = OnCardButton(card, "Home", Icon.Home, 54, ShowHome);
        homeButton.GetComponent<RectTransform>().At(new Vector2(0.5f, 0f), new Vector2(-160f, 60f), new Vector2(300f, 144f), new Vector2(0.5f, 0f));
        var restart = OnCardButton(card, "Restart", Icon.Restart, 54, () => StartCoroutine(LoadLevel(board.LevelNumber)));
        restart.GetComponent<RectTransform>().At(new Vector2(0.5f, 0f), new Vector2(160f, 60f), new Vector2(300f, 144f), new Vector2(0.5f, 0f));
    }

    void BuildSettings()
    {
        var card = Popup("Settings", 780f, out settings);
        settingsCard = card;
        Label(card, "Settings", 96, Art.Ink).rectTransform.At(new Vector2(0.5f, 1f), new Vector2(0f, -50f), new Vector2(600f, 120f), new Vector2(0.5f, 1f));
        CloseButton(card, () => ClosePopup(settings));
        AddToggles(card, -210f);

        resetButton = OnCardButton(card, "Reset progress", Icon.Restart, 48, OnReset);
        resetButton.GetComponent<RectTransform>().At(new Vector2(0.5f, 0f), new Vector2(0f, 60f), new Vector2(620f, 140f), new Vector2(0.5f, 0f));
        LabelOf(resetButton).color = Art.Danger;
        IconOf(resetButton).color = Art.Danger;

        privacyButton = OnCardButton(card, "Privacy choices", Icon.Info, 44, Ads.ShowPrivacyOptions);
        privacyButton.gameObject.SetActive(false);

        cloudButton = OnCardButton(card, "Sign in to save", Icon.Cloud, 44, () =>
        {
            if (CloudSave.SignedIn) Toast("Your progress is saved to Google Play");
            else CloudSave.SignIn();
        });
        cloudButton.gameObject.SetActive(false);
    }

    // The consent answer and the Google Play sign-in arrive after startup, so the optional rows
    // (Google Play above Reset, then Privacy choices) are laid out each time Settings opens or sign-in changes.
    void RefreshSettings()
    {
        float y = 220f;
        bool cloud = CloudSave.Available;
        cloudButton.gameObject.SetActive(cloud);
        if (cloud)
        {
            cloudButton.GetComponent<RectTransform>().At(new Vector2(0.5f, 0f), new Vector2(0f, y), new Vector2(620f, 130f), new Vector2(0.5f, 0f));
            LabelOf(cloudButton).text = CloudSave.SignedIn ? "Saved to Google Play" : "Sign in to save";
            IconOf(cloudButton).text = CloudSave.SignedIn ? Icon.CloudDone : Icon.Cloud;
            y += 160f;
        }
        bool privacy = Ads.PrivacyOptionsRequired;
        privacyButton.gameObject.SetActive(privacy);
        if (privacy)
        {
            privacyButton.GetComponent<RectTransform>().At(new Vector2(0.5f, 0f), new Vector2(0f, y), new Vector2(620f, 130f), new Vector2(0.5f, 0f));
            y += 160f;
        }
        settingsCard.sizeDelta = new Vector2(settingsCard.sizeDelta.x, 780f + (y - 220f));
    }

    void AddToggles(RectTransform card, float y)
    {
        var sound = ToggleButton(card, () => { Progress.Sound = !Progress.Sound; });
        sound.GetComponent<RectTransform>().At(new Vector2(0.5f, 1f), new Vector2(-240f, y), new Vector2(200f, 170f), new Vector2(0.5f, 1f));
        soundToggles.Add(sound);
        var music = ToggleButton(card, () => { Progress.Music = !Progress.Music; Sfx.RefreshMusic(); });
        music.GetComponent<RectTransform>().At(new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(200f, 170f), new Vector2(0.5f, 1f));
        musicToggles.Add(music);
        var vibration = ToggleButton(card, () => { Progress.Vibration = !Progress.Vibration; if (Progress.Vibration) Sfx.Buzz(); });
        vibration.GetComponent<RectTransform>().At(new Vector2(0.5f, 1f), new Vector2(240f, y), new Vector2(200f, 170f), new Vector2(0.5f, 1f));
        vibrationToggles.Add(vibration);
    }

    void OnReset()
    {
        // Two taps: the first arms it for a few seconds.
        var label = LabelOf(resetButton);
        if (resetRoutine == null)
        {
            label.text = "Tap again to reset";
            resetRoutine = StartCoroutine(DisarmReset());
            return;
        }
        StopCoroutine(resetRoutine);
        resetRoutine = null;
        label.text = "Reset progress";
        Progress.Reset();
        board.Clear(Progress.Level);
        Toast("Progress reset");
    }

    IEnumerator DisarmReset()
    {
        yield return new WaitForSecondsRealtime(3f);
        LabelOf(resetButton).text = "Reset progress";
        resetRoutine = null;
    }

    // A scrolling list: Remove Ads, Free Coins, hint packs, booster packs, Kite Skins.
    void BuildShop()
    {
        var card = Popup("Shop", 1580f, out shop, 820f);
        Label(card, "Shop", 96, Art.Ink).rectTransform.At(new Vector2(0.5f, 1f), new Vector2(0f, -50f), new Vector2(500f, 120f), new Vector2(0.5f, 1f));
        CloseButton(card, () => ClosePopup(shop));
        CoinPill(card, withPlus: false).At(new Vector2(0.5f, 1f), new Vector2(0f, -180f), new Vector2(300f, 110f), new Vector2(0.5f, 1f));

        var viewport = Node("Viewport", card).Fill(20, 30, 20, 315);
        viewport.gameObject.AddComponent<RectMask2D>();
        Img(viewport, "HitArea", null, Color.clear, raycast: true).rectTransform.Fill(); // lets drags scroll
        var list = Node("List", viewport);
        list.anchorMin = new Vector2(0f, 1f);
        list.anchorMax = new Vector2(1f, 1f);
        list.pivot = new Vector2(0.5f, 1f);
        list.sizeDelta = Vector2.zero;
        var column = list.gameObject.AddComponent<VerticalLayoutGroup>();
        column.spacing = 18f;
        column.padding = new RectOffset(0, 0, 4, 4);
        column.childAlignment = TextAnchor.UpperCenter;
        column.childControlWidth = column.childControlHeight = false;
        column.childForceExpandWidth = column.childForceExpandHeight = false;
        list.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.content = list;
        scroll.viewport = viewport;
        scroll.horizontal = false;
        scroll.scrollSensitivity = 40f;

        removeAdsButton = ShopRow(list, Icon.NoAds, "Remove Ads", "No ads between levels", "Buy", null, false,
            Purchases.BuyRemoveAds, out removeAdsSubtitle);
        freeCoinsButton = ShopRow(list, Icon.Ad, "Free Coins", $"Watch an ad for +{FreeCoinsAmount}", "Free", Icon.Ad, false,
            () => Rewarded("free_coins", () => { Progress.AddCoins(FreeCoinsAmount); Sfx.Play(Sfx.Sound.Coin); Toast($"+{FreeCoinsAmount} coins!"); }),
            out freeCoinsSubtitle);
        ShopRow(list, Icon.Hint, "3 Hints", "Find the next move", $"{HintPack3Price}", null, true, () => BuyHints(3, HintPack3Price), out _);
        ShopRow(list, Icon.Hint, "10 Hints", "Best value", $"{HintPack10Price}", null, true, () => BuyHints(10, HintPack10Price), out _);
        boosterShopSubtitles = new Text[BoosterNames.Length];
        for (int b = 0; b < BoosterNames.Length; b++)
        {
            var booster = (Progress.Booster)b;
            int price = BoosterPackPrices[b];
            ShopRow(list, BoosterIcons[b], $"{BoosterPack} {BoosterNames[b]}", "", $"{price}", null, true, () =>
            {
                if (!Progress.TrySpendCoins(price)) { Sfx.Play(Sfx.Sound.Wrong); Toast("Not enough coins"); return; }
                Progress.AddBoosters(booster, BoosterPack);
                Sfx.Play(Sfx.Sound.ChhatDone);
                Toast($"+{BoosterPack} {BoosterNames[(int)booster]}!");
            }, out boosterShopSubtitles[b]);
        }
        ShopRow(list, Icon.Palette, "Kite Skins", "Patterns for your patang", "Open", null, false, ShowSkins, out _);
    }

    Button ShopRow(Transform list, string icon, string title, string subtitle, string price, string buttonIcon, bool coins,
        System.Action buy, out Text subtitleLabel)
    {
        var row = Img(list, "Row", Art.Load("ui/button_secondary"), OnCard, sliced: true);
        row.rectTransform.sizeDelta = new Vector2(740f, 180f);
        Glyph(row.transform, icon, 90, Art.SaffronShade).rectTransform.At(new Vector2(0f, 0.5f), new Vector2(28f, 6f), new Vector2(110f, 110f));
        Label(row.transform, title, 52, Art.Ink, align: TextAnchor.MiddleLeft)
            .rectTransform.At(new Vector2(0f, 0.5f), new Vector2(156f, 26f), new Vector2(340f, 70f), new Vector2(0f, 0.5f));
        subtitleLabel = Label(row.transform, subtitle, 30, Art.Muted, Art.Body, TextAnchor.MiddleLeft);
        subtitleLabel.rectTransform.At(new Vector2(0f, 0.5f), new Vector2(156f, -30f), new Vector2(340f, 50f), new Vector2(0f, 0.5f));
        var button = MakeButton(row.transform, Style.Primary, price, buttonIcon, 46, buy, coins ? Art.Load("ui/coin") : null);
        button.GetComponent<RectTransform>().At(new Vector2(1f, 0.5f), new Vector2(-22f, 4f), new Vector2(220f, 120f));
        return button;
    }

    // Prices, owned state and counts change while the game runs, so the shop is refreshed each time it opens.
    void RefreshShop()
    {
        if (removeAdsButton == null) return;
        bool owned = Progress.NoAds;
        LabelOf(removeAdsButton).text = owned ? "Owned" : Purchases.RemoveAdsPrice ?? "Buy";
        removeAdsButton.interactable = !owned;
        removeAdsSubtitle.text = owned ? $"Ads off. {Progress.FreeRewardsLeft} free gifts today" : "No ads between levels";
        LabelOf(freeCoinsButton).text = "Free";
        IconOf(freeCoinsButton).text = RewardIcon;
        freeCoinsSubtitle.text = FreeReward ? $"+{FreeCoinsAmount} coins, no ad" : $"Watch an ad for +{FreeCoinsAmount}";
        for (int b = 0; b < boosterShopSubtitles.Length; b++)
            boosterShopSubtitles[b].text = $"You have {Progress.Boosters((Progress.Booster)b)}";
    }

    // ───────────────────────────── Kite skins ─────────────────────────────

    void BuildSkins()
    {
        var card = Popup("Skins", 1460f, out skins, 860f);
        Label(card, "Kite Skins", 90, Art.Ink).rectTransform.At(new Vector2(0.5f, 1f), new Vector2(0f, -50f), new Vector2(600f, 120f), new Vector2(0.5f, 1f));
        CloseButton(card, () => ClosePopup(skins));
        skinButtons = new Button[Art.SkinFiles.Length];
        for (int i = 0; i < Art.SkinFiles.Length; i++)
        {
            int skin = i;
            var tile = Img(card, "Skin", Art.Load("ui/button_secondary"), OnCard, sliced: true);
            Vector2 at = i == Art.SkinFiles.Length - 1 && i % 2 == 0 ? new Vector2(0f, -190f - i / 2 * 410f)
                : new Vector2(i % 2 == 0 ? -200f : 200f, -190f - i / 2 * 410f);
            tile.rectTransform.At(new Vector2(0.5f, 1f), at, new Vector2(380f, 390f), new Vector2(0.5f, 1f));
            int color = (i + 1) % LevelDefs.ColorNames.Length;
            var kite = Img(tile.transform, "Kite", Art.KiteSprite(color), Color.white);
            kite.preserveAspect = true;
            kite.rectTransform.At(new Vector2(0.5f, 1f), new Vector2(0f, -20f), new Vector2(120f, 168f), new Vector2(0.5f, 1f));
            if (Art.SkinSprite(i) != null)
            {
                var pattern = Img(kite.transform, "Pattern", Art.SkinSprite(i), Color.white);
                pattern.preserveAspect = true;
                pattern.rectTransform.Fill();
            }
            kite.gameObject.AddComponent<Bob>().phase = i * 1.3f;
            Label(tile.transform, Art.SkinNames[i], 44, Art.Ink)
                .rectTransform.At(new Vector2(0.5f, 0f), new Vector2(0f, 120f), new Vector2(360f, 60f), new Vector2(0.5f, 0f));
            skinButtons[i] = MakeButton(tile.transform, Style.Primary, "Use", null, 42, () => OnSkin(skin), Art.Load("ui/coin"));
            skinButtons[i].GetComponent<RectTransform>().At(new Vector2(0.5f, 0f), new Vector2(0f, 18f), new Vector2(300f, 100f), new Vector2(0.5f, 0f));
        }
    }

    void ShowSkins()
    {
        RefreshSkins();
        OpenPopup(skins);
    }

    void RefreshSkins()
    {
        for (int i = 0; i < skinButtons.Length; i++)
        {
            bool owned = Progress.OwnsSkin(i);
            LabelOf(skinButtons[i]).text = !owned ? Art.SkinPrices[i].ToString() : Progress.Skin == i ? "In use" : "Use";
            skinButtons[i].transform.Find("Content/Image").gameObject.SetActive(!owned);
            skinButtons[i].interactable = !(owned && Progress.Skin == i);
        }
    }

    void OnSkin(int skin)
    {
        if (Progress.OwnsSkin(skin)) Progress.UseSkin(skin);
        else if (Progress.TryBuySkin(skin, Art.SkinPrices[skin])) Sfx.Play(Sfx.Sound.Purchase);
        else { Sfx.Play(Sfx.Sound.Wrong); Toast("Not enough coins"); return; }
        Toast($"{Art.SkinNames[skin]} on! Your kites wear it from the next level");
        RefreshSkins();
    }

    void BuildTutorial()
    {
        var card = Popup("HowToPlay", 1120f, out tutorial, 820f);
        tutorialIcon = Glyph(card, Icon.Info, 150, Art.SaffronShade);
        tutorialIcon.rectTransform.At(new Vector2(0.5f, 1f), new Vector2(0f, -40f), new Vector2(180f, 180f), new Vector2(0.5f, 1f));
        tutorialTitle = Label(card, "", 84, Art.Ink);
        tutorialTitle.rectTransform.At(new Vector2(0.5f, 1f), new Vector2(0f, -215f), new Vector2(740f, 110f), new Vector2(0.5f, 1f));
        tutorialBody = Label(card, "", 42, Art.Muted, Art.Body, TextAnchor.UpperCenter);
        tutorialBody.horizontalOverflow = HorizontalWrapMode.Wrap;
        tutorialBody.rectTransform.At(new Vector2(0.5f, 1f), new Vector2(0f, -340f), new Vector2(700f, 520f), new Vector2(0.5f, 1f));
        var ok = MakeButton(card, Style.Primary, "Got it", Icon.Check, 64, () => ClosePopup(tutorial));
        ok.GetComponent<RectTransform>().At(new Vector2(0.5f, 0f), new Vector2(0f, 60f), new Vector2(560f, 160f), new Vector2(0.5f, 0f));
    }

    void ShowTutorial(int key)
    {
        var lesson = Lessons[key];
        tutorialIcon.text = lesson.icon;
        tutorialTitle.text = lesson.title;
        tutorialBody.text = lesson.body;
        OpenPopup(tutorial);
    }

    // A rewarded ad that wasn't loaded yet (or was closed early) gives nothing.
    void NoAd() => Toast(Ads.TestNoFill ? "No ad (ads off for testing). Use coins" : "No ad right now. Use coins or try again");

    void BuyHints(int count, int price)
    {
        if (!Progress.TrySpendCoins(price))
        {
            Sfx.Play(Sfx.Sound.Wrong);
            Toast("Not enough coins");
            return;
        }
        Progress.AddHints(count);
        Sfx.Play(Sfx.Sound.ChhatDone);
        Toast($"+{count} hints!");
    }

    // ───────────────────────────── Stats ─────────────────────────────

    static readonly (string icon, string label)[] StatTiles =
    {
        (Icon.Map, "Levels done"), (Icon.Star, "Stars"),
        (Icon.Star, "3-star levels"), (Icon.Play, "Games played"),
        (Icon.Trophy, "Wins"), (Icon.Check, "Win rate"),
        (Icon.Streak, "Win streak"), (Icon.Streak, "Best streak"),
        (Icon.Heart, "Kites freed"), (Icon.Hint, "Hints used"),
        (Icon.Undo, "Boosters used"), (Icon.Palette, "Skins owned"),
    };

    void BuildStats()
    {
        var card = Popup("Stats", 1410f, out stats, 860f);
        Label(card, "Stats", 96, Art.Ink).rectTransform.At(new Vector2(0.5f, 1f), new Vector2(0f, -50f), new Vector2(500f, 120f), new Vector2(0.5f, 1f));
        CloseButton(card, () => ClosePopup(stats));

        statValues = new Text[StatTiles.Length];
        for (int i = 0; i < StatTiles.Length; i++)
        {
            var tile = Img(card, "Stat", Art.Load("ui/button_secondary"), OnCard, sliced: true);
            tile.rectTransform.At(new Vector2(0.5f, 1f), new Vector2(i % 2 == 0 ? -195f : 195f, -200f - i / 2 * 170f), new Vector2(370f, 150f), new Vector2(0.5f, 1f));
            Glyph(tile.transform, StatTiles[i].icon, 64, Art.SaffronShade).rectTransform.At(new Vector2(0f, 0.5f), new Vector2(20f, 4f), new Vector2(80f, 80f));
            statValues[i] = Label(tile.transform, "0", 58, Art.Ink, align: TextAnchor.MiddleLeft);
            statValues[i].rectTransform.At(new Vector2(0f, 0.5f), new Vector2(112f, 22f), new Vector2(240f, 70f), new Vector2(0f, 0.5f));
            Label(tile.transform, StatTiles[i].label, 30, Art.Muted, Art.Body, TextAnchor.MiddleLeft)
                .rectTransform.At(new Vector2(0f, 0.5f), new Vector2(112f, -32f), new Vector2(240f, 44f), new Vector2(0f, 0.5f));
        }

        statsFooter = Label(card, "", 34, Art.Muted, Art.Body);
        statsFooter.rectTransform.At(new Vector2(0.5f, 0f), new Vector2(0f, 40f), new Vector2(760f, 60f), new Vector2(0.5f, 0f));
    }

    void ShowStats()
    {
        int wins = Progress.Get(Progress.Stat.Wins), losses = Progress.Get(Progress.Stat.Losses);
        int[] values =
        {
            Progress.Level - 1, Progress.TotalStars,
            Progress.ThreeStarLevels, Progress.Get(Progress.Stat.Played),
            wins, wins + losses == 0 ? 0 : Mathf.RoundToInt(100f * wins / (wins + losses)),
            Progress.Get(Progress.Stat.Streak), Progress.Get(Progress.Stat.BestStreak),
            Progress.Get(Progress.Stat.KitesFreed), Progress.Get(Progress.Stat.HintsUsed),
            Progress.Get(Progress.Stat.BoostersUsed), SkinsOwned(),
        };
        for (int i = 0; i < values.Length; i++) statValues[i].text = i == 5 ? $"{values[i]}%" : values[i].ToString();
        statsFooter.text = CloudSave.SignedIn ? "Saved on this phone and to Google Play" : "Saved on this phone";
        OpenPopup(stats);
    }

    static int SkinsOwned()
    {
        int count = 0;
        for (int i = 0; i < Art.SkinFiles.Length; i++) if (Progress.OwnsSkin(i)) count++;
        return count;
    }

    // ───────────────────────────── Loading and toast ─────────────────────────────

    void BuildLoading()
    {
        loading = Node("Loading", root).Fill();
        Img(loading, "Dim", null, new Color(Art.Ink.r, Art.Ink.g, Art.Ink.b, 0.35f), raycast: true).rectTransform.Fill();
        var pill = Panel(loading, "Pill");
        pill.rectTransform.At(new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(520f, 150f), new Vector2(0.5f, 0.5f));
        Label(pill.transform, "Tangling kites...", 50, Art.Ink).rectTransform.Fill(0, 14, 0, 0);
        loading.gameObject.SetActive(false);
    }

    void BuildToast()
    {
        toast = Node("Toast", root).At(new Vector2(0.5f, 0f), new Vector2(0f, 330f), new Vector2(760f, 100f), new Vector2(0.5f, 0f));
        toast.gameObject.AddComponent<CanvasGroup>().blocksRaycasts = false;
        Img(toast, "Bg", Art.Load("ui/button_secondary"), new Color(Art.Ink.r, Art.Ink.g, Art.Ink.b, 0.9f), sliced: true).rectTransform.Fill();
        toastText = Label(toast, "", 40, Art.Cream, Art.Body);
        toastText.rectTransform.Fill(20, 10, 20, 0);
        toast.gameObject.SetActive(false);
    }

    void Toast(string message)
    {
        if (toastRoutine != null) StopCoroutine(toastRoutine);
        toastRoutine = StartCoroutine(ToastRoutine(message));
    }

    IEnumerator ToastRoutine(string message)
    {
        toastText.text = message;
        toast.SetAsLastSibling();
        toast.gameObject.SetActive(true);
        var group = toast.GetComponent<CanvasGroup>();
        yield return Fade(group, 0.15f);
        yield return new WaitForSecondsRealtime(1.6f);
        for (float t = 0; t < 0.3f; t += Time.unscaledDeltaTime)
        {
            group.alpha = 1f - t / 0.3f;
            yield return null;
        }
        toast.gameObject.SetActive(false);
        toastRoutine = null;
    }

    // ───────────────────────────── Flow ─────────────────────────────

    IEnumerator LoadLevel(int number)
    {
        if (busy) yield break;
        busy = true;
        CloseAllPopups();
        win.gameObject.SetActive(false);
        RefreshBlock();

        // Levels are generated on a worker thread; the pill only shows if this one isn't ready yet.
        var task = LevelCache.Request(number);
        if (!task.IsCompleted)
        {
            loading.SetAsLastSibling();
            loading.gameObject.SetActive(true);
            while (!task.IsCompleted) yield return null;
        }
        loading.gameObject.SetActive(false);
        if (task.IsFaulted)
        {
            Debug.LogException(task.Exception);
            busy = false;
            Toast("Couldn't build that level");
            ShowHome();
            yield break;
        }

        shownState = TangleBoard.GameState.Playing;
        shownHearts = TangleBoard.MaxHearts;
        board.LoadLevel(task.Result);
        Progress.RecordStart();
        LevelCache.Prefetch(number + 1); // built while this one is played
        busy = false;
        ShowOnly(hud);
        RefreshHud();

        if (Lessons.ContainsKey(number) && number > 0 && !Progress.TutorialSeen(number))
        {
            Progress.MarkTutorialSeen(number);
            ShowTutorial(number);
        }
    }

    void OnBoardChanged()
    {
        RefreshHud();
        if (board.State == shownState) return;
        shownState = board.State;
        if (shownState == TangleBoard.GameState.Won) StartCoroutine(After(WinDelay, TangleBoard.GameState.Won, () => StartCoroutine(ShowWin())));
        else if (shownState == TangleBoard.GameState.Lost) StartCoroutine(After(LoseDelay, TangleBoard.GameState.Lost, ShowLose));
        RefreshBlock();
    }

    IEnumerator After(float seconds, TangleBoard.GameState expected, System.Action action)
    {
        int levelNumber = board.LevelNumber;
        yield return new WaitForSeconds(seconds);
        if (board.State == expected && board.LevelNumber == levelNumber && !busy) action();
    }

    void ShowOnly(RectTransform screen)
    {
        foreach (var s in new[] { home, map, hud, win }) s.gameObject.SetActive(s == screen);
        RefreshBlock();
    }

    void OpenPopup(RectTransform popup)
    {
        if (popups.Contains(popup)) return;
        if (popup == shop) RefreshShop();
        popups.Add(popup);
        popup.SetAsLastSibling();
        popup.gameObject.SetActive(true);
        RefreshToggles();
        RefreshBlock();
        StartCoroutine(PopIn(popup));
    }

    void ClosePopup(RectTransform popup)
    {
        popups.Remove(popup);
        popup.gameObject.SetActive(false);
        RefreshBlock();
    }

    void CloseAllPopups()
    {
        foreach (var p in popups) p.gameObject.SetActive(false);
        popups.Clear();
        RefreshBlock();
    }

    // The board only takes taps while the HUD is up and nothing covers it.
    void RefreshBlock()
    {
        if (board == null || hud == null) return;
        board.InputBlocked = busy || popups.Count > 0 || !hud.gameObject.activeSelf || win.gameObject.activeSelf;
    }

    void RefreshProgress()
    {
        foreach (var label in coinLabels) label.text = Progress.Coins.ToString();
        if (playLabel != null) playLabel.text = $"Level {Progress.Level}";
        if (hintBadge != null) hintBadge.text = Progress.Hints > 0 ? Progress.Hints.ToString() : "+";
        if (board != null && hud != null && hud.gameObject.activeSelf) RefreshBoosters();
        if (popups.Contains(shop)) RefreshShop();
        RefreshToggles();
    }

    void RefreshToggles()
    {
        foreach (var b in soundToggles)
        {
            IconOf(b).text = Progress.Sound ? Icon.SoundOn : Icon.SoundOff;
            b.transform.Find("Caption").GetComponent<Text>().text = Progress.Sound ? "Sound on" : "Sound off";
        }
        foreach (var b in musicToggles)
        {
            IconOf(b).text = Progress.Music ? Icon.Music : Icon.MusicOff;
            b.transform.Find("Caption").GetComponent<Text>().text = Progress.Music ? "Music on" : "Music off";
        }
        foreach (var b in vibrationToggles)
        {
            IconOf(b).text = Progress.Vibration ? Icon.Vibration : Icon.VibrationOff;
            b.transform.Find("Caption").GetComponent<Text>().text = Progress.Vibration ? "Vibration on" : "Vibration off";
        }
    }

    // Android: leaving the app sends any progress the cloud save hasn't got yet.
    void OnApplicationPause(bool paused)
    {
        if (paused) CloudSave.Flush();
    }

    void Update()
    {
        // First levels: if the player waits, point at the right kite for free.
        if (board.State == TangleBoard.GameState.Playing && board.LevelNumber <= TutorialLevels && !board.InputBlocked
            && !board.Stuck && !board.HintActive && board.IdleTime > TutorialIdleSeconds)
            board.ShowHint();

        // Ad buttons fade while no ad is loaded (tapping still says so); the coin choice always works.
        if (popups.Count > 0)
        {
            var faded = new Color(1f, 1f, 1f, RewardReady ? 1f : 0.5f);
            offerAd.GetComponent<Image>().color = faded;
            continueButton.GetComponent<Image>().color = faded;
            freeCoinsButton.GetComponent<Image>().color = faded;
        }

        if (!BackPressed() || busy) return;
        if (popups.Count > 0)
        {
            var top = popups[popups.Count - 1];
            if (top != lose) ClosePopup(top); // the lose popup needs a choice
        }
        else if (map.gameObject.activeSelf) ShowHome();
        else if (win.gameObject.activeSelf) ShowHome();
        else if (hud.gameObject.activeSelf && board.State == TangleBoard.GameState.Playing) OpenPopup(pause);
    }

    static bool BackPressed()
    {
#if ENABLE_INPUT_SYSTEM
        var keyboard = UnityEngine.InputSystem.Keyboard.current;
        return keyboard != null && keyboard.escapeKey.wasPressedThisFrame; // Android back arrives as Escape
#else
        return Input.GetKeyDown(KeyCode.Escape);
#endif
    }

    // ───────────────────────────── Tweens ─────────────────────────────

    IEnumerator PopIn(RectTransform popup)
    {
        var group = popup.GetComponent<CanvasGroup>();
        var card = popup.Find("Card");
        for (float t = 0; t < 0.22f; t += Time.unscaledDeltaTime)
        {
            float k = t / 0.22f;
            if (group != null) group.alpha = k;
            if (card != null) card.localScale = Vector3.one * Mathf.LerpUnclamped(0.85f, 1f, EaseOutBack(k));
            yield return null;
        }
        if (group != null) group.alpha = 1f;
        if (card != null) card.localScale = Vector3.one;
    }

    static IEnumerator Fade(CanvasGroup group, float seconds)
    {
        for (float t = 0; t < seconds; t += Time.unscaledDeltaTime)
        {
            group.alpha = t / seconds;
            yield return null;
        }
        group.alpha = 1f;
    }

    static IEnumerator Pop(RectTransform rt, float seconds)
    {
        for (float t = 0; t < seconds; t += Time.unscaledDeltaTime)
        {
            rt.localScale = Vector3.one * EaseOutBack(t / seconds);
            yield return null;
        }
        rt.localScale = Vector3.one;
    }

    static IEnumerator Punch(RectTransform rt)
    {
        for (float t = 0; t < 0.35f; t += Time.unscaledDeltaTime)
        {
            rt.localScale = Vector3.one * (1f + 0.5f * Mathf.Sin(t / 0.35f * Mathf.PI));
            yield return null;
        }
        rt.localScale = Vector3.one;
    }
}
