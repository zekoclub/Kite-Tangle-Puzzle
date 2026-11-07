# Kite Tangle Puzzle: project context

A casual mobile puzzle game with a Basant theme (South Asian kite festival), made in **Unity 2D**, portrait, for Android.
Working title: "Kite Tangle" (a store name like "Bo Kata: Kite Tangle Puzzle" is under consideration).
Package id: `com.zekoclub.kitetangle`. Editor: **Unity 6000.0.84f1**.

## The game in one paragraph
Kites fly in the sky and each one is tied by a string to a **charkhi** (spool) on the rooftop at the bottom of the screen. The strings cross each other,
and at every crossing one string lies **on top**. You can free a kite only if **no other string lies on top of its string**.
A freed kite flies to the **active chhat** (a colored rooftop at the top of the screen) if the colors match; otherwise it waits in one of the **slots**.
A chhat leaves once it holds 3 kites, and the next chhat in the queue then takes its matching kites out of the slots.
It combines "pick-up sticks" (Mikado) with the Bus Jam / Parking Jam genre.

### Rules (exact)
- Tap a covered kite → it shakes, the string(s) lying on it flash white, and you lose 1 heart (3 hearts). 0 hearts = **"Try Again"**.
- Tap a free kite that doesn't match the active chhat while every slot is full → **"No Space!"** (you lose).
- Clear every chhat → **"Bo Kata!"**, and progress is saved to `PlayerPrefs["level"]`.
- **Hint** runs the solver from the current state and pulses the next correct kite.

## Architecture (`Assets/Scripts/`)
All objects are created from code at runtime. The scene holds only a Camera, a `TangleBoard` object and a `GameUI` object, and there are no prefabs.
Art and fonts load from `Assets/Resources` (`Art/…` sprites, `Fonts/…`); the source files are in `MockUps/…/assets` (svg + png + `palette.json`).

| File | Role |
|---|---|
| `LevelDefs.cs` | Data types: `KiteDef` (colorId, layer, spool, anchor, bend in **normalized** 0..1 coordinates), `LevelData` (kites, `blockerMask[]`, chhat `queue[]`, `slots`), the color `Palette`/`PaletteDark` and `ColorNames` (red, blue, green, yellow, magenta; they name the sprite files). |
| `StringGeometry.cs` | Samples a string as a quadratic Bezier curve (24 points) and tests whether two strings cross, segment by segment. |
| `PuzzleModel.cs` | **Pure rules with no Unity visuals**: `IsUntangled`, `CanPlace`, `Free`, `AddSlot`, `Won`, slot and chhat bookkeeping (`KiteChhat`, `KiteDock`, `Slots`). `Solve()` is a DFS whose visited set is keyed on `FreedMask`, because the state depends only on which kites are freed. It's used by the generator and by Hint. |
| `LevelGenerator.cs` | Builds level N from a fixed seed (the same for every player). It rejects a layout if it can't be solved, if "obvious play" wins (level 3+), or if random legal play wins more often than the level's allowed rate (`maxRandomWin`). If no layout works, it adds a slot. The difficulty table is in `For(level)`. |
| `LevelCache.cs` | Serves the **baked** levels (`Resources/Levels/levels.json`, levels 1–200) at once; later levels are generated on a worker thread (`Task.Run`) and the last 6 kept. `GameUI` requests the level it loads and prefetches the next one, so play doesn't freeze while a level is built. |
| `Kite.cs` | One kite: its sprite body (pivot = string point) and shadow, its string (3 `LineRenderer` passes from the mockup: cream edge, dark shade, base color, plus a soft shadow) and its charkhi sprite. Also `DockTo(transform)`, `Shake`, `Flash`, `HintPulse` (with the hint ring). |
| `ChhatView.cs` | A rooftop sprite with 3 dashed kite docks; it glides to wherever the board sends it (`MoveTo`/`Snap`). |
| `TangleBoard.cs` | The game board: layout (from the camera and the safe area), sky/roof background, level loading, slots (incl. the locked "+1 FREE" bonus slot), tap input (Input System `Pointer`, ignoring taps on uGUI buttons), `Sync()`, win/lose. Public state and actions for the UI (see below). |
| `Art.cs` | Loads sprites/fonts from Resources, the UI palette (`Ink`, `Cream`, `Saffron`…), `DesignPx` (390-px design → world units) and the `Icon` code points. |
| `Progress.cs` | PlayerPrefs: `level` (highest unlocked), `stars_N`, `coins`, `hints`, `sound`, `vibration`, `tut_N`, `sig`; `Changed` event. |
| `Sfx.cs` | Placeholder synthesized sounds + vibration. Swap in recorded clips later. |
| `Ads.cs` | Google AdMob (plugin 11.5.0 in `Assets/GoogleMobileAds`, gradle templates in `Plugins/Android`) with Google's **TEST IDs**. Few ads: rewarded only on request (`ShowRewarded`), an interstitial only via `AfterWin` (from level 10, every 4th win, 3+ min apart), no banners/app-open. UMP consent in `Init`; `PrivacyOptionsRequired`/`ShowPrivacyOptions` for Settings. Editor/desktop: rewarded succeeds at once. Real IDs: App ID in `Assets/GoogleMobileAds/Resources/GoogleMobileAdsSettings.asset`, unit IDs in `Ads.cs`. |
| `SpriteFactory.cs` | An anti-aliased circle sprite for UI badges. |
| `UI/UIKit.cs` | Helpers for building uGUI from code (`Node`, `Fill`, `At`, `Img`, `Label`, `Glyph`, `MakeButton`), plus `PressScale`, `Bob`, `SafeArea`. |
| `UI/GameUI.cs` | All screens on one Screen Space Overlay canvas: Home, HUD, Win, Lose, Pause, Settings, Shop, loading pill, toasts. Android back = Escape. |
| `Editor/SceneBuilder.cs` | Menu **Kite Tangle → Build Game Scene** creates `Assets/Scenes/Game.unity`. |
| `Editor/ArtImporter.cs` | Import rules for `Assets/Resources/Art` (sprite, 100 PPU, kite pivot on the string point, 9-slice borders for buttons/rooftops/slots, uncompressed half-size backgrounds). |
| `Editor/Builder.cs` | Android build from the command line (IL2CPP, ARM64, min API 24). |
| `SkyDecor.cs` | Sky life behind the strings: sun glow + drifting clouds (day), warm clouds (dusk), moon + twinkling stars (night). Sprites come from `SpriteFactory` (Glow, Cloud). |
| `UI/UiTour.cs` | Screenshot tour of every screen; only runs with `-uitour <folder>` on the desktop build (`Builder.BuildLinuxTour`, `Tools/ui_tour.sh`). |
| `Editor/LevelBaker.cs` | Menu **Kite Tangle → Bake Levels** / `-executeMethod LevelBaker.Run [-count 200]`: pre-builds levels into `Resources/Levels/levels.json` (in parallel, ~40 s). **Re-run after any change to the generator or rules**; LogicTests fails while it is stale and a `.aab` build refuses to run without it. |
| `Editor/AndroidManifestFix.cs` | Post-processes the Gradle project: `android:allowBackup="false"` (with `tools:replace`). |
| `Editor/LogicTests.cs` | Menu **Kite Tangle → Logic Tests** / `-executeMethod LogicTests.Run [-levels 40]`: rule checks on a hand-made level, then for every generated level: shape, solvable, the solution replays legally, deterministic (1–10). Must say `0 failed`. |
| `Editor/LevelReport.cs` | Menu **Kite Tangle → Level Report** logs levels 1–30: slots, crossings, random-play win %, whether it's solvable, and generation time. |

