using System.Collections.Generic;
using UnityEngine;

// The game screen: builds a level, turns taps into PuzzleModel moves and keeps the visuals in sync.
public class TangleBoard : MonoBehaviour
{
    const int MaxHearts = 3;
    const float MinVisibleWidth = 6.2f; // world units that must always fit horizontally
    const float HudHeightPx = 190f;     // top HUD height in the 1080x1920 GUI reference
    const float RefHeight = 1920f;
    const string LevelKey = "level";

    enum State { Playing, Won, Lost }

    readonly List<Kite> kites = new();
    readonly List<ChhatView> chhats = new();
    readonly List<SpriteRenderer> slots = new();
    Transform levelRoot;
    Camera cam;

    LevelDefs.LevelData level;
    PuzzleModel model;
    int hearts;
    State state;
    string loseReason;
    float overlayAt;

    // Layout (world units), computed from the camera.
    float halfW, chhatY, slotsY, fieldTop, spoolY, fieldHalfW;

    void Start()
    {
        cam = Camera.main;
        cam.orthographicSize = Mathf.Max(5f, MinVisibleWidth * 0.5f / cam.aspect);
        ComputeLayout();
        BuildRoof();
        LoadLevel(Mathf.Max(1, PlayerPrefs.GetInt(LevelKey, 1)));
    }

    void ComputeLayout()
    {
        float s = cam.orthographicSize;
        halfW = s * cam.aspect;
        float hud = 2f * s * HudHeightPx / RefHeight;
        chhatY = s - hud - 0.75f;
        slotsY = chhatY - 1.35f;
        fieldTop = slotsY - 1.1f;      // highest kite anchor
        spoolY = -s + 1.5f;
        fieldHalfW = Mathf.Min(halfW, 3.1f) - 0.35f;
    }

    Vector2 ToWorld(Vector2 n) => new Vector2(
        Mathf.Lerp(-fieldHalfW, fieldHalfW, n.x),
        Mathf.Lerp(spoolY, fieldTop, n.y));

    void BuildRoof()
    {
        float s = cam.orthographicSize;
        float top = spoolY - 0.25f;
        var roof = new GameObject("Roof").AddComponent<SpriteRenderer>();
        roof.transform.SetParent(transform, false);
        roof.transform.position = new Vector3(0f, (top - s) * 0.5f, 0f);
        roof.transform.localScale = new Vector3(halfW * 2f + 1f, top + s, 1f);
        roof.sprite = SpriteFactory.Pixel;
        roof.color = new Color(0.55f, 0.33f, 0.22f);
        roof.sortingOrder = -10;
    }

    void LoadLevel(int number)
    {
        if (levelRoot != null) Destroy(levelRoot.gameObject);
        levelRoot = new GameObject("Level").transform;
        levelRoot.SetParent(transform, false);
        kites.Clear();
        chhats.Clear();
        slots.Clear();

        level = LevelGenerator.Generate(number);
        model = new PuzzleModel(level);

        for (int i = 0; i < level.kites.Length; i++)
        {
            var kite = new GameObject($"Kite_{i}").AddComponent<Kite>();
            kite.transform.SetParent(levelRoot, false);
            kite.Init(i, level.kites[i], ToWorld);
            kites.Add(kite);
        }

        foreach (int colorId in level.queue)
        {
            var view = new GameObject("Chhat").AddComponent<ChhatView>();
            view.transform.SetParent(levelRoot, false);
            view.Init(LevelDefs.Palette[colorId]);
            chhats.Add(view);
        }

        const float slotGap = 0.9f;
        for (int i = 0; i < level.slots; i++)
        {
            var slot = new GameObject($"Slot_{i}").AddComponent<SpriteRenderer>();
            slot.transform.SetParent(levelRoot, false);
            slot.transform.position = new Vector3((i - (level.slots - 1) * 0.5f) * slotGap, slotsY, 0f);
            slot.transform.localScale = Vector3.one * 0.72f;
            slot.sprite = SpriteFactory.Pixel;
            slot.color = new Color(1f, 1f, 1f, 0.45f);
            slot.sortingOrder = 300;
            slots.Add(slot);
        }

        hearts = MaxHearts;
        state = State.Playing;
        Sync(snap: true);
    }

