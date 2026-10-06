using System.Collections.Generic;
using UnityEngine;

namespace Racing
{
    // The open world around the city: rolling countryside, a highway ring (3 lanes each way with a
    // barrier in the middle) and four winding two-lane national roads joining the city exits to it.
    // Roads carry their own smoothed, grade-limited height profile; the terrain is pulled onto them,
    // which gives natural cuttings and embankments. Inside the city square CityLayout rules unchanged.
    public static class WorldLayout
    {
        public const float Min = -1500f, Max = 2268f; // aligned with the city edges on an 8 m grid
        public static readonly Vector2 Centre = new Vector2(384f, 384f);

        // Highway cross-section (half widths from the centre line).
        public const float MedianHalf = 2.5f, LaneWidth = 3.6f, HighwayLanes = 3, ShoulderWidth = 2.5f;
        public static float CarriageCentre => MedianHalf + LaneWidth * HighwayLanes * 0.5f;     // 7.9
        public static float HighwayHalf => MedianHalf + LaneWidth * HighwayLanes + ShoulderWidth; // 15.8
        public const float NationalHalf = 5f; // two 3.5 m lanes plus 1.5 m shoulders
        public const float Verge = 12f;

        public class Road
        {
            public string name;
            public bool highway, closed;
            public int index;
            public float halfWidth;
            public Vector3[] pts;   // centre line with profile height, ~4 m apart
            public Vector3[] right; // flat right-hand normals
            public float[] dist;
            public float Length => dist[dist.Length - 1];

            public int Count => pts.Length;
            public int Wrap(int i) => closed ? ((i % Count) + Count) % Count : Mathf.Clamp(i, 0, Count - 1);
        }

        // A city exit: the street that continues out of the city and the national road starting at it.
        public struct Exit
        {
            public Vector2 cityEnd;   // last intersection inside the street grid
            public Vector2 edge;      // where the street leaves the city square
            public Vector2 dir;       // outward
            public float ringAngle;   // where its national road meets the highway (degrees)
        }

        public static readonly Exit[] Exits =
        {
            new Exit { cityEnd = new Vector2(768f, 384f), edge = new Vector2(CityLayout.Max, 384f), dir = Vector2.right, ringAngle = 2f },
            new Exit { cityEnd = new Vector2(384f, 768f), edge = new Vector2(384f, CityLayout.Max), dir = Vector2.up, ringAngle = 92f },
            new Exit { cityEnd = new Vector2(96f, 0f), edge = new Vector2(96f, CityLayout.Min), dir = Vector2.down, ringAngle = 248f },
            new Exit { cityEnd = new Vector2(0f, 576f), edge = new Vector2(CityLayout.Min, 576f), dir = Vector2.left, ringAngle = 172f },
        };

        public static Road Highway { get; private set; }
        public static readonly List<Road> National = new List<Road>();
        public static readonly List<Road> Roads = new List<Road>();
        // Highway sample index where each national road joins (same order as Exits).
        public static readonly List<int> Junctions = new List<int>();

        static readonly Dictionary<long, List<(Road road, int i)>> hash = new Dictionary<long, List<(Road, int)>>();
        const float HashCell = 48f;

        static WorldLayout() => Build();

        public static bool InBounds(Vector3 p, float inset = 0f) =>
            p.x > Min + inset && p.x < Max - inset && p.z > Min + inset && p.z < Max - inset;

        static bool InCity(float x, float z) =>
            x >= CityLayout.Min && x <= CityLayout.Max && z >= CityLayout.Min && z <= CityLayout.Max;

        // ---- Heights ----

        // Final ground height: city rules inside the city, otherwise countryside pulled onto the roads.
        public static float Height(float x, float z)
        {
            if (InCity(x, z)) return CityLayout.Height(x, z);
            float nat = Natural(x, z);
            float bestW = 0f, target = nat;
            int n = Query(x, z, 45f, out var near);
            for (int k = 0; k < n; k++)
            {
                var road = near[k].road;
                float d = near[k].d, y = near[k].y;
                // Flat verge wider than a terrain cell's diagonal (8 m grid), so no ground triangle that
                // overlaps the paving can tilt up over it in a cutting.
                float core = road.halfWidth + Verge;
                float fall = road.highway ? 42f : 26f;
                float w = d <= core ? 1f : 1f - Smooth((d - core) / fall);
                if (w > bestW) { bestW = w; target = y - 0.06f; }
            }
            return Mathf.Lerp(nat, target, bestW);
        }

