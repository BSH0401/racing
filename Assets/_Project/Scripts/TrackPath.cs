using System.Collections.Generic;
using UnityEngine;

namespace Racing
{
    // Closed centripetal Catmull-Rom loop through the control points, resampled at even spacing.
    // Points are in world space (the track object sits at the origin).
    [ExecuteAlways]
    public class TrackPath : MonoBehaviour
    {
        public Vector3[] controlPoints = new Vector3[0];
        [Tooltip("Optional road half width per control point (falls back to roadHalfWidth).")]
        public float[] controlHalfWidths = new float[0];
        public float spacing = 2f;
        public float roadHalfWidth = 7f;
        public float startDistance = 60f;
        [Tooltip("Drape the resampled path onto the road surface nearest its own height (WorldLayout).")]
        public bool followTerrain;

        Vector3[] points, tangents, rights;
        float[] halfWidths;
        float realSpacing, length;
        int startIndex;
        bool built;

        public int Count { get { EnsureBuilt(); return points.Length; } }
        public float Spacing { get { EnsureBuilt(); return realSpacing; } }
        public float Length { get { EnsureBuilt(); return length; } }
        public int StartIndex { get { EnsureBuilt(); return startIndex; } }

        void OnEnable() => built = false;
        void OnValidate() => built = false;

        public void EnsureBuilt()
        {
            if (!built || points == null) Rebuild();
        }

        public void Rebuild()
        {
            int n = controlPoints.Length;
            var dense = new List<Vector3>();
            const int sub = 64;
            for (int s = 0; s < n; s++)
            {
                Vector3 p0 = Ctrl(s - 1), p1 = Ctrl(s), p2 = Ctrl(s + 1), p3 = Ctrl(s + 2);
                for (int k = 0; k < sub; k++) dense.Add(CatmullRom(p0, p1, p2, p3, k / (float)sub));
            }

            int d = dense.Count;
            var cum = new float[d + 1];
            for (int i = 0; i < d; i++) cum[i + 1] = cum[i] + Vector3.Distance(dense[i], dense[(i + 1) % d]);
            length = cum[d];

            int count = Mathf.Max(8, Mathf.RoundToInt(length / spacing));
            realSpacing = length / count;
            points = new Vector3[count];
            tangents = new Vector3[count];
            rights = new Vector3[count];

            halfWidths = new float[count];
            bool widths = controlHalfWidths != null && controlHalfWidths.Length == n;
            int j = 0;
            for (int i = 0; i < count; i++)
            {
                float target = i * realSpacing;
                while (j < d - 1 && cum[j + 1] < target) j++;
                float seg = cum[j + 1] - cum[j];
                float t = seg > 1e-5f ? (target - cum[j]) / seg : 0f;
                points[i] = Vector3.Lerp(dense[j], dense[(j + 1) % d], t);
                if (followTerrain) points[i].y = WorldLayout.SurfaceHeight(points[i].x, points[i].z, points[i].y);
                int ctrl = j / sub;
                halfWidths[i] = widths ? Mathf.Min(controlHalfWidths[ctrl % n], controlHalfWidths[(ctrl + 1) % n]) : roadHalfWidth;
            }

            for (int i = 0; i < count; i++)
            {
                Vector3 tg = points[(i + 1) % count] - points[(i - 1 + count) % count];
                tangents[i] = tg.normalized;
                Vector3 flat = new Vector3(tg.x, 0f, tg.z).normalized;
                rights[i] = new Vector3(flat.z, 0f, -flat.x);
            }

            startIndex = Mathf.RoundToInt(startDistance / realSpacing) % count;
            built = true;
        }

        Vector3 Ctrl(int i)
        {
            int n = controlPoints.Length;
            return controlPoints[((i % n) + n) % n];
        }

        static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t0 = 0f;
            float t1 = t0 + Mathf.Max(Mathf.Sqrt(Vector3.Distance(p0, p1)), 1e-4f);
            float t2 = t1 + Mathf.Max(Mathf.Sqrt(Vector3.Distance(p1, p2)), 1e-4f);
            float t3 = t2 + Mathf.Max(Mathf.Sqrt(Vector3.Distance(p2, p3)), 1e-4f);
            float u = Mathf.Lerp(t1, t2, t);
            Vector3 a1 = (t1 - u) / (t1 - t0) * p0 + (u - t0) / (t1 - t0) * p1;
            Vector3 a2 = (t2 - u) / (t2 - t1) * p1 + (u - t1) / (t2 - t1) * p2;
            Vector3 a3 = (t3 - u) / (t3 - t2) * p2 + (u - t2) / (t3 - t2) * p3;
            Vector3 b1 = (t2 - u) / (t2 - t0) * a1 + (u - t0) / (t2 - t0) * a2;
            Vector3 b2 = (t3 - u) / (t3 - t1) * a2 + (u - t1) / (t3 - t1) * a3;
            return (t2 - u) / (t2 - t1) * b1 + (u - t1) / (t2 - t1) * b2;
        }

        public int Wrap(int i)
        {
            int c = Count;
            return ((i % c) + c) % c;
        }

        public Vector3 Point(int i) { EnsureBuilt(); return points[Wrap(i)]; }
        public Vector3 Tangent(int i) { EnsureBuilt(); return tangents[Wrap(i)]; }
        public Vector3 Right(int i) { EnsureBuilt(); return rights[Wrap(i)]; }
        public float HalfWidth(int i) { EnsureBuilt(); return halfWidths[Wrap(i)]; }

        public Vector3 FlatTangent(int i)
        {
            Vector3 t = Tangent(i);
            t.y = 0f;
            return t.normalized;
        }

        // Index relative to the start/finish line (0 = on the line).
        public int Rel(int i) => Wrap(i - StartIndex);

        public int IndexAhead(int i, float meters) => Wrap(i + Mathf.RoundToInt(meters / Spacing));

        // Closest sample to pos; searches a window around hint, or the whole loop when hint < 0.
        public int FindClosest(Vector3 pos, int hint = -1, int back = 15, int forward = 30)
        {
            EnsureBuilt();
            int best = 0;
            float bestD = float.MaxValue;
            if (hint < 0)
            {
                for (int i = 0; i < points.Length; i++) Check(i);
            }
            else
            {
                for (int k = -back; k <= forward; k++) Check(Wrap(hint + k));
            }
            return best;

            void Check(int i)
            {
                float dd = (points[i] - pos).sqrMagnitude;
                if (dd < bestD) { bestD = dd; best = i; }
            }
        }

        public float LateralOffset(Vector3 pos, int i) => Vector3.Dot(pos - Point(i), Right(i));

        // Heading change (degrees) between two points ahead of index i.
        public float TurnAngle(int i, float fromMeters, float toMeters)
        {
            return Vector3.Angle(FlatTangent(IndexAhead(i, fromMeters)), FlatTangent(IndexAhead(i, toMeters)));
        }

        public Bounds GetBounds()
        {
            EnsureBuilt();
            var b = new Bounds(points[0], Vector3.zero);
            foreach (var p in points) b.Encapsulate(p);
            return b;
        }

        public float MinHorizontalDistance(Vector3 pos)
        {
            EnsureBuilt();
            float best = float.MaxValue;
            foreach (var p in points)
            {
                float dx = p.x - pos.x, dz = p.z - pos.z;
                float dd = dx * dx + dz * dz;
                if (dd < best) best = dd;
            }
            return Mathf.Sqrt(best);
        }
    }
}
