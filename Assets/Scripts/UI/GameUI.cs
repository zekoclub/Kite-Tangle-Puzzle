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
            "Running out of room? Tap the locked +1 FREE slot to open one more slot for this level.\n\n" +
            "Stuck? Hint shows the next good move."),
        [0] = (Icon.Info, "How to play",
            "Tap a patang whose dor lies on top of every string it crosses. Tangled tap = lose a heart.\n\n" +
            "It flies to the chhat of its color, or waits in a slot. A chhat leaves with 3 kites and the next one takes its kites from the slots.\n\n" +
            "All slots full and the wrong color = No Space! Use the +1 FREE slot and Hint."),
    };

    // Screens and popups.
    RectTransform home, map, hud, win, lose, pause, settings, shop, stats, loading, toast, tutorial;
    Text tutorialIcon, tutorialTitle, tutorialBody;
    readonly List<RectTransform> popups = new();
    bool busy; // a level is being generated

    // Things that get refreshed.
    readonly List<Text> coinLabels = new();
    Text playLabel, hudLevel, hudTip, hintBadge, winTitleLevel, winCoins, winRating, loseIcon, loseTitle, loseMessage, toastText;
    Text[] hearts, winStars;
    Button continueButton, resetButton, privacyButton, cloudButton;
    Text[] statValues;
    Text statsFooter;
    RectTransform settingsCard;
    RectTransform mapContent, winReward;
    Confetti winConfetti;
    ScrollRect mapScroll;
    readonly List<Button> soundToggles = new(), vibrationToggles = new();
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
        BuildTutorial();
        BuildLoading();
        BuildToast();

        Progress.Validate();
        Ads.Init();
        CloudSave.Init();
        CloudSave.Changed += RefreshSettings;
        board.Changed += OnBoardChanged;
        board.BonusSlotTapped += OnBonusSlotTapped;
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
        if (board != null)
        {
            board.Changed -= OnBoardChanged;
            board.BonusSlotTapped -= OnBonusSlotTapped;
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

        var hint = MakeButton(hud, Style.Primary, "Hint", Icon.Hint, 56, OnHint);
        hint.GetComponent<RectTransform>().At(new Vector2(0.5f, 0f), new Vector2(-20f, 44f), new Vector2(330f, 140f), new Vector2(1f, 0f));
        var badge = Img(hint.transform, "Badge", SpriteFactory.Circle, Art.Danger);
        badge.rectTransform.At(new Vector2(1f, 1f), new Vector2(14f, 18f), new Vector2(64f, 64f), new Vector2(0.5f, 0.5f));
        hintBadge = Label(badge.transform, "3", 38, Color.white);
        hintBadge.rectTransform.Fill(0, 4, 0, 0);

        var restart = MakeButton(hud, Style.Secondary, "Restart", Icon.Restart, 56, () => StartCoroutine(LoadLevel(board.LevelNumber)));
        restart.GetComponent<RectTransform>().At(new Vector2(0.5f, 0f), new Vector2(20f, 44f), new Vector2(330f, 140f), new Vector2(0f, 0f));
    }

    void RefreshHud()
    {
        if (board.State == TangleBoard.GameState.Idle) return;
        hudLevel.text = $"Level {board.LevelNumber}";
        for (int i = 0; i < hearts.Length; i++)
            hearts[i].color = i < board.Hearts ? Art.Danger : new Color(Art.Ink.r, Art.Ink.g, Art.Ink.b, 0.18f);
        if (board.Hearts < shownHearts && board.Hearts >= 0 && board.Hearts < hearts.Length)
            StartCoroutine(Punch(hearts[board.Hearts].rectTransform));
        shownHearts = board.Hearts;

        bool warn = true;
        if (board.Stuck)
            hudTip.text = board.BonusSlotAvailable ? "Stuck! Try the +1 FREE slot or Restart" : "No way out! Tap Restart";
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
                _ => board.ChhatsLeft == 1 ? "Last chhat!" : $"{board.ChhatsLeft} chhats left",
            };
        }
        hudTip.color = warn ? Art.Danger : Art.Ink;
    }

    void OnHint()
    {
        if (board.Stuck)
        {
            Toast(board.BonusSlotAvailable ? "No winning move. Try the +1 FREE slot" : "No winning move left. Restart!");
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
                Toast(board.BonusSlotAvailable ? "No winning move. Try the +1 FREE slot" : "No winning move left. Restart!");
                break;
        }
    }

    void OnBonusSlotTapped()
    {
        Ads.ShowRewarded("bonus_slot", ok =>
        {
            if (!ok) { NoAd(); return; }
            board.UnlockBonusSlot();
            Toast("+1 slot!");
        });
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
        var card = Popup("Lose", 1000f, out lose);
        loseIcon = Glyph(card, Icon.HeartBroken, 170, Art.Danger);
        loseIcon.rectTransform.At(new Vector2(0.5f, 1f), new Vector2(0f, -50f), new Vector2(200f, 200f), new Vector2(0.5f, 1f));
        loseTitle = Label(card, "Out of Hearts", 96, Art.Ink);
        loseTitle.rectTransform.At(new Vector2(0.5f, 1f), new Vector2(0f, -250f), new Vector2(700f, 120f), new Vector2(0.5f, 1f));
        loseMessage = Label(card, "", 40, Art.Muted, Art.Body);
        loseMessage.horizontalOverflow = HorizontalWrapMode.Wrap;
        loseMessage.rectTransform.At(new Vector2(0.5f, 1f), new Vector2(0f, -370f), new Vector2(640f, 130f), new Vector2(0.5f, 1f));

        continueButton = MakeButton(card, Style.Primary, "+1 Slot", Icon.Ad, 60, OnContinue);
        continueButton.GetComponent<RectTransform>().At(new Vector2(0.5f, 0f), new Vector2(0f, 230f), new Vector2(620f, 160f), new Vector2(0.5f, 0f));

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
        LabelOf(continueButton).text = noSpace ? "+1 Slot" : "Refill Hearts";
        OpenPopup(lose);
    }

    void OnContinue()
    {
        Ads.ShowRewarded("continue", ok =>
        {
            if (!ok) { NoAd(); return; }
            ClosePopup(lose);
            board.Continue();
        });
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
        sound.GetComponent<RectTransform>().At(new Vector2(0.5f, 1f), new Vector2(-130f, y), new Vector2(200f, 170f), new Vector2(0.5f, 1f));
        soundToggles.Add(sound);
        var vibration = ToggleButton(card, () => { Progress.Vibration = !Progress.Vibration; if (Progress.Vibration) Sfx.Buzz(); });
        vibration.GetComponent<RectTransform>().At(new Vector2(0.5f, 1f), new Vector2(130f, y), new Vector2(200f, 170f), new Vector2(0.5f, 1f));
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

    void BuildShop()
    {
        var card = Popup("Shop", 1180f, out shop, 800f);
        Label(card, "Shop", 96, Art.Ink).rectTransform.At(new Vector2(0.5f, 1f), new Vector2(0f, -50f), new Vector2(500f, 120f), new Vector2(0.5f, 1f));
        CloseButton(card, () => ClosePopup(shop));
        CoinPill(card, withPlus: false).At(new Vector2(0.5f, 1f), new Vector2(0f, -180f), new Vector2(300f, 110f), new Vector2(0.5f, 1f));

        ShopRow(card, -320f, Icon.Hint, "3 Hints", "Find the next move", $"{HintPack3Price}", () => BuyHints(3, HintPack3Price));
        ShopRow(card, -520f, Icon.Hint, "10 Hints", "Best value", $"{HintPack10Price}", () => BuyHints(10, HintPack10Price));
        ShopRow(card, -720f, Icon.Ad, "Free Coins", $"Watch an ad for +{FreeCoinsAmount}", "Free", () =>
            Ads.ShowRewarded("free_coins", ok => { if (ok) { Progress.AddCoins(FreeCoinsAmount); Toast($"+{FreeCoinsAmount} coins!"); } else NoAd(); }));
        ShopRow(card, -920f, Icon.Star, "Kite Skins", "Coming soon", null, null);
    }

    void ShopRow(RectTransform card, float y, string icon, string title, string subtitle, string price, System.Action buy)
    {
        var row = Img(card, "Row", Art.Load("ui/button_secondary"), OnCard, sliced: true);
        row.rectTransform.At(new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(720f, 180f), new Vector2(0.5f, 1f));
        Glyph(row.transform, icon, 90, price == null ? Art.Muted : Art.SaffronShade)
            .rectTransform.At(new Vector2(0f, 0.5f), new Vector2(30f, 6f), new Vector2(110f, 110f));
        Label(row.transform, title, 54, price == null ? Art.Muted : Art.Ink, align: TextAnchor.MiddleLeft)
            .rectTransform.At(new Vector2(0f, 0.5f), new Vector2(160f, 26f), new Vector2(330f, 70f), new Vector2(0f, 0.5f));
        Label(row.transform, subtitle, 32, Art.Muted, Art.Body, TextAnchor.MiddleLeft)
            .rectTransform.At(new Vector2(0f, 0.5f), new Vector2(160f, -30f), new Vector2(330f, 50f), new Vector2(0f, 0.5f));
        if (price == null) return;

        bool coins = price != "Free";
        var button = MakeButton(row.transform, Style.Primary, price, coins ? null : Icon.Ad, 50, buy,
            coins ? Art.Load("ui/coin") : null);
        button.GetComponent<RectTransform>().At(new Vector2(1f, 0.5f), new Vector2(-24f, 4f), new Vector2(210f, 120f));
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
    void NoAd() => Toast("No ad right now. Try again in a moment");

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
    };

    void BuildStats()
    {
        var card = Popup("Stats", 1240f, out stats, 860f);
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
        };
        for (int i = 0; i < values.Length; i++) statValues[i].text = i == 5 ? $"{values[i]}%" : values[i].ToString();
        statsFooter.text = CloudSave.SignedIn ? "Saved on this phone and to Google Play" : "Saved on this phone";
        OpenPopup(stats);
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
        RefreshToggles();
    }

    void RefreshToggles()
    {
        foreach (var b in soundToggles)
        {
            IconOf(b).text = Progress.Sound ? Icon.SoundOn : Icon.SoundOff;
            b.transform.Find("Caption").GetComponent<Text>().text = Progress.Sound ? "Sound on" : "Sound off";
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
