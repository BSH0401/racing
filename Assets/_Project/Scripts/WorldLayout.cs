using System.Collections.Generic;
using UnityEngine;

namespace Racing
{
    // The open world around the city: rolling countryside with a river running into a lake, a
    // highway ring (3 lanes each way with a barrier in the middle) and four winding two-lane national
    // roads that fly over the ring at diamond interchanges, with an on and off ramp for each
    // carriageway. Roads carry their own smoothed, grade-limited height profile; where a road runs
    // well above the ground (river valley, flyovers, ramps) it becomes a bridge deck on piers,
    // elsewhere the terrain is pulled onto it, which gives natural cuttings and embankments.
    // Inside the city square CityLayout rules unchanged.
    public static class WorldLayout
    {
        public const float Min = -1500f, Max = 2268f; // aligned with the city edges on an 8 m grid
        public static readonly Vector2 Centre = new Vector2(384f, 384f);

        // Highway cross-section (half widths from the centre line).
        public const float MedianHalf = 2.5f, LaneWidth = 3.6f, HighwayLanes = 3, ShoulderWidth = 2.5f;
        public static float CarriageCentre => MedianHalf + LaneWidth * HighwayLanes * 0.5f;     // 7.9
        public static float HighwayHalf => MedianHalf + LaneWidth * HighwayLanes + ShoulderWidth; // 15.8
        public const float NationalHalf = 5f; // two 3.5 m lanes plus 1.5 m shoulders
        public const float RampHalf = 4.5f;   // one 4.5 m lane plus shoulders
        public const float Verge = 12f;

        // Interchanges: the national road crosses the ring at 'BridgeRise' above it; ramp terminals sit
        // 'TerminalOffset' from the ring's centre line, ramps leave the ring up to 'RampReach' away.
        public const float BridgeRise = 7.5f, TerminalOffset = 50f, RampReach = 330f;
        // A road this far above the ground under it is built as a bridge deck.
        public const float BridgeClearance = 4f, DeckDepth = 1.3f;

        public class Road
        {
            public string name;
            public bool highway, closed, ramp, pad;
            public int index;
            public float halfWidth;
            public Vector3[] pts;   // centre line with profile height, ~4 m apart
            public Vector3[] right; // flat right-hand normals
            public float[] dist;
            public bool[] elevated; // bridge deck at this sample
            public float Length => dist[dist.Length - 1];
            public float Lift => highway ? 0.015f : ramp ? 0.03f : pad ? 0.045f : 0f;

            public int Count => pts.Length;
            public int Wrap(int i) => closed ? ((i % Count) + Count) % Count : Mathf.Clamp(i, 0, Count - 1);
        }

        // A city exit: the street that continues out of the city and the national road starting at it.
        public struct Exit
        {
            public Vector2 cityEnd;   // last intersection inside the street grid
            public Vector2 edge;      // where the street leaves the city square
            public Vector2 dir;       // outward
            public float ringAngle;   // where its national road crosses the highway (degrees)
        }

        public static readonly Exit[] Exits =
        {
            new Exit { cityEnd = new Vector2(768f, 384f), edge = new Vector2(CityLayout.Max, 384f), dir = Vector2.right, ringAngle = 2f },
            new Exit { cityEnd = new Vector2(384f, 768f), edge = new Vector2(384f, CityLayout.Max), dir = Vector2.up, ringAngle = 92f },
            new Exit { cityEnd = new Vector2(96f, 0f), edge = new Vector2(96f, CityLayout.Min), dir = Vector2.down, ringAngle = 248f },
            new Exit { cityEnd = new Vector2(0f, 576f), edge = new Vector2(CityLayout.Min, 576f), dir = Vector2.left, ringAngle = 172f },
        };

        // Diamond interchange where a national road flies over the ring. "Outer" ramps serve the
        // anticlockwise (outside) carriageway, "inner" ramps the clockwise one.
        public class Interchange
        {
            public int j;                // highway sample under the flyover
            public Road national;
            public int natInner, natOuter; // national road samples at the two ramp terminals
            public Road offOuter, onOuter, offInner, onInner;
            public Road padInner, padOuter;
        }

