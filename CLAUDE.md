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
All objects are created from code at runtime. The scene holds only a Camera and a `TangleBoard` object, and there are no prefabs yet.

| File | Role |
|---|---|
| `LevelDefs.cs` | Data types: `KiteDef` (colorId, layer, spool, anchor, bend in **normalized** 0..1 coordinates), `LevelData` (kites, `blockerMask[]`, chhat `queue[]`, `slots`), and the color `Palette`. |
| `StringGeometry.cs` | Samples a string as a quadratic Bezier curve (24 points) and tests whether two strings cross, segment by segment. |
| `PuzzleModel.cs` | **Pure rules with no Unity visuals**: `IsUntangled`, `CanPlace`, `Free`, `Won`, slot and chhat bookkeeping (`KiteChhat`, `KiteDock`, `Slots`). `Solve()` is a DFS whose visited set is keyed on `FreedMask`, because the state depends only on which kites are freed. It's used by the generator and by Hint. |
| `LevelGenerator.cs` | Builds level N from a fixed seed (the same for every player). It rejects a layout if it can't be solved, if "obvious play" wins (level 3+), or if random legal play wins more often than the level's allowed rate (`maxRandomWin`). If no layout works, it adds a slot. The difficulty table is in `For(level)`. |
| `Kite.cs` | One kite: its body (diamond sprite + collider), its string (a `LineRenderer` plus a dark outline), and its charkhi. Also `DockTo(transform)` (cuts the string and glides to a chhat or slot), `Shake`, `Flash`, `HintPulse`. |
| `ChhatView.cs` | A colored rooftop with 3 dock transforms; it glides to wherever the board sends it (`MoveTo`/`Snap`). |
| `TangleBoard.cs` | The game screen: layout (computed from the camera), level loading, tap input (Input System `Pointer`), `Sync()` (moves the visuals to match the model), win/lose state, and a **temporary `OnGUI` HUD**. |
| `SpriteFactory.cs` | Placeholder sprites generated at runtime (diamond, circle, pixel). **Replace them with real art.** |
| `Editor/SceneBuilder.cs` | Menu **Kite Tangle → Build Game Scene** creates `Assets/Scenes/Game.unity`. |
| `Editor/Builder.cs` | Android build from the command line (IL2CPP, ARM64, min API 24). |
| `Editor/LevelReport.cs` | Menu **Kite Tangle → Level Report** logs levels 1–30: slots, crossings, random-play win %, whether it's solvable, and generation time. |

### Key rules for changing code
- **Keep game rules only in `PuzzleModel`.** The generator, the solver and the board all depend on it. Visual code must never decide rules.
- Level geometry is **normalized** and mapped to the screen only by linear scaling (`TangleBoard.ToWorld`), which keeps string crossings the same on every screen size. Don't add world-space offsets to string points.
- A layer is "on top" only because of its `sortingOrder`: string = `layer*3+1`, outline = `layer*3`, kite body in the sky = `200 + layer*2`.
  Chhat sprites are 300–302, slots 300, docked kites 400. Keep new world-space UI outside the 0–229 range used by strings and kites in the sky.
- A kite can have at most 32 bits in the masks, and the solver is fine up to about 15–16 kites. More kites need a different solver.

## UI work (next task: replace the placeholder HUD)
The current HUD is `TangleBoard.OnGUI()`, a quick placeholder. Replace it with uGUI or TextMeshPro (a Canvas set to Screen Space Overlay, reference 1080x1920, match height).
Hooks the new UI needs from `TangleBoard` (currently private, so make them public or expose events):
- `level.number`, `hearts` / `MaxHearts`, `level.queue.Length - model.QueueIndex` (chhats left), `state` (Playing/Won/Lost), `loseReason`
- Actions: `ShowHint()`, `LoadLevel(level.number)` (restart/retry), `LoadLevel(level.number + 1)` (next)
- Layout: the top HUD reserves `HudHeightPx = 190` (in 1920-high reference pixels), and the Hint/Restart buttons sit on the brown roof band at the bottom.
  If the HUD changes height, update `HudHeightPx`, because the chhat row, slots and kite field are laid out below it (`ComputeLayout`).
- Screens still to design: Home, Level map, Win (stars?), Lose (continue with an ad / +1 slot), Settings, Shop (kite skins, boosters).
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

## Roadmap
1. ~~Stage 1: core tap and the "on top" rule~~ (done)
2. ~~Stage 2: chhat queue, slots, generated levels, solver, hint, difficulty filter~~ (done)
3. Real UI and art (in progress by the owner): HUD, menus, kite/chhat/charkhi sprites, sky, sounds ("Bo Kata!" shout, wind)
4. Boosters: +Slot, Undo, Cut Any, Freeze; coins; rewarded ads
5. New mechanics: double-string patang, wind (swaps which string is on top), ice string (2 taps), hidden-color kite, and later **dragging a charkhi to an empty peg** (the moved string goes on top)
6. Pre-generate levels in the editor (some levels take about 1.4 s to generate on a PC and 3–4 s on a phone)
