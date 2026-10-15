using System.Numerics;

namespace StarBlaster;

/// <summary>A smooth Catmull-Rom curve through control points, sampled by distance travelled.</summary>
public sealed class PathCurve
{
    private readonly Vector2[] _pts;
    private readonly float[] _cum;

    public float Length => _cum[^1];

    public PathCurve(IReadOnlyList<Vector2> control, float step = 3f)
    {
        int n = control.Count;
        Vector2 P(int i) => control[Math.Clamp(i, 0, n - 1)];

        var list = new List<Vector2>();
        for (int i = 0; i < n - 1; i++)
        {
            Vector2 p0 = P(i - 1), p1 = P(i), p2 = P(i + 1), p3 = P(i + 2);
            int steps = Math.Max(2, (int)(Vector2.Distance(p1, p2) / step));
            for (int s = 0; s < steps; s++)
                list.Add(CatmullRom(p0, p1, p2, p3, s / (float)steps));
        }
        list.Add(control[n - 1]);

        _pts = list.ToArray();
        _cum = new float[_pts.Length];
        for (int i = 1; i < _pts.Length; i++)
            _cum[i] = _cum[i - 1] + Vector2.Distance(_pts[i - 1], _pts[i]);
    }

    private static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
    {
        float t2 = t * t, t3 = t2 * t;
        return 0.5f * ((2f * p1) + (p2 - p0) * t
            + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2
            + (3f * p1 - p0 - 3f * p2 + p3) * t3);
    }

    /// <summary>Position and unit heading after travelling <paramref name="dist"/> along the curve.</summary>
    public Vector2 At(float dist, out Vector2 dir)
    {
        if (dist <= 0f) dist = 0f;
        if (dist >= Length) dist = Length;

        int idx = Array.BinarySearch(_cum, dist);
        if (idx < 0) idx = ~idx;
        idx = Math.Clamp(idx, 1, _pts.Length - 1);

        Vector2 a = _pts[idx - 1], b = _pts[idx];
        float seg = _cum[idx] - _cum[idx - 1];
        float t = seg > 0.0001f ? (dist - _cum[idx - 1]) / seg : 0f;
        dir = Vector2.Normalize(b - a);
        if (float.IsNaN(dir.X)) dir = new Vector2(0, 1);
        return Vector2.Lerp(a, b, t);
    }
}