        // Countryside without roads: blends out of the city's hills into bigger rolling country,
        // rising towards a ring of high ground near the world edge.
        public static float Natural(float x, float z)
        {
            float city = CityLayout.Height(x, z);
            float dx = Mathf.Max(CityLayout.Min - x, 0f, x - CityLayout.Max);
            float dz = Mathf.Max(CityLayout.Min - z, 0f, z - CityLayout.Max);
            float w = Smooth(Mathf.Sqrt(dx * dx + dz * dz) / 380f);
            float country = 14f
                + 20f * Mathf.Sin(x / 430f + 0.7f) * Mathf.Cos(z / 520f - 0.3f)
                + 9f * Mathf.Sin((x + 1.7f * z) / 300f)
                + 4.5f * Mathf.Cos((x - z) / 160f)
                + 2.5f * Mathf.Sin(z / 90f) * Mathf.Cos(x / 110f);
            float rc = Vector2.Distance(new Vector2(x, z), Centre);
            float ang = Mathf.Atan2(z - Centre.y, x - Centre.x);
            country += Smooth((rc - 1500f) / 500f) * 45f * (0.6f + 0.4f * Mathf.Sin(ang * 5f + 1f));
            return Mathf.Lerp(city, country, w);
        }

        static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        // ---- Road queries ----

        public struct RoadHit
        {
            public Road road;
            public float d, y;
        }

        static readonly RoadHit[] hits = new RoadHit[8];
        static readonly int[] bestIdx = new int[8];
        static readonly float[] bestD2 = new float[8];

        // Every road within (halfWidth + extra) of (x, z): lateral distance from the centre line and
        // profile height. Results are valid until the next call (shared buffer, main thread only).
        public static int Query(float x, float z, float extra, out RoadHit[] result)
        {
            result = hits;
            int nr = Roads.Count;
            for (int r = 0; r < nr; r++) { bestIdx[r] = -1; bestD2[r] = float.MaxValue; }
            int cx = Mathf.FloorToInt(x / HashCell), cz = Mathf.FloorToInt(z / HashCell);
            for (int ix = -1; ix <= 1; ix++)
            for (int iz = -1; iz <= 1; iz++)
            {
                if (!hash.TryGetValue(Key(cx + ix, cz + iz), out var list)) continue;
                for (int k = 0; k < list.Count; k++)
                {
                    var (road, i) = list[k];
                    int r = road.index;
                    float ddx = road.pts[i].x - x, ddz = road.pts[i].z - z;
                    float dd = ddx * ddx + ddz * ddz;
                    if (dd < bestD2[r]) { bestD2[r] = dd; bestIdx[r] = i; }
                }
            }
            int count = 0;
            for (int r = 0; r < nr; r++)
            {
                var road = Roads[r];
                int bi = bestIdx[r];
                float lim = road.halfWidth + extra;
                if (bi < 0 || bestD2[r] > lim * lim) continue;
                float d = Mathf.Sqrt(bestD2[r]);
                float y = road.pts[bi].y;
                // Refine on the two segments around the nearest sample.
                for (int s = -1; s <= 0; s++)
                {
                    int a = road.Wrap(bi + s), b = road.Wrap(bi + s + 1);
                    if (a == b) continue;
                    float ax = road.pts[a].x, az = road.pts[a].z;
                    float abx = road.pts[b].x - ax, abz = road.pts[b].z - az;
                    float t = Mathf.Clamp01(((x - ax) * abx + (z - az) * abz) / Mathf.Max(abx * abx + abz * abz, 1e-4f));
                    float px = ax + abx * t - x, pz = az + abz * t - z;
                    float dist = Mathf.Sqrt(px * px + pz * pz);
                    if (dist < d) { d = dist; y = Mathf.Lerp(road.pts[a].y, road.pts[b].y, t); }
                }
                hits[count++] = new RoadHit { road = road, d = d, y = y };
            }
            return count;
        }

        // Distance from (x, z) to the edge of the nearest paved road (negative = on it).
        public static float RoadClearance(float x, float z)
        {
            float best = float.MaxValue;
            int n = Query(x, z, 60f, out var near);
            for (int k = 0; k < n; k++) best = Mathf.Min(best, near[k].d - near[k].road.halfWidth);
            return best;
        }

        static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