        public static Road Highway { get; private set; }
        public static readonly List<Road> National = new List<Road>();
        public static readonly List<Road> Ramps = new List<Road>();
        public static readonly List<Road> Pads = new List<Road>();
        public const float PadRadius = 12f;
        public static readonly List<Road> Roads = new List<Road>();
        public static readonly List<Interchange> Interchanges = new List<Interchange>();
        // Highway sample index of each interchange (same order as Exits).
        public static readonly List<int> Junctions = new List<int>();

        static readonly Dictionary<long, List<(Road road, int i)>> hash = new Dictionary<long, List<(Road, int)>>();
        const float HashCell = 48f;

        static WorldLayout() => Build();

        public static bool InBounds(Vector3 p, float inset = 0f) =>
            p.x > Min + inset && p.x < Max - inset && p.z > Min + inset && p.z < Max - inset;

        static bool InCity(float x, float z) =>
            x >= CityLayout.Min && x <= CityLayout.Max && z >= CityLayout.Min && z <= CityLayout.Max;

        // ---- River and lake ----

        // A river comes in over the north-east world edge, passes under the ring's corner and fills a
        // lake between the ring and the city.
        public static readonly Vector2 LakeCentre = new Vector2(1060f, 1060f);
        public const float LakeRadius = 130f, RiverHalf = 18f;
        public static Vector2[] RiverPts { get; private set; }
        public static float[] RiverLevel { get; private set; }
        public static float LakeLevel { get; private set; }

        const float WaterCell = 8f;
        static int waterN;
        static float[] waterDist, waterLevel; // distance past the water's edge, surface level

        static void BuildWater()
        {
            var ctrl = new List<Vector2>
            {
                new Vector2(2420f, 1990f), new Vector2(2380f, 1960f), new Vector2(2080f, 1790f), new Vector2(1780f, 1610f),
                new Vector2(1500f, 1420f), new Vector2(1260f, 1230f), LakeCentre,
            };
            var pts = Resample(CatmullRom(ctrl), false, 4f);
            RiverPts = pts.ToArray();
            // Surface a few metres below the ground along the bed, never running uphill, and at least
            // 'BridgeClearance' + deck below any road that crosses it.
            RiverLevel = new float[pts.Count];
            for (int i = 0; i < pts.Count; i++)
            {
                float level = Natural0(pts[i].x, pts[i].y) - 6f;
                int n = Query(pts[i].x, pts[i].y, RiverHalf + 25f, out var near);
                for (int k = 0; k < n; k++) level = Mathf.Min(level, near[k].y - DeckDepth - BridgeClearance - 1f);
                RiverLevel[i] = i > 0 ? Mathf.Min(level, RiverLevel[i - 1]) : level;
            }
            LakeLevel = Mathf.Min(RiverLevel[pts.Count - 1], Natural0(LakeCentre.x, LakeCentre.y) - 7f);
            RiverLevel[pts.Count - 1] = LakeLevel;
            // Ease the steps out of the profile, never above the limits found above, never uphill.
            var cap = (float[])RiverLevel.Clone();
            for (int pass = 0; pass < 3; pass++)
                for (int i = 1; i < pts.Count - 1; i++)
                    RiverLevel[i] = Mathf.Min((RiverLevel[i - 1] + RiverLevel[i] + RiverLevel[i + 1]) / 3f, RiverLevel[i - 1], cap[i]);

            waterN = Mathf.CeilToInt((Max - Min) / WaterCell) + 1;
            waterDist = new float[waterN * waterN];
            waterLevel = new float[waterN * waterN];
            const float reach = 220f;
            for (int gj = 0; gj < waterN; gj++)
            for (int gi = 0; gi < waterN; gi++)
            {
                var p = new Vector2(Min + gi * WaterCell, Min + gj * WaterCell);
                float best = Vector2.Distance(p, LakeCentre) - LakeRadius, level = LakeLevel;
                if (p.x > 1000f && p.y > 1000f)
                {
                    for (int i = 0; i < RiverPts.Length; i += 2)
                    {
                        float d = Vector2.Distance(p, RiverPts[i]) - RiverHalf;
                        if (d < best) { best = d; level = RiverLevel[i]; }
                    }
                }
                waterDist[gj * waterN + gi] = Mathf.Min(best, reach);
                waterLevel[gj * waterN + gi] = level;
            }
        }

