using UnityEngine;

// Kite string curves and crossing tests.
public static class StringGeometry
{
    public const int Samples = 24;

    // Quadratic Bezier from the charkhi to the kite, bent sideways by `bend`.
    public static Vector2[] Sample(Vector2 spool, Vector2 anchor, Vector2 bend)
    {
        Vector2 ctrl = (spool + anchor) * 0.5f + bend;
        var points = new Vector2[Samples];
        for (int i = 0; i < Samples; i++)
        {
            float t = i / (Samples - 1f);
            points[i] = (1 - t) * (1 - t) * spool + 2 * (1 - t) * t * ctrl + t * t * anchor;
        }
        return points;
    }

    public static bool Cross(Vector2[] a, Vector2[] b)
    {
        for (int i = 0; i < a.Length - 1; i++)
        for (int j = 0; j < b.Length - 1; j++)
            if (SegmentsIntersect(a[i], a[i + 1], b[j], b[j + 1]))
                return true;
        return false;
    }

    static bool SegmentsIntersect(Vector2 p1, Vector2 p2, Vector2 q1, Vector2 q2)
    {
        float d1 = CrossZ(q2 - q1, p1 - q1), d2 = CrossZ(q2 - q1, p2 - q1);
        float d3 = CrossZ(p2 - p1, q1 - p1), d4 = CrossZ(p2 - p1, q2 - p1);
        return d1 * d2 < 0f && d3 * d4 < 0f;
    }

    static float CrossZ(Vector2 u, Vector2 v) => u.x * v.y - u.y * v.x;
}