        // Dev check (-validateworld): ground poking through the paved surface of any road.
        public static void Validate()
        {
            foreach (var road in Roads)
            {
                float worst = 0f;
                Vector3 at = Vector3.zero;
                int bad = 0;
                for (int i = 0; i < road.Count; i++)
                for (float o = -road.halfWidth; o <= road.halfWidth; o += 2f)
                {
                    Vector3 p = road.pts[i] + road.right[i] * o;
                    float above = Height(p.x, p.z) - road.pts[i].y;
                    if (above > 0.02f) bad++;
                    if (above > worst) { worst = above; at = p; }
                }
                Debug.Log($"[World] {road.name} len={road.Length:F0} samples={road.Count} groundAbove>2cm={bad} worst={worst:F2} at {at:F0}");
            }
        }

        // ---- Construction ----

        static void Build()
        {
            Roads.Clear();
            National.Clear();
            Junctions.Clear();
            hash.Clear();

            // Highway: a rounded-square (superellipse) ring around the city with a gentle wobble.
            var ring = new List<Vector2>();
            for (int k = 0; k < 720; k++)
            {
                float th = k * Mathf.PI * 2f / 720f;
                float c = Mathf.Cos(th), s = Mathf.Sin(th);
                const float n = 3.4f;
                float rx = Mathf.Sign(c) * Mathf.Pow(Mathf.Abs(c), 2f / n);
                float rz = Mathf.Sign(s) * Mathf.Pow(Mathf.Abs(s), 2f / n);
                float r = 1100f * (1f + 0.035f * Mathf.Sin(4f * th + 1f));
                ring.Add(Centre + new Vector2(rx, rz) * r);
            }
            Highway = MakeRoad("Highway", Resample(ring, true, 4f), true, true, HighwayHalf);
            Profile(Highway, 160f, 0.045f, float.NaN, float.NaN);
            Register(Highway);

            // National roads: out of each exit, then winding across country to the ring's inner edge.
            for (int e = 0; e < Exits.Length; e++)
            {
                var ex = Exits[e];
                int j = RingIndex(ex.ringAngle);
                Junctions.Add(j);
                Vector2 ringPt = Flat(Highway.pts[j]);
                Vector2 inward = (Centre - ringPt).normalized;
                Vector2 end = ringPt + inward * (HighwayHalf - 1f);
                Vector2 approach = ringPt + inward * (HighwayHalf + 70f);
                Vector2 start = ex.edge, lead = start + ex.dir * 90f;
                var ctrl = new List<Vector2> { start - ex.dir * 4f, start, lead };
                Vector2 span = approach - lead;
                Vector2 perp = new Vector2(-span.y, span.x).normalized;
                int bends = Mathf.Max(2, Mathf.RoundToInt(span.magnitude / 170f));
                for (int b = 1; b < bends; b++)
                {
                    float t = b / (float)bends;
                    float amp = (55f + 25f * Mathf.Sin(e * 2.3f + b)) * (b % 2 == 0 ? 1f : -1f);
                    ctrl.Add(lead + span * t + perp * amp);
                }
                ctrl.Add(approach);
                ctrl.Add(end);
                var pts = Resample(CatmullRom(ctrl), false, 4f);
                // Drop the run-in: the road starts where it leaves the city square.
                while (pts.Count > 2 && InCity(pts[1].x, pts[1].y)) pts.RemoveAt(0);
                pts[0] = start;
                var road = MakeRoad("National" + e, pts, false, false, NationalHalf);
                float startY = CityLayout.Height(start.x, start.y);
                Profile(road, 60f, 0.075f, startY, Highway.pts[j].y);
                National.Add(road);
                Register(road);
            }
            BuildGraph();
        }

        public static int RingIndex(float degrees)
        {
            Vector2 dir = new Vector2(Mathf.Cos(degrees * Mathf.Deg2Rad), Mathf.Sin(degrees * Mathf.Deg2Rad));
            int best = 0;
            float bestDot = -2f;
            for (int i = 0; i < Highway.Count; i++)
            {
                float dot = Vector2.Dot((Flat(Highway.pts[i]) - Centre).normalized, dir);
                if (dot > bestDot) { bestDot = dot; best = i; }
            }
            return best;
        }

        static Road MakeRoad(string name, List<Vector2> pts2, bool closed, bool highway, float half)
        {
            var road = new Road { name = name, closed = closed, highway = highway, halfWidth = half };
            int n = pts2.Count;
            road.pts = new Vector3[n];
            road.right = new Vector3[n];
            road.dist = new float[n];
            for (int i = 0; i < n; i++) road.pts[i] = new Vector3(pts2[i].x, 0f, pts2[i].y);
            for (int i = 0; i < n; i++)
            {
                Vector3 t = road.pts[road.Wrap(i + 1)] - road.pts[road.Wrap(i - 1)];
                t.y = 0f;
                t.Normalize();
                road.right[i] = new Vector3(t.z, 0f, -t.x);
                if (i > 0) road.dist[i] = road.dist[i - 1] + Vector3.Distance(road.pts[i - 1], road.pts[i]);
            }
            return road;
        }