        // Distance past the nearest water's edge (negative = in the water) and its surface level.
        public static float WaterDistance(float x, float z, out float level)
        {
            float fx = Mathf.Clamp((x - Min) / WaterCell, 0f, waterN - 1.001f), fz = Mathf.Clamp((z - Min) / WaterCell, 0f, waterN - 1.001f);
            int i = (int)fx, j = (int)fz;
            float tx = fx - i, tz = fz - j;
            int a = j * waterN + i;
            float d = Mathf.Lerp(Mathf.Lerp(waterDist[a], waterDist[a + 1], tx), Mathf.Lerp(waterDist[a + waterN], waterDist[a + waterN + 1], tx), tz);
            level = Mathf.Lerp(Mathf.Lerp(waterLevel[a], waterLevel[a + 1], tx), Mathf.Lerp(waterLevel[a + waterN], waterLevel[a + waterN + 1], tx), tz);
            return d;
        }

        // ---- Heights ----

        // Final ground height: city rules inside the city, otherwise countryside pulled onto the roads
        // that run at ground level, and kept under the bridge decks.
        public static float Height(float x, float z)
        {
            if (InCity(x, z)) return CityLayout.Height(x, z);
            float h = PulledGround(x, z, false, out int n, out var near);
            for (int k = 0; k < n; k++)
                if (near[k].elevated && near[k].d <= near[k].road.halfWidth + 2f)
                    h = Mathf.Min(h, near[k].y - DeckDepth - 0.2f);
            return h;
        }

        // Ground pulled onto the ground-level roads nearby (or only the highway). Where two roads both
        // claim a point, the one whose paving is nearer wins.
        static float PulledGround(float x, float z, bool highwayOnly, out int n, out RoadHit[] near)
        {
            float nat = Natural(x, z);
            float bestW = 0f, bestEdge = float.MaxValue, target = nat;
            n = Query(x, z, 45f, out near);
            for (int k = 0; k < n; k++)
            {
                var road = near[k].road;
                if (near[k].elevated || (highwayOnly && !road.highway)) continue;
                float d = near[k].d, y = near[k].y;
                // Flat verge wider than a terrain cell's diagonal (8 m grid), so no ground triangle that
                // overlaps the paving can tilt up over it in a cutting.
                float core = road.halfWidth + Verge;
                float fall = road.highway ? 42f : 26f;
                float w = d <= core ? 1f : 1f - Smooth((d - core) / fall);
                float edge = d - road.halfWidth;
                if (w > bestW + 1e-4f || (w >= 1f && bestW >= 1f && edge < bestEdge))
                {
                    bestW = w;
                    bestEdge = edge;
                    target = y - 0.06f;
                }
            }
            return Mathf.Lerp(nat, target, bestW);
        }

        // Height of the paved surface at (x, z) nearest to 'nearY' (decks stack at flyovers), or the
        // ground where there is no road.
        public static float SurfaceHeight(float x, float z, float nearY)
        {
            if (InCity(x, z)) return CityLayout.Height(x, z);
            int n = Query(x, z, 1f, out var near);
            float best = float.NaN, bestDy = float.MaxValue;
            for (int k = 0; k < n; k++)
            {
                if (near[k].d > near[k].road.halfWidth + 0.5f) continue;
                float y = near[k].y + near[k].road.Lift;
                float dy = Mathf.Abs(y - nearY);
                if (dy < bestDy) { bestDy = dy; best = y; }
            }
            return float.IsNaN(best) ? Height(x, z) : best;
        }

        // Countryside with the river valley and lake basin carved in.
        public static float Natural(float x, float z)
        {
            float nat = Natural0(x, z);
            float d = WaterDistance(x, z, out float level);
            if (d > 200f) return nat;
            float bank = d < -6f ? level - 2.5f
                : d < 6f ? level - 2.5f + 3.3f * Smooth((d + 6f) / 12f)
                : level + 0.8f + 40f * Smooth((d - 6f) / 160f);
            return Mathf.Min(nat, bank);
        }

        // Countryside without water: blends out of the city's hills into bigger rolling country,
        // rising towards a ring of high ground near the world edge.
        public static float Natural0(float x, float z)
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
            public int i;
            public bool elevated;
        }

        const int MaxRoads = 32;
        static readonly RoadHit[] hits = new RoadHit[MaxRoads];
        static readonly int[] bestIdx = new int[MaxRoads];
        static readonly float[] bestD2 = new float[MaxRoads];

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
                bool elev = road.elevated != null && road.elevated[bi];
                hits[count++] = new RoadHit { road = road, d = d, y = y, i = bi, elevated = elev };
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

