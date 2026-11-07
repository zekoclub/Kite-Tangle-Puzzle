using UnityEngine;

// A colored rooftop with ChhatSize dock spots for kites. Glides to wherever the board sends it.
public class ChhatView : MonoBehaviour
{
    public const float Width = 2.0f;   // world width at scale 1
    public const float DockGap = 0.6f; // between dock centers

    public Transform[] Docks { get; private set; }

    Vector3 targetPos;
    float targetScale = 1f;
    float delay;

    public void Init(int colorId)
    {
        var roof = new GameObject("Rooftop").AddComponent<SpriteRenderer>();
        roof.transform.SetParent(transform, false);
        roof.sprite = Art.RooftopSprite(colorId);
        roof.transform.localScale = Vector3.one * Art.ScaleForWidth(roof.sprite, Width);
        roof.sortingOrder = 300;

        // Kite sprites pivot on their string point, so drop each dock below the middle to center the kite.
        var empty = Art.Load("kites/kite_dock_empty");
        float emptyScale = Art.ScaleForWidth(empty, Kite.DockWidth);
        float dockY = -empty.bounds.size.y * emptyScale * 0.5f + (-empty.bounds.min.y) * emptyScale;

        Docks = new Transform[PuzzleModel.ChhatSize];
        for (int i = 0; i < Docks.Length; i++)
        {
            var dock = new GameObject($"Dock_{i}").transform;
            dock.SetParent(transform, false);
            dock.localPosition = new Vector3((i - (Docks.Length - 1) * 0.5f) * DockGap, dockY, 0f);
            Docks[i] = dock;

            var marker = new GameObject("Marker").AddComponent<SpriteRenderer>();
            marker.transform.SetParent(dock, false);
            marker.transform.localScale = Vector3.one * emptyScale;
            marker.sprite = empty;
            marker.sortingOrder = 302;
        }
    }

    public void Snap(Vector3 pos, float scale)
    {
        targetPos = pos;
        targetScale = scale;
        transform.position = pos;
        transform.localScale = Vector3.one * scale;
    }

    public void MoveTo(Vector3 pos, float scale, float afterDelay = 0f)
    {
        if (pos == targetPos && Mathf.Approximately(scale, targetScale)) return;
        targetPos = pos;
        targetScale = scale;
        delay = afterDelay;
    }

    void Update()
    {
        if (delay > 0f) { delay -= Time.deltaTime; return; }
        float t = 1f - Mathf.Exp(-7f * Time.deltaTime);
        transform.position = Vector3.Lerp(transform.position, targetPos, t);
        transform.localScale = Vector3.Lerp(transform.localScale, Vector3.one * targetScale, t);
    }
}