        // Smoothed natural height along the road, grade-limited, optionally pinned at both ends.
        static void Profile(Road road, float sigma, float grade, float startY, float endY)
        {
            int n = road.Count;
            var raw = new float[n];
            for (int i = 0; i < n; i++) raw[i] = Natural(road.pts[i].x, road.pts[i].z);
            var y = new float[n];
            int win = Mathf.CeilToInt(sigma * 2.5f / 4f);
            for (int i = 0; i < n; i++)
            {
                float sum = 0f, wsum = 0f;
                for (int k = -win; k <= win; k++)
                {
                    int idx = i + k;
                    if (road.closed) idx = road.Wrap(idx);
                    else if (idx < 0 || idx >= n) continue;
                    float w = Mathf.Exp(-(k * 4f) * (k * 4f) / (2f * sigma * sigma));
                    sum += raw[idx] * w;
                    wsum += w;
                }
                y[i] = sum / wsum;
            }
            if (!float.IsNaN(startY))
            {
                float d0 = startY - y[0], d1 = endY - y[n - 1];
                for (int i = 0; i < n; i++) y[i] += Mathf.Lerp(d0, d1, road.dist[i] / road.Length);
            }
            for (int pass = 0; pass < 4; pass++)
            {
                for (int i = 1; i < n; i++) y[i] = Mathf.Clamp(y[i], y[i - 1] - grade * 4f, y[i - 1] + grade * 4f);
                for (int i = n - 2; i >= 0; i--) y[i] = Mathf.Clamp(y[i], y[i + 1] - grade * 4f, y[i + 1] + grade * 4f);
                if (!float.IsNaN(startY)) { y[0] = startY; y[n - 1] = endY; }
            }
            for (int i = 0; i < n; i++) road.pts[i].y = y[i];
        }

        static void Register(Road road)
        {
            road.index = Roads.Count;
            Roads.Add(road);
            for (int i = 0; i < road.Count; i++)
            {
                long k = Key(Mathf.FloorToInt(road.pts[i].x / HashCell), Mathf.FloorToInt(road.pts[i].z / HashCell));
                if (!hash.TryGetValue(k, out var list)) hash[k] = list = new List<(Road, int)>();
                list.Add((road, i));
            }
        }

        public static Vector2 Flat(Vector3 v) => new Vector2(v.x, v.z);

        static List<Vector2> CatmullRom(List<Vector2> c)
        {
            var o = new List<Vector2>();
            for (int s = 0; s < c.Count - 1; s++)
            {
                Vector2 p0 = c[Mathf.Max(s - 1, 0)], p1 = c[s], p2 = c[s + 1], p3 = c[Mathf.Min(s + 2, c.Count - 1)];
                for (int k = 0; k < 24; k++)
                {
                    float t = k / 24f, t2 = t * t, t3 = t2 * t;
                    o.Add(0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3));
                }
            }
            o.Add(c[c.Count - 1]);
            return o;
        }

        static List<Vector2> Resample(List<Vector2> pts, bool closed, float spacing)
        {
            var cum = new List<float> { 0f };
            int n = pts.Count, segs = closed ? n : n - 1;
            for (int i = 0; i < segs; i++) cum.Add(cum[i] + Vector2.Distance(pts[i], pts[(i + 1) % n]));
            float len = cum[segs];
            int count = Mathf.Max(2, Mathf.RoundToInt(len / spacing));
            var o = new List<Vector2>();
            int j = 0;
            int total = closed ? count : count + 1;
            float step = len / count;
            for (int i = 0; i < total; i++)
            {
                float target = Mathf.Min(i * step, len);
                while (j < segs - 1 && cum[j + 1] < target) j++;
                float seg = cum[j + 1] - cum[j];
                float t = seg > 1e-5f ? (target - cum[j]) / seg : 0f;
                o.Add(Vector2.Lerp(pts[j], pts[(j + 1) % n], t));
            }
            return o;
        }

        // ---- Road graph (police navigation) ----

        public class Node
        {
            public Vector3 pos;
            public readonly List<int> next = new List<int>();
        }

        public static readonly List<Node> Graph = new List<Node>();