        // True when (x, z) lies on the paving of a road other than 'self' at about height y (within
        // 'margin' of its edge; 'padMargin' for terminal pads, whose rims carry their own parapet).
        public static bool OnOtherPaving(Road self, Vector3 p, float margin = 0.3f, float dy = 2.5f, float padMargin = float.NaN)
        {
            int n = Query(p.x, p.z, Mathf.Max(margin, 0f) + 1f, out var near);
            for (int k = 0; k < n; k++)
            {
                float m = near[k].road.pad && !float.IsNaN(padMargin) ? padMargin : margin;
                if (near[k].road != self && near[k].d < near[k].road.halfWidth + m && Mathf.Abs(near[k].y - p.y) < dy) return true;
            }
            return false;
        }

        // Paving of any road other than 'self' below height y (piers must not stand on it).
        public static bool OverLowerPaving(Road self, Vector3 p, float margin)
        {
            int n = Query(p.x, p.z, margin + 1f, out var near);
            for (int k = 0; k < n; k++)
                if (near[k].road != self && near[k].d < near[k].road.halfWidth + margin && near[k].y < p.y - 2f) return true;
            return false;
        }

        static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

        // Dev check (-validateworld): ground poking through the paved surface (or a deck) of any road.
        public static void Validate()
        {
            foreach (var road in Roads)
            {
                float worst = 0f;
                Vector3 at = Vector3.zero;
                int bad = 0, decks = 0;
                for (int i = 0; i < road.Count; i++)
                {
                    if (road.elevated[i]) decks++;
                    for (float o = -road.halfWidth; o <= road.halfWidth; o += 2f)
                    {
                        Vector3 p = road.pts[i] + road.right[i] * o;
                        if (OnOtherPaving(road, p, -0.5f, 0.6f)) continue; // ramp mouths overlap
                        float limit = road.elevated[i] ? road.pts[i].y - DeckDepth + 0.05f : road.pts[i].y;
                        float above = Height(p.x, p.z) - limit;
                        if (above > 0.02f) bad++;
                        if (above > worst) { worst = above; at = p; }
                    }
                }
                Debug.Log($"[World] {road.name} len={road.Length:F0} samples={road.Count} deck={decks} groundAbove>2cm={bad} worst={worst:F2} at {at:F0}");
            }
            Debug.Log($"[World] lake level {LakeLevel:F1}, river {RiverLevel[0]:F1} -> {RiverLevel[RiverLevel.Length - 1]:F1}");
        }

        // ---- Construction ----

        static void Build()
        {
            Roads.Clear();
            National.Clear();
            Ramps.Clear();
            Pads.Clear();
            Interchanges.Clear();
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
            // Profiles ignore the river, so roads bridge the valley instead of dipping into it.
            Profile(Highway, 160f, 0.045f, float.NaN, -1, 0f);
            Register(Highway);
            for (int e = 0; e < Exits.Length; e++) BuildInterchange(e);

            // Water goes in once the road heights are known (it must pass under them), then decks.
            BuildWater();
            MarkDecks(Highway, Natural);
            foreach (var road in National) MarkDecks(road, HighwayGround);
            foreach (var road in Ramps) MarkDecks(road, HighwayGround);
            foreach (var road in Pads) MarkDecks(road, HighwayGround);
            BuildGraph();
        }

        static float HighwayGround(float x, float z) => PulledGround(x, z, true, out _, out _);

