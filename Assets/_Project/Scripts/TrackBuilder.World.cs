using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Racing
{
    // The countryside around the city (WorldLayout): terrain chunks, road surfaces with markings,
    // bridge decks on piers, highway barriers, guard rails and lamps, the river and lake, forests of
    // photographed-bark trees and the world edge.
    public partial class TrackBuilder
    {
        [Header("World (countryside, highway, national roads)")]
        public float worldCell = 8f;
        public int worldChunks = 8;
        public float forestChunk = 128f;
        public float treeSpacing = 11f;
        [Tooltip("Racing/TerrainBlend: grass and soil blended by vertex colour.")]
        public Material countryGrass;
        public Material bark, foliageBroadleaf, foliagePine, water;

        const int BroadleafKinds = 4, PineKinds = 3;
        TreeMeshes.Tree[] treeKinds;

        TreeMeshes.Tree[] TreeKinds
        {
            get
            {
                if (treeKinds != null && treeKinds[0].wood) return treeKinds;
                treeKinds = new TreeMeshes.Tree[BroadleafKinds + PineKinds];
                for (int i = 0; i < BroadleafKinds; i++) treeKinds[i] = TreeMeshes.Broadleaf(seed * 31 + i);
                for (int i = 0; i < PineKinds; i++) treeKinds[BroadleafKinds + i] = TreeMeshes.Pine(seed * 57 + i);
                return treeKinds;
            }
        }

        void BuildWorld(GameObject root)
        {
            var world = NewObject("World", root.transform);
            BuildCountry(world);
            foreach (var road in WorldLayout.Roads) BuildRoad(world, road);
            BuildWater(world);
            BuildForest(world);
            BuildWorldEdge(world);
        }

        // ---- Terrain ----

        // Vertex colour: red = soil instead of grass (steep banks, river shores, verges of country
        // roads, forest floor), green = broad brightness variation, blue = sun-dried grass.
        void BuildCountry(GameObject parent)
        {
            float min = WorldLayout.Min, cell = worldCell;
            int n = Mathf.RoundToInt((WorldLayout.Max - min) / cell);
            var h = new float[(n + 1) * (n + 1)];
            for (int j = 0; j <= n; j++)
            for (int i = 0; i <= n; i++)
                h[j * (n + 1) + i] = WorldLayout.Height(min + i * cell, min + j * cell);

            int per = Mathf.CeilToInt(n / (float)worldChunks);
            for (int cj = 0; cj < worldChunks; cj++)
            for (int ci = 0; ci < worldChunks; ci++)
            {
                var verts = new List<Vector3>();
                var normals = new List<Vector3>();
                var colors = new List<Color>();
                var tris = new List<int>();
                var map = new Dictionary<int, int>();
                int i0 = ci * per, i1 = Mathf.Min(n, i0 + per), j0 = cj * per, j1 = Mathf.Min(n, j0 + per);
                for (int j = j0; j < j1; j++)
                for (int i = i0; i < i1; i++)
                {
                    float cx = min + (i + 0.5f) * cell, cz = min + (j + 0.5f) * cell;
                    // The city square has its own ground.
                    if (cx > CityLayout.Min && cx < CityLayout.Max && cz > CityLayout.Min && cz < CityLayout.Max) continue;
                    int a = V(i, j), b = V(i + 1, j), c = V(i + 1, j + 1), d = V(i, j + 1);
                    tris.Add(a); tris.Add(d); tris.Add(c);
                    tris.Add(a); tris.Add(c); tris.Add(b);
                }
                if (tris.Count == 0) continue;
                var m = new Mesh { indexFormat = IndexFormat.UInt32, hideFlags = HideFlags.DontSave };
                m.SetVertices(verts);
                m.SetNormals(normals);
                m.SetColors(colors);
                m.SetTriangles(tris, 0);
                m.RecalculateBounds();
                MeshObject("Country_" + ci + "_" + cj, parent, m, true, countryGrass ? countryGrass : grass).AddComponent<TrackSurface>();

                int V(int i, int j)
                {
                    int key = j * (n + 1) + i;
                    if (map.TryGetValue(key, out int v)) return v;
                    float x = min + i * cell, z = min + j * cell;
                    float hc = h[key];
                    float hl = h[j * (n + 1) + Mathf.Max(i - 1, 0)], hr = h[j * (n + 1) + Mathf.Min(i + 1, n)];
                    float hd = h[Mathf.Max(j - 1, 0) * (n + 1) + i], hu = h[Mathf.Min(j + 1, n) * (n + 1) + i];
                    var normal = new Vector3(hl - hr, 2f * cell, hd - hu).normalized;
                    verts.Add(new Vector3(x, hc, z));
                    normals.Add(normal);
                    float shade = Mathf.PerlinNoise(x / 140f + 3.1f, z / 140f + 7.7f) * 0.7f + Mathf.PerlinNoise(x / 23f, z / 23f) * 0.3f;
                    float dry = Mathf.InverseLerp(0.45f, 0.75f, Mathf.PerlinNoise(x / 330f + 5.3f, z / 330f + 9.1f));
                    colors.Add(new Color(SoilWeight(x, z, normal.y), shade, dry, 1f));
                    map[key] = verts.Count - 1;
                    return verts.Count - 1;
                }
            }
        }

        static float SoilWeight(float x, float z, float upness)
        {
            float soil = Mathf.InverseLerp(0.9f, 0.75f, upness);
            float shore = WorldLayout.WaterDistance(x, z, out _);
            soil = Mathf.Max(soil, Mathf.InverseLerp(9f, 2f, shore));
            float verge = WorldLayout.RoadClearance(x, z);
            soil = Mathf.Max(soil, Mathf.InverseLerp(3f, 0.5f, verge) * 0.85f);
            soil = Mathf.Max(soil, Mathf.InverseLerp(0.6f, 0.85f, ForestDensity(x, z)) * 0.55f);
            return soil;
        }

        static float ForestDensity(float x, float z) => Mathf.PerlinNoise(x / 260f + 11.3f, z / 260f + 4.7f);

        // ---- Roads ----

        void BuildRoad(GameObject parent, WorldLayout.Road road)
        {
            if (road.pad) { BuildPad(parent, road); return; }
            var go = NewObject(road.name, parent.transform);
            float hw = road.halfWidth;
            float lift = road.Lift;
            int segs = road.closed ? road.Count : road.Count - 1;
            bool Deck(int k) => road.elevated[k] && road.elevated[road.Wrap(k + 1)];

            // Surface: a ribbon with skirts that tuck under the ground at both edges, or a deck slab on bridges.
            var surf = new MeshBuilder(1);
            var deck = new MeshBuilder(1);
            for (int k = 0; k < segs; k++)
            {
                int a = k, b = road.Wrap(k + 1);
                Vector3 pa = road.pts[a] + Vector3.up * lift, pb = road.pts[b] + Vector3.up * lift;
                Vector3 ra = road.right[a], rb = road.right[b];
                float va = road.dist[a] / 4f, vb = va + Vector3.Distance(pa, pb) / 4f;
                QuadUV(surf, pa - ra * hw, pa + ra * hw, pb + rb * hw, pb - rb * hw, -hw / 4f, hw / 4f, va, vb);
                if (Deck(k))
                {
                    Vector3 down = Vector3.down * (WorldLayout.DeckDepth + lift);
                    float w = hw + 0.4f;
                    deck.Quad(0, pb - rb * w + down, pa - ra * w + down, pa - ra * w, pb - rb * w, 1f);
                    deck.Quad(0, pa + ra * w + down, pb + rb * w + down, pb + rb * w, pa + ra * w, 1f);
                    deck.Quad(0, pb - rb * w + down, pb + rb * w + down, pa + ra * w + down, pa - ra * w + down, 1f);
                }
                else
                {
                    Vector3 down = Vector3.down * 0.7f;
                    QuadUV(surf, pa - ra * (hw + 0.6f) + down, pa - ra * hw, pb - rb * hw, pb - rb * (hw + 0.6f) + down, -hw / 4f - 0.2f, -hw / 4f, va, vb);
                    QuadUV(surf, pa + ra * hw, pa + ra * (hw + 0.6f) + down, pb + rb * (hw + 0.6f) + down, pb + rb * hw, hw / 4f, hw / 4f + 0.2f, va, vb);
                }
            }
            MeshObject("Surface", go, surf.Build(), true, this.road);
            MeshObject("Deck", go, deck.Build(), false, barrier);

            // Markings.
            var white = new MeshBuilder(1);
            var yellow = new MeshBuilder(1);
            if (road.highway)
            {
                float m = WorldLayout.MedianHalf, lw = WorldLayout.LaneWidth;
                float outer = m + lw * WorldLayout.HighwayLanes;
                foreach (float s in new[] { -1f, 1f })
                {
                    RoadStripe(white, road, s * (m + 0.2f), s * (m + 0.35f), 0f, 1f, lift);
                    RoadStripe(white, road, s * (outer - 0.35f), s * (outer - 0.2f), 0f, 1f, lift);
                    for (int l = 1; l < WorldLayout.HighwayLanes; l++)
                        RoadStripe(white, road, s * (m + lw * l - 0.08f), s * (m + lw * l + 0.08f), 4f, 12f, lift);
                }
            }
            else if (road.ramp)
            {
                RoadStripe(white, road, -hw + 0.45f, -hw + 0.6f, 0f, 1f, lift);
                RoadStripe(white, road, hw - 0.6f, hw - 0.45f, 0f, 1f, lift);
            }
            else
            {
                RoadStripe(yellow, road, -0.3f, -0.12f, 0f, 1f, lift);
                RoadStripe(yellow, road, 0.12f, 0.3f, 0f, 1f, lift);
                RoadStripe(white, road, -3.65f, -3.5f, 0f, 1f, lift);
                RoadStripe(white, road, 3.5f, 3.65f, 0f, 1f, lift);
            }
            MeshObject("Markings", go, white.Build(), false, line);
            MeshObject("CentreLines", go, yellow.Build(), false, yellowLine);

            BuildEdges(go, road);
            BuildPiers(go, road);
            if (road.highway) BuildHighwayFurniture(go, road);
        }

        // Round paved pad at a ramp terminal: disc, deck rim and underside, parapet ring open where
        // roads come in, and a central pier.
        void BuildPad(GameObject parent, WorldLayout.Road road)
        {
            var go = NewObject(road.name, parent.transform);
            Vector3 c = (road.pts[0] + road.pts[1]) * 0.5f;
            float r = road.halfWidth, lift = road.Lift;
            bool deck = road.elevated[0];
            const int sides = 40;
            var surf = new MeshBuilder(1);
            var under = new MeshBuilder(1);
            var parapets = new MeshBuilder(1);
            Vector3 top = c + Vector3.up * lift;
            Vector3 down = Vector3.down * (WorldLayout.DeckDepth + lift);
            for (int k = 0; k < sides; k++)
            {
                float a0 = k * Mathf.PI * 2f / sides, a1 = (k + 1) * Mathf.PI * 2f / sides;
                Vector3 d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                Vector3 e0 = top + d0 * r, e1 = top + d1 * r;
                surf.QuadUV(0, top, e0, e1, top, Vector2.zero, new Vector2(d0.x, d0.z) * r / 4f, new Vector2(d1.x, d1.z) * r / 4f, Vector2.zero);
                if (deck)
                {
                    under.Quad(0, e0 + down, e1 + down, e1, e0, 1f);
                    under.Quad(0, top + down, e1 + down, e0 + down, top + down, 1f);
                }
                else
                {
                    var sink = Vector3.down * 0.7f;
                    surf.Quad(0, e0 + d0 * 0.6f + sink, e1 + d1 * 0.6f + sink, e1, e0, 1f);
                }
            }
            // Parapet ring in 2 degree pieces, open where another road's paving (inside its own walls) meets the rim.
            if (deck)
            {
                const int steps = 180;
                for (int k = 0; k < steps; k++)
                {
                    float a0 = k * Mathf.PI * 2f / steps, a1 = (k + 1) * Mathf.PI * 2f / steps;
                    Vector3 d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                    Vector3 mid = c + (d0 + d1).normalized * (r - 0.3f);
                    if (WorldLayout.OnOtherPaving(road, mid, -0.3f, 1.5f)) continue;
                    Barrier(parapets, c + d0 * (r - 0.3f), c + d1 * (r - 0.3f), d0, d1, 0f, 0.22f, 1.05f);
                }
            }
            MeshObject("Surface", go, surf.Build(), true, this.road);
            MeshObject("Deck", go, under.Build(), false, barrier);
            MeshObject("Parapets", go, parapets.Build(), true, barrier);
            if (deck && !WorldLayout.OverLowerPaving(road, c, 2f))
            {
                float ground = WorldLayout.Height(c.x, c.z) - 1f, topY = c.y - WorldLayout.DeckDepth - lift;
                if (topY - ground > 0.5f)
                {
                    var parts = new List<CombineInstance> { Inst(PrimitiveMesh(PrimitiveType.Cylinder), new Vector3(c.x, (topY + ground) * 0.5f, c.z), Quaternion.identity, new Vector3(2.4f, (topY - ground) * 0.5f, 2.4f)) };
                    MeshObject("Pier", go, Combine(parts), true, barrier);
                }
            }
        }

        // Edges: concrete parapets on bridge decks, steel guard rails along the highway and ramps at
        // ground level; both open wherever the edge runs onto another road (ramp mouths, terminals).
        void BuildEdges(GameObject go, WorldLayout.Road road)
        {
            var parapets = new MeshBuilder(1);
            var rails = new MeshBuilder(1);
            int segs = road.closed ? road.Count : road.Count - 1;
            float edge = road.halfWidth - 0.3f;
            bool groundRails = road.highway || road.ramp;
            for (int k = 0; k < segs; k++)
            {
                int a = k, b = road.Wrap(k + 1);
                Vector3 pa = road.pts[a], pb = road.pts[b], ra = road.right[a], rb = road.right[b];
                bool deck = road.elevated[a] || road.elevated[b];
                if (!deck && !groundRails) continue;
                // Where a parapet starts or ends at a deck's end it flares out, so it deflects rather than stops.
                bool flareA = deck && !road.elevated[road.Wrap(k - 1)] && (road.closed || k > 0);
                bool flareB = deck && !road.elevated[road.Wrap(k + 2)] && (road.closed || k < segs - 1);
                foreach (float s in new[] { -1f, 1f })
                {
                    float oa = edge - 0.05f + (deck && flareA ? 1.2f : 0f), ob = edge - 0.05f + (deck && flareB ? 1.2f : 0f);
                    Vector3 wa = pa + ra * s * oa, wb = pb + rb * s * ob;
                    // Walls stop exactly at a terminal pad's rim, where the pad's own parapet takes over.
                    if (!ClipToPads(ref wa, ref wb)) continue;
                    Vector3 mid = (wa + wb) * 0.5f;
                    if (WorldLayout.OnOtherPaving(road, mid, 1.2f, 2.5f, -1000f)) continue;
                    if (deck) BarrierFlared(parapets, wa, wb, ra * s, rb * s, 0f, 0f, 0.22f, 1.05f);
                    else Barrier(rails, wa, wb, ra, rb, 0f, 0.08f, 0.8f);
                }
            }
            // A deck that just stops (end of the flyover past the outer terminal) gets an end wall.
            if (!road.closed)
            {
                foreach (int end in new[] { 0, road.Count - 1 })
                {
                    if (!road.elevated[end]) continue;
                    int inner = end == 0 ? 1 : road.Count - 2;
                    Vector3 p = road.pts[end], t = (p - road.pts[inner]).normalized;
                    if (WorldLayout.OnOtherPaving(road, p + t * 1.5f, 0f)) continue;
                    Vector3 fwd = new Vector3(t.x, 0f, t.z).normalized;
                    Barrier(parapets, p + road.right[end] * road.halfWidth, p - road.right[end] * road.halfWidth, fwd, fwd, -0.2f, 0.22f, 1.05f);
                }
            }
            MeshObject("Parapets", go, parapets.Build(), true, barrier);
            MeshObject("GuardRails", go, rails.Build(), true, lampPole);
        }

        // Bridge piers every ~28 m under decks: two columns and a crossbeam under the highway, one
        // column with a cap under the narrower roads. Never on another road's paving below.
        void BuildPiers(GameObject go, WorldLayout.Road road)
        {
            var cyl = PrimitiveMesh(PrimitiveType.Cylinder);
            var cube = PrimitiveMesh(PrimitiveType.Cube);
            var parts = new List<CombineInstance>();
            float under = WorldLayout.DeckDepth + road.Lift;
            for (int k = 0; k < road.Count; k += 7)
            {
                if (!road.elevated[k] || !road.elevated[road.Wrap(k - 2)] || !road.elevated[road.Wrap(k + 2)]) continue;
                Vector3 p = road.pts[k], r = road.right[k];
                var face = Quaternion.LookRotation(r);
                var cols = road.highway ? new[] { -WorldLayout.CarriageCentre, WorldLayout.CarriageCentre } : new[] { 0f };
                float radius = road.highway ? 1.1f : 0.85f;
                float capH = road.highway ? 1.2f : 0.9f;
                bool any = false;
                foreach (float o in cols)
                {
                    Vector3 c = p + r * o;
                    if (WorldLayout.OverLowerPaving(road, c, radius + 1f)) continue;
                    float ground = WorldLayout.Height(c.x, c.z) - 1f;
                    float top = p.y - under - capH;
                    if (top - ground < 0.5f) continue;
                    parts.Add(Inst(cyl, new Vector3(c.x, (top + ground) * 0.5f, c.z), Quaternion.identity, new Vector3(radius * 2f, (top - ground) * 0.5f, radius * 2f)));
                    any = true;
                }
                if (any)
                    parts.Add(Inst(cube, p + Vector3.down * (under + capH * 0.5f), face, new Vector3(1.6f, capH, (road.halfWidth - 0.6f) * 2f)));
            }
            if (parts.Count > 0) MeshObject("Piers", go, Combine(parts), true, barrier);
        }

        // Highway: concrete barrier and lamps down the middle (no lamps under the flyovers).
        void BuildHighwayFurniture(GameObject go, WorldLayout.Road road)
        {
            bool NearJunction(int i, float metres)
            {
                foreach (int j in WorldLayout.Junctions)
                {
                    int d = Mathf.Abs(i - j);
                    d = Mathf.Min(d, road.Count - d);
                    if (d * 4f < metres) return true;
                }
                return false;
            }

            var median = new MeshBuilder(1);
            var cyl = PrimitiveMesh(PrimitiveType.Cylinder);
            var cube = PrimitiveMesh(PrimitiveType.Cube);
            var poles = new List<CombineInstance>();
            var heads = new List<CombineInstance>();
            for (int k = 0; k < road.Count; k++)
            {
                int a = k, b = road.Wrap(k + 1);
                Vector3 pa = road.pts[a], pb = road.pts[b], ra = road.right[a], rb = road.right[b];
                Barrier(median, pa, pb, ra, rb, 0f, 0.3f, 0.9f);

                if (k % 15 == 0 && !NearJunction(k, 40f))
                {
                    Vector3 basePos = pa + Vector3.up * 0.9f;
                    poles.Add(Inst(cyl, basePos + Vector3.up * 5f, Quaternion.identity, new Vector3(0.25f, 5f, 0.25f)));
                    foreach (float s in new[] { -1f, 1f })
                    {
                        var face = Quaternion.LookRotation(ra * s);
                        poles.Add(Inst(cube, basePos + Vector3.up * 9.9f + ra * s * 1.6f, face, new Vector3(0.14f, 0.14f, 3.2f)));
                        heads.Add(Inst(cube, basePos + Vector3.up * 9.75f + ra * s * 3.1f, face, new Vector3(0.5f, 0.18f, 1f)));
                    }
                }
            }
            MeshObject("MedianBarrier", go, median.Build(), true, barrier);
            MeshObject("LampPoles", go, Combine(poles), false, lampPole).layer = cityLayer;
            MeshObject("LampHeads", go, Combine(heads), false, lampHead).layer = cityLayer;
        }

        // Trims the wall line a-b to the part outside every terminal pad's parapet circle; false if none is left.
        static bool ClipToPads(ref Vector3 a, ref Vector3 b)
        {
            foreach (var pad in WorldLayout.Pads)
            {
                Vector3 c = (pad.pts[0] + pad.pts[1]) * 0.5f;
                float r = pad.halfWidth - 0.3f;
                Vector2 A = new Vector2(a.x - c.x, a.z - c.z), B = new Vector2(b.x - c.x, b.z - c.z);
                bool inA = A.sqrMagnitude < r * r, inB = B.sqrMagnitude < r * r;
                if (inA && inB) return false;
                if (!inA && !inB) continue;
                // Circle crossing of the segment: |A + t (B - A)| = r.
                Vector2 d = B - A;
                float qa = Vector2.Dot(d, d), qb = 2f * Vector2.Dot(A, d), qc = A.sqrMagnitude - r * r;
                float disc = Mathf.Sqrt(Mathf.Max(qb * qb - 4f * qa * qc, 0f));
                float t = inA ? (-qb + disc) / (2f * qa) : (-qb - disc) / (2f * qa);
                Vector3 hit = Vector3.Lerp(a, b, Mathf.Clamp01(t));
                if (inA) a = hit; else b = hit;
            }
            return (b - a).sqrMagnitude > 0.01f;
        }

        // Wall whose lateral offset runs from offA at pa to offB at pb (ra/rb already point to its side).
        static void BarrierFlared(MeshBuilder mb, Vector3 pa, Vector3 pb, Vector3 ra, Vector3 rb, float offA, float offB, float half, float height)
        {
            Vector3 a0 = pa + ra * (offA - half), a1 = pa + ra * (offA + half), b0 = pb + rb * (offB - half), b1 = pb + rb * (offB + half);
            // Keep the faces outward-facing whichever side the wall is on.
            if (Vector3.Dot(Vector3.Cross(pb - pa, ra), Vector3.up) < 0f) { (a0, a1) = (a1, a0); (b0, b1) = (b1, b0); }
            Vector3 up = Vector3.up * height, sink = Vector3.down * 0.3f;
            mb.Quad(0, b0 + sink, a0 + sink, a0 + up, b0 + up, 1f);
            mb.Quad(0, a1 + sink, b1 + sink, b1 + up, a1 + up, 1f);
            mb.Quad(0, a0 + up, a1 + up, b1 + up, b0 + up, 0.2f);
        }

        // Upright wall segment (both faces + top) at lateral offset 'off', of the given half thickness and height.
        static void Barrier(MeshBuilder mb, Vector3 pa, Vector3 pb, Vector3 ra, Vector3 rb, float off, float half, float height)
        {
            Vector3 a0 = pa + ra * (off - half), a1 = pa + ra * (off + half), b0 = pb + rb * (off - half), b1 = pb + rb * (off + half);
            Vector3 up = Vector3.up * height, sink = Vector3.down * 0.3f;
            mb.Quad(0, b0 + sink, a0 + sink, a0 + up, b0 + up, 1f);
            mb.Quad(0, a1 + sink, b1 + sink, b1 + up, a1 + up, 1f);
            mb.Quad(0, a0 + up, a1 + up, b1 + up, b0 + up, 0.2f);
        }

        // Painted strip between lateral offsets a..b; dashed when dash > 0 (dash metres painted every gap metres).
        void RoadStripe(MeshBuilder mb, WorldLayout.Road road, float a, float b, float dash, float gap, float lift)
        {
            int count = road.closed ? road.Count : road.Count - 1;
            for (int k = 0; k < count; k++)
            {
                int i = k, j = road.Wrap(k + 1);
                if (dash > 0f && road.dist[i] % (dash + gap) > dash) continue;
                Vector3 pi = road.pts[i] + Vector3.up * (lift + 0.03f), pj = road.pts[j] + Vector3.up * (lift + 0.03f);
                Vector3 ri = road.right[i], rj = road.right[j];
                // Lines stop where they would run across another road's paving (ramp mouths).
                if (!road.highway && WorldLayout.OnOtherPaving(road, (pi + pj) * 0.5f + (ri + rj) * 0.25f * (a + b), -0.2f, 1f)) continue;
                mb.Quad(0, pi + ri * a, pi + ri * b, pj + rj * b, pj + rj * a, 1f);
            }
        }

        static void QuadUV(MeshBuilder mb, Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3, float u0, float u1, float va, float vb)
        {
            mb.QuadUV(0, v0, v1, v2, v3, new Vector2(u0, va), new Vector2(u1, va), new Vector2(u1, vb), new Vector2(u0, vb));
        }

        // ---- River and lake ----

        void BuildWater(GameObject parent)
        {
            var mb = new MeshBuilder(1);
            var pts = WorldLayout.RiverPts;
            var level = WorldLayout.RiverLevel;
            float half = WorldLayout.RiverHalf + 7f;
            for (int i = 0; i < pts.Length - 1; i++)
            {
                Vector2 d0 = (pts[Mathf.Min(i + 1, pts.Length - 1)] - pts[Mathf.Max(i - 1, 0)]).normalized;
                Vector2 d1 = (pts[Mathf.Min(i + 2, pts.Length - 1)] - pts[i]).normalized;
                Vector3 r0 = new Vector3(d0.y, 0f, -d0.x) * half, r1 = new Vector3(d1.y, 0f, -d1.x) * half;
                Vector3 a = new Vector3(pts[i].x, level[i], pts[i].y), b = new Vector3(pts[i + 1].x, level[i + 1], pts[i + 1].y);
                mb.Quad(0, a - r0, a + r0, b + r1, b - r1, 1f);
            }
            Vector3 c = new Vector3(WorldLayout.LakeCentre.x, WorldLayout.LakeLevel, WorldLayout.LakeCentre.y);
            float rad = WorldLayout.LakeRadius + 9f;
            const int sides = 72;
            for (int k = 0; k < sides; k++)
            {
                float a0 = k * Mathf.PI * 2f / sides, a1 = (k + 1) * Mathf.PI * 2f / sides;
                Vector3 e0 = c + new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * rad, e1 = c + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * rad;
                mb.Quad(0, c, e0, e1, c, 1f);
            }
            var go = MeshObject("Water", parent, mb.Build(), false, water ? water : barrier);
            go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        }

        // ---- Forests ----

        // Broadleaf and pine trees in noisy clumps, kept off the roads, the water and out of the city;
        // drawn instanced by a ForestRenderer, with plain capsule colliders on the trunks.
        void BuildForest(GameObject parent)
        {
            var kinds = TreeKinds;
            var rng = new System.Random(seed + 5);
            float R() => (float)rng.NextDouble();
            var go = NewObject("Forest", parent.transform);
            var forest = go.AddComponent<ForestRenderer>();
            forest.layer = cityLayer;
            forest.Init(kinds, bark ? bark : trunk, foliageBroadleaf ? foliageBroadleaf : leaves, foliagePine ? foliagePine : leaves);
            int chunks = Mathf.CeilToInt((WorldLayout.Max - WorldLayout.Min) / forestChunk);
            var colliders = new GameObject[chunks * chunks];
            for (float z = WorldLayout.Min + 20f; z < WorldLayout.Max - 20f; z += treeSpacing)
            for (float x = WorldLayout.Min + 20f; x < WorldLayout.Max - 20f; x += treeSpacing)
            {
                float px = x + (R() - 0.5f) * treeSpacing, pz = z + (R() - 0.5f) * treeSpacing;
                if (R() > Mathf.InverseLerp(0.48f, 0.72f, ForestDensity(px, pz)) + 0.012f) continue; // plus a few lone trees
                if (px > CityLayout.Min - 30f && px < CityLayout.Max + 30f && pz > CityLayout.Min - 30f && pz < CityLayout.Max + 30f) continue;
                if (WorldLayout.RoadClearance(px, pz) < WorldLayout.Verge + 2f) continue;
                if (WorldLayout.WaterDistance(px, pz, out _) < 8f) continue;
                float y = WorldLayout.Height(px, pz);
                int ci = Mathf.Clamp(Mathf.FloorToInt((px - WorldLayout.Min) / forestChunk), 0, chunks - 1);
                int cj = Mathf.Clamp(Mathf.FloorToInt((pz - WorldLayout.Min) / forestChunk), 0, chunks - 1);
                int c = cj * chunks + ci;
                bool pine = Mathf.PerlinNoise(px / 400f, pz / 400f) + R() * 0.3f > 0.6f;
                int kind = pine ? BroadleafKinds + rng.Next(PineKinds) : rng.Next(BroadleafKinds);
                float s = 0.75f + R() * 0.55f;
                var rot = Quaternion.Euler(0f, R() * 360f, 0f);
                Vector3 p = new Vector3(px, y - 0.1f, pz);
                var scale = new Vector3(s, s * (0.9f + R() * 0.2f), s);
                Vector3 centre = new Vector3(WorldLayout.Min + (ci + 0.5f) * forestChunk, y, WorldLayout.Min + (cj + 0.5f) * forestChunk);
                forest.Add(c, centre, forestChunk, kind, Matrix4x4.TRS(p, rot, scale));
                if (!colliders[c]) colliders[c] = NewObject("TreeColliders_" + c, go.transform);
                var col = colliders[c].AddComponent<CapsuleCollider>();
                col.center = p + Vector3.up * 2.5f;
                col.radius = 0.35f * s;
                col.height = 5f;
            }
            if (!Application.isPlaying) return;
            Debug.Log("[Racing] forest trees: " + forest.TreeCount);
        }

        // ---- Edge of the world ----

        void BuildWorldEdge(GameObject parent)
        {
            var mb = new MeshBuilder(1);
            float min = WorldLayout.Min + 4f, max = WorldLayout.Max - 4f;
            const float step = 16f, height = 4f;
            for (int side = 0; side < 4; side++)
            for (float t = min; t < max - 0.01f; t += step)
            {
                float t1 = Mathf.Min(t + step, max);
                Vector3 a, b;
                switch (side)
                {
                    case 0: a = new Vector3(t1, 0f, min); b = new Vector3(t, 0f, min); break;
                    case 1: a = new Vector3(t, 0f, max); b = new Vector3(t1, 0f, max); break;
                    case 2: a = new Vector3(min, 0f, t); b = new Vector3(min, 0f, t1); break;
                    default: a = new Vector3(max, 0f, t1); b = new Vector3(max, 0f, t); break;
                }
                Vector3 at = new Vector3(a.x, WorldLayout.Height(a.x, a.z) + height, a.z), bt = new Vector3(b.x, WorldLayout.Height(b.x, b.z) + height, b.z);
                Vector3 ab = new Vector3(a.x, WorldLayout.Height(a.x, a.z) - 3f, a.z), bb = new Vector3(b.x, WorldLayout.Height(b.x, b.z) - 3f, b.z);
                mb.Quad(0, ab, bb, bt, at, step / 4f);
                mb.Quad(0, bb, ab, at, bt, step / 4f);
            }
            MeshObject("WorldEdge", parent, mb.Build(), true, barrier);
        }
    }
}
