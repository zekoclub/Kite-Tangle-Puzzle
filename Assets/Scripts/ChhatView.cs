using UnityEngine;

// A colored rooftop with ChhatSize dock spots for kites. Glides to wherever the board sends it.
public class ChhatView : MonoBehaviour
{
    public Transform[] Docks { get; private set; }

    Vector3 targetPos;
    float targetScale = 1f;
    float delay;

    public void Init(Color color)
    {
        AddRect("Wall", new Vector2(0f, 0f), new Vector2(1.7f, 0.55f), Color.Lerp(color, Color.white, 0.55f), 300);
        AddRect("Roof", new Vector2(0f, 0.3f), new Vector2(1.9f, 0.16f), color, 301);
        AddRect("Door", new Vector2(0f, -0.08f), new Vector2(0.28f, 0.4f), Color.Lerp(color, Color.black, 0.35f), 301);

        Docks = new Transform[PuzzleModel.ChhatSize];
        for (int i = 0; i < Docks.Length; i++)
        {
            var dock = new GameObject($"Dock_{i}").transform;
            dock.SetParent(transform, false);
            dock.localPosition = new Vector3((i - 1) * 0.55f, 0.66f, 0f);
            Docks[i] = dock;

            var marker = new GameObject("Marker").AddComponent<SpriteRenderer>();
            marker.transform.SetParent(dock, false);
            marker.transform.localScale = Vector3.one * 0.36f;
            marker.sprite = SpriteFactory.Circle;
            marker.color = new Color(color.r, color.g, color.b, 0.35f);
            marker.sortingOrder = 302;
        }
    }

    void AddRect(string name, Vector2 pos, Vector2 size, Color color, int order)
    {
        var sr = new GameObject(name).AddComponent<SpriteRenderer>();
        sr.transform.SetParent(transform, false);
        sr.transform.localPosition = pos;
        sr.transform.localScale = new Vector3(size.x, size.y, 1f);
        sr.sprite = SpriteFactory.Pixel;
        sr.color = color;
        sr.sortingOrder = order;
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