        // National road e: out of its city exit, winding across country, then straight over the ring
        // on a flyover to the outer ramp terminal. Plus the four ramps of its diamond interchange.
        static void BuildInterchange(int e)
        {
            var ex = Exits[e];
            int j = RingIndex(ex.ringAngle);
            Junctions.Add(j);
            Vector3 hj = Highway.pts[j];
            Vector2 c = Flat(hj), r = Flat(Highway.right[j]);
            float D = TerminalOffset;
            Vector2 L(float l) => c + r * l; // along the ring's normal (+ = outside)

            Vector2 start = ex.edge, lead = start + ex.dir * 90f;
            var ctrl = new List<Vector2> { start - ex.dir * 4f, start, lead };
            Vector2 approach = L(-D - 130f);
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
            ctrl.Add(L(-D - 60f));
            ctrl.Add(L(-D));
            ctrl.Add(L(0f));
            ctrl.Add(L(D));
            ctrl.Add(L(D + 7f));
            var pts = Resample(CatmullRom(ctrl), false, 4f);
            // Drop the run-in: the road starts where it leaves the city square.
            while (pts.Count > 2 && InCity(pts[1].x, pts[1].y)) pts.RemoveAt(0);
            pts[0] = start;
            var road = MakeRoad("National" + e, pts, false, false, NationalHalf);
            int inner = Nearest(road, L(-D)), outer = Nearest(road, L(D));
            float deckY = hj.y + BridgeRise;
            Profile(road, 60f, 0.075f, CityLayout.Height(start.x, start.y), inner, deckY);
            National.Add(road);
            Register(road);

            var ic = new Interchange { j = j, national = road, natInner = inner, natOuter = outer };
            float eo = HighwayHalf + RampHalf - 0.3f, tn = NationalHalf - 1f;
            // (s along the ring in metres, l across it), in driving order.
            // Ramps run beside the ring and taper into its outer lane, so the paving is continuous.
            float tp = HighwayHalf - 2.5f, lr = RampReach;
            ic.offOuter = Ramp("OffRampOuter" + e, j, deckY, new[] { V(-lr - 40, tp), V(-lr, eo - 2), V(-250, eo), V(-190, eo + 5), V(-130, D - 14), V(-75, D - 2), V(-35, D), V(-tn, D) });
            ic.onOuter = Ramp("OnRampOuter" + e, j, deckY, new[] { V(tn, D), V(35, D), V(75, D - 2), V(130, D - 14), V(190, eo + 5), V(250, eo), V(lr, eo - 2), V(lr + 40, tp) });
            ic.offInner = Ramp("OffRampInner" + e, j, deckY, new[] { V(lr + 40, -tp), V(lr, -eo + 2), V(250, -eo), V(190, -eo - 5), V(130, -D + 14), V(75, -D + 2), V(35, -D), V(tn, -D) });
            ic.onInner = Ramp("OnRampInner" + e, j, deckY, new[] { V(-tn, -D), V(-35, -D), V(-75, -D + 2), V(-130, -D + 14), V(-190, -eo - 5), V(-250, -eo), V(-lr, -eo + 2), V(-lr - 40, -tp) });
            // Paved pads at the ramp terminals, so turning traffic can cut the corners.
            ic.padInner = Pad("PadInner" + e, road, inner, deckY);
            ic.padOuter = Pad("PadOuter" + e, road, outer, deckY);
            Interchanges.Add(ic);

            static Vector2 V(float s, float l) => new Vector2(s, l);
        }

        // World point at (s metres along the ring from sample j, l metres across it) at height y.
        public static Vector3 RingPoint(int j, float s, float l, float y)
        {
            Vector2 p = RingFrame(j, new Vector2(s, l), out _);
            return new Vector3(p.x, y, p.y);
        }

        // Point at (s metres along the ring from sample j, l metres across it).
        static Vector2 RingFrame(int j, Vector2 sl, out float y)
        {
            float spacing = Highway.dist[1];
            float f = j + sl.x / spacing;
            int a = Mathf.FloorToInt(f);
            float t = f - a;
            Vector3 p = Vector3.Lerp(Highway.pts[Highway.Wrap(a)], Highway.pts[Highway.Wrap(a + 1)], t);
            Vector3 rt = Vector3.Lerp(Highway.right[Highway.Wrap(a)], Highway.right[Highway.Wrap(a + 1)], t).normalized;
            y = p.y;
            return Flat(p + rt * sl.y);
        }