    // Put every chhat and freed kite where the model says it belongs.
    void Sync(bool snap = false)
    {
        float activeX = -halfW + 1.3f;
        float nextX = activeX + 2.0f;
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
                pos = new Vector3(nextX + (d - 1) * 1.2f, chhatY - 0.1f, 0f);
                if (pos.x + 0.6f > halfW) pos.x = halfW + 2.5f; // wait off-screen
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
                kites[k].DockTo(slots[model.Slots.IndexOf(k)].transform);
        }
    }

    void Update()
    {
        if (state != State.Playing || !TryGetTap(out Vector2 screenPos)) return;

        Kite tapped = KiteAt(cam.ScreenToWorldPoint(screenPos));
        if (tapped == null) return;
        int k = tapped.Id;

        if (!model.IsUntangled(k))
        {
            tapped.StartCoroutine(tapped.Shake());
            for (int j = 0; j < kites.Count; j++)
                if ((level.blockerMask[k] & (1 << j)) != 0 && !model.IsFreed(j))
                    kites[j].Flash();
            hearts--;
            if (hearts <= 0) Lose("Try Again");
            return;
        }

        if (!model.CanPlace(k))
        {
            tapped.StartCoroutine(tapped.Shake());
            foreach (var slot in slots) slot.color = new Color(1f, 0.3f, 0.3f, 0.7f);
            Lose("No Space!");
            return;
        }

        model.Free(k);
        Sync();
        if (model.Won)
        {
            state = State.Won;
            overlayAt = Time.time + 1.0f;
            PlayerPrefs.SetInt(LevelKey, level.number + 1);
            PlayerPrefs.Save();
        }
    }

    void Lose(string reason)
    {
        state = State.Lost;
        loseReason = reason;
        overlayAt = Time.time + 0.5f;
    }

    void ShowHint()
    {
        var path = PuzzleModel.Solve(model.Clone());
        if (path == null || path.Count == 0) return;
        var kite = kites[path[0]];
        kite.StartCoroutine(kite.HintPulse());
    }

    // If kite bodies overlap, pick the one drawn on top.
    Kite KiteAt(Vector2 worldPos)
    {
        Kite best = null;
        foreach (var hit in Physics2D.OverlapPointAll(worldPos))
        {
            var kite = hit.GetComponentInParent<Kite>();
            if (kite != null && !model.IsFreed(kite.Id) && (best == null || kite.Layer > best.Layer))
                best = kite;
        }
        return best;
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

    void OnGUI()
    {
        if (level == null) return;

        // Scale the HUD to a 1920-high reference so it reads the same on phones and in the editor.
        float scale = Screen.height / RefHeight;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
        float w = Screen.width / scale;
        var ink = new Color(0.1f, 0.15f, 0.3f);

        var label = new GUIStyle(GUI.skin.label) { fontSize = 52, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
        label.normal.textColor = ink;
        GUI.Label(new Rect(50, 30, w * 0.5f, 80), $"Level {level.number}", label);
        label.alignment = TextAnchor.MiddleRight;
        GUI.Label(new Rect(w * 0.5f - 50, 30, w * 0.5f, 80), $"Hearts {hearts}/{MaxHearts}", label);

        var small = new GUIStyle(label) { fontSize = 32, fontStyle = FontStyle.Normal, alignment = TextAnchor.MiddleCenter };
        small.normal.textColor = new Color(ink.r, ink.g, ink.b, 0.75f);
        string tip = level.number == 1
            ? "Free the kite on top. It flies to its color's chhat."
            : $"Chhats left: {level.queue.Length - model.QueueIndex}";
        GUI.Label(new Rect(0, 115, w, 60), tip, small);

        var button = new GUIStyle(GUI.skin.button) { fontSize = 46 };
        if (state == State.Playing)
        {
            if (GUI.Button(new Rect(w * 0.5f - 330, RefHeight - 150, 300, 110), "Hint", button)) ShowHint();
            if (GUI.Button(new Rect(w * 0.5f + 30, RefHeight - 150, 300, 110), "Restart", button)) LoadLevel(level.number);
            return;
        }
        if (Time.time < overlayAt) return;

        GUI.color = new Color(1f, 1f, 1f, 0.85f);
        GUI.DrawTexture(new Rect(0, 560, w, 460), Texture2D.whiteTexture);
        GUI.color = Color.white;

        var title = new GUIStyle(label) { fontSize = 110, alignment = TextAnchor.MiddleCenter };
        title.normal.textColor = state == State.Won ? new Color(0.95f, 0.45f, 0.05f) : new Color(0.75f, 0.1f, 0.1f);
        GUI.Label(new Rect(0, 600, w, 160), state == State.Won ? "Bo Kata!" : loseReason, title);

        var big = new GUIStyle(button) { fontSize = 56 };
        if (state == State.Won)
        {
            if (GUI.Button(new Rect(w * 0.5f - 250, 800, 500, 150), "Next Level", big)) LoadLevel(level.number + 1);
        }
        else if (GUI.Button(new Rect(w * 0.5f - 250, 800, 500, 150), "Retry", big)) LoadLevel(level.number);
    }
}