        static int AddNode(Vector3 p)
        {
            Graph.Add(new Node { pos = p });
            return Graph.Count - 1;
        }

        static void Link(int a, int b, bool both = true)
        {
            if (!Graph[a].next.Contains(b)) Graph[a].next.Add(b);
            if (both && !Graph[b].next.Contains(a)) Graph[b].next.Add(a);
        }

        static void BuildGraph()
        {
            Graph.Clear();
            int lines = CityLayout.Lines;
            var grid = new int[lines, lines];
            for (int i = 0; i < lines; i++)
            for (int j = 0; j < lines; j++) grid[i, j] = AddNode(CityLayout.Intersection(i, j));
            for (int i = 0; i < lines; i++)
            for (int j = 0; j < lines; j++)
            {
                if (i + 1 < lines) Link(grid[i, j], grid[i + 1, j]);
                if (j + 1 < lines) Link(grid[i, j], grid[i, j + 1]);
            }

            // Highway: one directed chain per carriageway (right-hand traffic), a node every ~40 m.
            const int step = 10;
            int hn = Highway.Count / step;
            var ccw = new int[hn];
            var cw = new int[hn];
            for (int k = 0; k < hn; k++)
            {
                int i = k * step;
                Vector3 c = Highway.pts[i], r = Highway.right[i];
                // The ring is generated anticlockwise (seen from above): its right side is the outside.
                ccw[k] = AddNode(c + r * CarriageCentre);
                cw[k] = AddNode(c - r * CarriageCentre);
            }
            for (int k = 0; k < hn; k++)
            {
                Link(ccw[k], ccw[(k + 1) % hn], false);
                Link(cw[(k + 1) % hn], cw[k], false);
            }

            // National roads, two-way, joined to their exit intersection and both carriageways.
            for (int e = 0; e < National.Count; e++)
            {
                var road = National[e];
                Vector2 ce = Exits[e].cityEnd;
                int prev = grid[Mathf.RoundToInt(ce.x / CityLayout.Pitch), Mathf.RoundToInt(ce.y / CityLayout.Pitch)];
                for (int i = 0; i < road.Count; i += 10)
                {
                    int node = AddNode(road.pts[i]);
                    Link(prev, node);
                    prev = node;
                }
                int last = AddNode(road.pts[road.Count - 1]);
                Link(prev, last);
                int k = Mathf.RoundToInt(Junctions[e] / (float)step) % hn;
                Link(last, ccw[k]);
                Link(last, cw[k]);
            }
        }

        public static int NearestNode(Vector3 p, Vector3 heading)
        {
            int best = -1;
            float bestScore = float.MaxValue;
            for (int i = 0; i < Graph.Count; i++)
            {
                Vector3 d = Graph[i].pos - p;
                d.y = 0f;
                float score = d.magnitude;
                if (heading.sqrMagnitude > 0.01f && Vector3.Dot(d, heading) < -5f) score += 60f; // prefer ahead
                if (score < bestScore) { bestScore = score; best = i; }
            }
            return best;
        }

        // A* over the road graph; returns node positions from start to goal (empty if unreachable).
        public static List<Vector3> FindPath(int start, int goal)
        {
            var path = new List<Vector3>();
            if (start < 0 || goal < 0) return path;
            int n = Graph.Count;
            var g = new float[n];
            var from = new int[n];
            var closed = new bool[n];
            for (int i = 0; i < n; i++) { g[i] = float.MaxValue; from[i] = -1; }
            var open = new List<int> { start };
            g[start] = 0f;
            Vector3 goalPos = Graph[goal].pos;
            while (open.Count > 0)
            {
                int bi = 0;
                float bf = float.MaxValue;
                for (int k = 0; k < open.Count; k++)
                {
                    float f = g[open[k]] + Vector3.Distance(Graph[open[k]].pos, goalPos);
                    if (f < bf) { bf = f; bi = k; }
                }
                int cur = open[bi];
                open.RemoveAt(bi);
                if (cur == goal) break;
                if (closed[cur]) continue;
                closed[cur] = true;
                foreach (int nb in Graph[cur].next)
                {
                    float ng = g[cur] + Vector3.Distance(Graph[cur].pos, Graph[nb].pos);
                    if (ng < g[nb]) { g[nb] = ng; from[nb] = cur; open.Add(nb); }
                }
            }
            if (g[goal] == float.MaxValue) return path;
            for (int c = goal; c >= 0; c = from[c]) path.Add(Graph[c].pos);
            path.Reverse();
            return path;
        }
    }
}