        // One-way ramp between the ring (level with it) and a terminal at deck height on the flyover.
        static Road Ramp(string name, int j, float deckY, Vector2[] sl)
        {
            var ctrl = new List<Vector2>();
            foreach (var p in sl) ctrl.Add(RingFrame(j, p, out _));
            var road = MakeRoad(name, Resample(CatmullRom(ctrl), false, 4f), false, false, RampHalf);
            road.ramp = true;
            int n = road.Count;
            float lo = HighwayHalf + RampHalf + 1f, hi = TerminalOffset - 3f;
            var pinned = new bool[n];
            var y = new float[n];
            for (int i = 0; i < n; i++)
            {
                // Lateral distance from the ring decides how far up the ramp has climbed.
                int hn = Query(road.pts[i].x, road.pts[i].z, 400f, out var near);
                float d = float.MaxValue, hy = deckY;
                for (int k = 0; k < hn; k++)
                    if (near[k].road == Highway) { d = near[k].d; hy = near[k].y; }
                float u = Smooth(Mathf.InverseLerp(lo, hi, d));
                y[i] = Mathf.Lerp(hy, deckY, u);
                pinned[i] = d < lo || i < 2 && sl[0].y * sl[0].y > hi * hi || i > n - 3 && sl[sl.Length - 1].y * sl[sl.Length - 1].y > hi * hi;
            }
            // The blend of the ring's own (smooth) profile and the deck height is already smooth; just
            // ease out the corners where the ramp starts to climb, keeping the pinned ends exact.
            for (int pass = 0; pass < 4; pass++)
            {
                var prev = (float[])y.Clone();
                for (int i = 1; i < n - 1; i++)
                    if (!pinned[i]) y[i] = (prev[i - 1] + prev[i] * 2f + prev[i + 1]) * 0.25f;
            }
            for (int i = 0; i < n; i++) road.pts[i].y = y[i];
            Ramps.Add(road);
            Register(road);
            return road;
        }

        // A round paved pad (a 1 m road with a 12 m half width) centred on national road sample i.
        static Road Pad(string name, Road national, int i, float y)
        {
            Vector3 c = national.pts[i], t = new Vector3(-national.right[i].z, 0f, national.right[i].x);
            var road = MakeRoad(name, new List<Vector2> { Flat(c - t * 0.5f), Flat(c + t * 0.5f) }, false, false, PadRadius);
            road.pad = true;
            for (int k = 0; k < road.Count; k++) road.pts[k].y = y;
            Pads.Add(road);
            Register(road);
            return road;
        }