### Key rules for changing code
- **Keep `LevelGenerator`, `PuzzleModel` and `StringGeometry` free of Unity objects** (no `Debug`, `Resources`, `Time`...): they run on worker threads.
- **Keep game rules only in `PuzzleModel`.** The generator, the solver and the board all depend on it. Visual code must never decide rules.
- Level geometry is **normalized** and mapped to the screen only by linear scaling (`TangleBoard.ToWorld`), which keeps string crossings the same on every screen size. Don't add world-space offsets to string points (the string *shadow* is moved as a whole via its transform, which is fine).
- Strings are a dark outline in their own color (`PaletteDark` mixed with Ink), the cord (`Palette`) and a thin shine (`PaletteLight`). No light outline: it read as a white border on the dusk/night skies.
- A layer is "on top" only because of its `sortingOrder`: string outline = `layer*3`, cord = `layer*3+1`, shine = `layer*3+2`; string shadows -5; sky decor -99..-95; kite body in the sky = `200 + layer*2` (its shadow one below); charkhis 150; hint ring 250.
  Sky -100, roof band -10. Chhat rooftops 300, dock markers 302, slots 300, docked kites 400. Keep new world-space UI outside the 0–229 range used by strings and kites in the sky.
- A kite can have at most 32 bits in the masks, and the solver is fine up to about 15–16 kites. More kites need a different solver.

## UI
- Canvas: Screen Space Overlay, reference 1080x1920, **Expand** (the canvas is always at least 1080 wide and 1920 high; tall phones get extra height).
  Everything except full-bleed backgrounds and popups sits under a `SafeArea` rect. `TangleBoard.UiScale` is the same scale for world-space layout.
- Audio needs an `AudioListener`: the scene camera has one (SceneBuilder), and `Sfx` adds one if it's missing.
- Fonts: Baloo 2 (static ExtraBold/SemiBold instances cut from the variable font) and a **subset** of Material Symbols Rounded holding only the glyphs in `Icon`.
  To add an icon: find its code point in the Material Symbols `.codepoints` file, regenerate the subset with `pyftsubset` (fill=1, wght=600, opsz=48) and add it to `Icon`.