        static int Nearest(Road road, Vector2 p)
        {
            int best = 0;
            float bd = float.MaxValue;
            for (int i = 0; i < road.Count; i++)
            {
                float d = (Flat(road.pts[i]) - p).sqrMagnitude;
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

        // Bridge decks: samples well above the ground beneath (runs shorter than 4 samples dropped).
        static void MarkDecks(Road road, System.Func<float, float, float> ground)
        {
            int n = road.Count;
            road.elevated = new bool[n];
            for (int i = 0; i < n; i++)
            {
                Vector3 p = road.pts[i];
                float g = ground(p.x, p.z);
                // Check the edges too: a deck on a side slope needs the low side.
                foreach (float o in new[] { -road.halfWidth, road.halfWidth })
                {
                    Vector3 q = p + road.right[i] * o;
                    g = Mathf.Min(g, ground(q.x, q.z) + 1.5f);
                }
                road.elevated[i] = p.y - g > BridgeClearance && !InCity(p.x, p.z);
            }
            for (int pass = 0; pass < 2; pass++)
            {
                bool want = pass == 0; // first fill short gaps in decks, then drop short decks
                for (int i = 0; i < n;)
                {
                    int k = i;
                    while (k < n && road.elevated[k] == road.elevated[i]) k++;
                    bool atEnd = !road.closed && (i == 0 || k == n);
                    if (road.elevated[i] != want && k - i < 4 && !atEnd)
                        for (int m = i; m < k; m++) road.elevated[m] = want;
                    i = k;
                }
            }
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

        // Smoothed natural height (without the river) along the road, grade-limited. Optionally pinned
        // to startY at the start and held at pinY from sample 'pinFrom' to the end.
        static void Profile(Road road, float sigma, float grade, float startY, int pinFrom, float pinY)
        {
            int n = road.Count;
            var raw = new float[n];
            for (int i = 0; i < n; i++) raw[i] = Natural0(road.pts[i].x, road.pts[i].z);
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
            bool pinned = !float.IsNaN(startY);
            int last = pinFrom >= 0 ? pinFrom : n - 1;
            if (pinned)
            {
                float d0 = startY - y[0], d1 = pinY - y[last];
                for (int i = 0; i <= last; i++) y[i] += Mathf.Lerp(d0, d1, road.dist[i] / road.dist[last]);
            }
            void Pin()
            {
                if (!pinned) return;
                y[0] = startY;
                for (int i = last; i < n; i++) y[i] = pinY;
            }
            Pin();
            for (int pass = 0; pass < 4; pass++)
            {
                for (int i = 1; i < n; i++) y[i] = Mathf.Clamp(y[i], y[i - 1] - grade * 4f, y[i - 1] + grade * 4f);
                for (int i = n - 2; i >= 0; i--) y[i] = Mathf.Clamp(y[i], y[i + 1] - grade * 4f, y[i + 1] + grade * 4f);
                Pin();
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
            int HighwayNode(Vector3 p, int[] chain)
            {
                int best = 0;
                float bd = float.MaxValue;
                for (int k = 0; k < chain.Length; k++)
                {
                    float d = (Graph[chain[k]].pos - p).sqrMagnitude;
                    if (d < bd) { bd = d; best = chain[k]; }
                }
                return best;
            }

            // National roads, two-way, from their exit intersection over the flyover.
            for (int e = 0; e < National.Count; e++)
            {
                var ic = Interchanges[e];
                var road = National[e];
                Vector2 ce = Exits[e].cityEnd;
                int prev = grid[Mathf.RoundToInt(ce.x / CityLayout.Pitch), Mathf.RoundToInt(ce.y / CityLayout.Pitch)];
                int innerNode = -1, outerNode = -1;
                for (int i = 0; i < road.Count; i++)
                {
                    bool key = i == ic.natInner || i == ic.natOuter || i == road.Count - 1;
                    if (i % 10 != 0 && !key) continue;
                    int node = AddNode(road.pts[i]);
                    Link(prev, node);
                    prev = node;
                    if (i == ic.natInner) innerNode = node;
                    if (i == ic.natOuter) outerNode = node;
                }
                // Ramps, one-way: highway -> off ramp -> terminal, terminal -> on ramp -> highway.
                int chainEnd = -1;
                int Chain(Road ramp)
                {
                    int first = -1, last = -1;
                    for (int i = 0; i < ramp.Count; i += 5)
                    {
                        int node = AddNode(ramp.pts[i]);
                        if (last >= 0) Link(last, node, false);
                        else first = node;
                        last = node;
                    }
                    int end = AddNode(ramp.pts[ramp.Count - 1]);
                    Link(last, end, false);
                    chainEnd = end;
                    return first;
                }
                int s0 = Chain(ic.offOuter);
                Link(HighwayNode(ic.offOuter.pts[0], ccw), s0, false);
                Link(chainEnd, outerNode, false);
                s0 = Chain(ic.onOuter);
                Link(outerNode, s0, false);
                Link(chainEnd, NextOnChain(HighwayNode(ic.onOuter.pts[ic.onOuter.Count - 1], ccw)), false);
                s0 = Chain(ic.offInner);
                Link(HighwayNode(ic.offInner.pts[0], cw), s0, false);
                Link(chainEnd, innerNode, false);
                s0 = Chain(ic.onInner);
                Link(innerNode, s0, false);
                Link(chainEnd, NextOnChain(HighwayNode(ic.onInner.pts[ic.onInner.Count - 1], cw)), false);
            }

            int NextOnChain(int node) => Graph[node].next.Count > 0 ? Graph[node].next[0] : node;
            IndexEdges();
        }

        public static int NearestNode(Vector3 p, Vector3 heading)
        {
            int best = -1;
            float bestScore = float.MaxValue;
            for (int i = 0; i < Graph.Count; i++)
            {
                Vector3 d = Graph[i].pos - p;
                float score = new Vector2(d.x, d.z).magnitude + Mathf.Abs(d.y) * 4f; // decks stack at flyovers
                if (heading.sqrMagnitude > 0.01f && Vector3.Dot(new Vector3(d.x, 0f, d.z), heading) < -5f) score += 60f; // prefer ahead
                if (score < bestScore) { bestScore = score; best = i; }
            }
            return best;
        }

        // A* over the road graph; returns node positions from start to goal (empty if unreachable).
        // The search runs over directed edges so it can charge 'turnCost' metres per 90 degrees of
        // heading change at a node: on the street grid every monotone route is equally short, and
        // without it the cheapest-looking one is a staircase with a corner at every block. 'heading'
        // (optional) is the direction the car arrives at 'start' with, so turning round costs extra.
        public static List<Vector3> FindPath(int start, int goal, Vector3 heading = default, float turnCost = 0f)
        {
            var path = new List<Vector3>();
            if (start < 0 || goal < 0) return path;
            if (start == goal) { path.Add(Graph[start].pos); return path; }
            if (edgeHead == null || edgeBase.Length != Graph.Count + 1) IndexEdges();
            int m = edgeHead.Length;
            var g = new float[m];
            var from = new int[m];
            var closed = new bool[m];
            for (int i = 0; i < m; i++) { g[i] = float.MaxValue; from[i] = -1; }
            Vector3 goalPos = Graph[goal].pos;
            var open = new MinHeap();
            heading.y = 0f;
            for (int e = edgeBase[start]; e < edgeBase[start + 1]; e++)
            {
                Vector3 d = Graph[edgeHead[e]].pos - Graph[start].pos;
                float cost = d.magnitude + (heading.sqrMagnitude > 0.01f ? TurnPenalty(heading, d, turnCost) : 0f);
                g[e] = cost;
                open.Push(e, cost + Vector3.Distance(Graph[edgeHead[e]].pos, goalPos));
            }
            int found = -1;
            while (open.Count > 0)
            {
                int cur = open.Pop();
                if (closed[cur]) continue;
                closed[cur] = true;
                int node = edgeHead[cur];
                if (node == goal) { found = cur; break; }
                Vector3 inDir = Graph[node].pos - Graph[edgeTail[cur]].pos;
                for (int e = edgeBase[node]; e < edgeBase[node + 1]; e++)
                {
                    if (closed[e]) continue;
                    Vector3 d = Graph[edgeHead[e]].pos - Graph[node].pos;
                    float ng = g[cur] + d.magnitude + TurnPenalty(inDir, d, turnCost);
                    if (ng < g[e])
                    {
                        g[e] = ng;
                        from[e] = cur;
                        open.Push(e, ng + Vector3.Distance(Graph[edgeHead[e]].pos, goalPos));
                    }
                }
            }
            if (found < 0) return path;
            for (int e = found; e >= 0; e = from[e]) path.Add(Graph[edgeHead[e]].pos);
            path.Add(Graph[start].pos);
            path.Reverse();
            return path;
        }

        // Nothing for gentle bends (curved roads are sampled every ~40 m), 'turnCost' for a right
        // angle, a little over twice that for turning round.
        static float TurnPenalty(Vector3 a, Vector3 b, float turnCost)
        {
            if (turnCost <= 0f) return 0f;
            float angle = Vector3.Angle(new Vector3(a.x, 0f, a.z), new Vector3(b.x, 0f, b.z));
            return turnCost * Mathf.Max(0f, angle - 15f) / 75f;
        }

        // Directed edges numbered node by node: node u's edges are edgeBase[u] .. edgeBase[u + 1] - 1,
        // in the order of Graph[u].next.
        static int[] edgeBase, edgeHead, edgeTail;

        static void IndexEdges()
        {
            edgeBase = new int[Graph.Count + 1];
            for (int u = 0; u < Graph.Count; u++) edgeBase[u + 1] = edgeBase[u] + Graph[u].next.Count;
            edgeHead = new int[edgeBase[Graph.Count]];
            edgeTail = new int[edgeHead.Length];
            for (int u = 0; u < Graph.Count; u++)
                for (int k = 0; k < Graph[u].next.Count; k++)
                {
                    edgeHead[edgeBase[u] + k] = Graph[u].next[k];
                    edgeTail[edgeBase[u] + k] = u;
                }
        }

        // Binary min-heap of (item, priority) for the path search.
        class MinHeap
        {
            readonly List<(int item, float key)> h = new List<(int, float)>();
            public int Count => h.Count;

            public void Push(int item, float key)
            {
                h.Add((item, key));
                for (int i = h.Count - 1; i > 0;)
                {
                    int parent = (i - 1) / 2;
                    if (h[parent].key <= h[i].key) break;
                    (h[parent], h[i]) = (h[i], h[parent]);
                    i = parent;
                }
            }

            public int Pop()
            {
                int top = h[0].item;
                h[0] = h[h.Count - 1];
                h.RemoveAt(h.Count - 1);
                for (int i = 0; ;)
                {
                    int l = i * 2 + 1, r = l + 1, s = i;
                    if (l < h.Count && h[l].key < h[s].key) s = l;
                    if (r < h.Count && h[r].key < h[s].key) s = r;
                    if (s == i) break;
                    (h[s], h[i]) = (h[i], h[s]);
                    i = s;
                }
                return top;
            }
        }
    }
}