- `TangleBoard` hooks: `State` (Idle/Playing/Won/Lost), `Reason`, `LevelNumber`, `Hearts`/`MaxHearts`, `ChhatsLeft`, `Stars`, `CoinsEarned`, `UsedContinue`, `BonusSlotAvailable`, `InputBlocked`,
  `SlotsFree`, `Stuck` (a background solve after every move proved the level can't be won with the current slots), `HintActive`, `IdleTime`;
  events `Changed`, `BonusSlotTapped`; actions `LoadLevel(LevelData)` (get it from `LevelCache`), `Clear(skyLevel)`, `ShowHint()` (→ `Shown`/`Busy`/`NoMoves`; it uses the background solution when ready), `UnlockBonusSlot()`, `Continue()`.
- Layout: the top HUD reserves `HudHeightPx = 190` and the bottom roof band `RoofBandPx = 250` (both in canvas units, converted with `TangleBoard.UiScale`, plus safe-area insets).
  If either changes, the chhat row, slots and kite field move with it (`ComputeLayout`).
- Win: stars = hearts left (1 if a continue was used); coins = 10 + 5 × stars on the first win, and on a replay only 5 per star above the best (no farming). The reward pill hides when it's 0.
- HUD tip: warnings in red take priority (stuck, slots full, 1 slot left from level 4); levels 1–2 pulse a free hint after 5 s idle. Lose: continue once per attempt with a rewarded ad (+1 slot for "No Space!", full hearts for "Try Again").
- Sky: day, dusk and night take turns every 5 levels (`TangleBoard.SkyFor`). The camera color matches each sky's edge, and the sky/roof re-fit whenever the screen size changes.
- "How to play" cards: once before levels 1–3 (`Progress.TutorialSeen`), and all rules from the Pause help (i) button.
- Win screen: confetti (`Confetti` in UIKit), a star rating word (Perfect!/Great!/Well done!), coins counting up (`CountUp`, `Sfx.Sound.Coin`).
- Saves carry a checksum (`sig`) of level, coins and hints; `Progress.Validate()` at startup resets coins/hints if it doesn't match.
- App icon: `Tools/gen_icon.py` → `Assets/Store/icon.png` + adaptive layers (applied by `Builder`); `Store/play-icon-512.png` for the listing. All Play Store material lives in `Store/` (listing, checklist, privacy policy, app-ads.txt, screenshots; see `Store/README.md`). The Unity splash is off.
- Wording and theme: "Bo Kata!" (win), chhat = rooftop, charkhi = spool, patang = kite, dor = kite string.

## Build and run
- Editor: open the project, open `Assets/Scenes/Game.unity`, set the Game view to portrait (for example 1080x2340), and press Play.
- To reset progress: `PlayerPrefs.DeleteKey("level")`, or clear the app's data on the phone.
- Build an APK:
  ```
  ~/Unity/Hub/Editor/6000.0.84f1/Editor/Unity -batchmode -nographics -quit -projectPath . \
    -buildTarget Android -executeMethod Builder.PerformBuild -buildOutput Builds/KiteTangle.apk -logFile build.log
  adb install -r Builds/KiteTangle.apk
  ```
- Note: a Unity build restarts adb, which often drops **wireless debugging**. Turn it on again on the phone after each build, or use USB.
- Level report: `-executeMethod LevelReport.Run` (see its log lines tagged `[LevelReport]`).
- Logic tests (run after changing rules or the generator): `-executeMethod LogicTests.Run -levels 40 -logFile tmp/logictests.log`, then `grep "\[LogicTests\]"`.
- Version: `-versionCode N -versionName 1.0`. A `.aab` output is a Play release: it needs `Keys/keystore.properties` (storeFile, storePassword, keyAlias, keyPassword; `Keys/` is git-ignored) or the build fails. An `.apk` without it is debug-signed.

## Roadmap
1. ~~Stage 1: core tap and the "on top" rule~~ (done)
2. ~~Stage 2: chhat queue, slots, generated levels, solver, hint, difficulty filter~~ (done)
3. ~~Real UI and art: HUD, menus, kite/chhat/charkhi sprites, sky~~ (done). Still to do: recorded sounds ("Bo Kata!" shout, wind), music, kite skins
4. Boosters: Undo, Cut Any, Freeze; AdMob is in with test IDs (real IDs before release)
5. New mechanics: double-string patang, wind (swaps which string is on top), ice string (2 taps), hidden-color kite, and later **dragging a charkhi to an empty peg** (the moved string goes on top)
6. Pre-generate levels in the editor (some levels take about 1.4 s to generate on a PC and 3–4 s on a phone)
