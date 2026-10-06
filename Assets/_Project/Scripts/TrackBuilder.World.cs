using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Racing
{
    // The countryside around the city (WorldLayout): terrain chunks, highway and national road
    // surfaces with markings, highway barriers, guard rails and lamps, forests and the world edge.
    public partial class TrackBuilder
    {
        [Header("World (countryside, highway, national roads)")]
        public float worldCell = 8f;
        public int worldChunks = 8;
        public float treeSpacing = 20f;
        public Material countryGrass;

        void BuildWorld(GameObject root)
        {
            var world = NewObject("World", root.transform);
            BuildCountry(world);
            foreach (var road in WorldLayout.Roads) BuildRoad(world, road);
            BuildForest(world);
            BuildWorldEdge(world);
        }

        // ---- Terrain ----

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
                var uvs = new List<Vector2>();
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
                m.SetUVs(0, uvs);
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
                    verts.Add(new Vector3(x, hc, z));
                    normals.Add(new Vector3(hl - hr, 2f * cell, hd - hu).normalized);
                    uvs.Add(new Vector2(x / 6f, z / 6f));
                    map[key] = verts.Count - 1;
                    return verts.Count - 1;
                }
            }
        }

        // ---- Roads ----

        void BuildRoad(GameObject parent, WorldLayout.Road road)
        {
            var go = NewObject(road.name, parent.transform);
            float hw = road.halfWidth;
            float lift = road.highway ? 0.015f : 0f;
            int count = road.closed ? road.Count + 1 : road.Count;

            // Surface: a flat ribbon with skirts that tuck under the ground at both edges.
            var surf = new MeshBuilder(1);
            for (int k = 0; k < count - 1; k++)
            {
                int a = road.Wrap(k), b = road.Wrap(k + 1);
                Vector3 pa = road.pts[a] + Vector3.up * lift, pb = road.pts[b] + Vector3.up * lift;
                Vector3 ra = road.right[a], rb = road.right[b];
                float va = road.dist[a] / 4f, vb = va + Vector3.Distance(pa, pb) / 4f;
                QuadUV(surf, pa - ra * hw, pa + ra * hw, pb + rb * hw, pb - rb * hw, -hw / 4f, hw / 4f, va, vb);
                // Skirts.
                Vector3 down = Vector3.down * 0.7f;
                QuadUV(surf, pa - ra * (hw + 0.6f) + down, pa - ra * hw, pb - rb * hw, pb - rb * (hw + 0.6f) + down, -hw / 4f - 0.2f, -hw / 4f, va, vb);
                QuadUV(surf, pa + ra * hw, pa + ra * (hw + 0.6f) + down, pb + rb * (hw + 0.6f) + down, pb + rb * hw, hw / 4f, hw / 4f + 0.2f, va, vb);
            }
            MeshObject("Surface", go, surf.Build(), true, this.road);

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
            else
            {
                RoadStripe(yellow, road, -0.3f, -0.12f, 0f, 1f, lift);
                RoadStripe(yellow, road, 0.12f, 0.3f, 0f, 1f, lift);
                RoadStripe(white, road, -3.65f, -3.5f, 0f, 1f, lift);
                RoadStripe(white, road, 3.5f, 3.65f, 0f, 1f, lift);
            }
            MeshObject("Markings", go, white.Build(), false, line);
            MeshObject("CentreLines", go, yellow.Build(), false, yellowLine);

            if (road.highway) BuildHighwayFurniture(go, road);
        }

        // Highway: concrete barrier and lamps down the middle, guard rails on both edges; all with
        // gaps where the national roads join.
        void BuildHighwayFurniture(GameObject go, WorldLayout.Road road)
        {
            bool Gap(int i, float metres)
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
            var rails = new MeshBuilder(1);
            var cyl = PrimitiveMesh(PrimitiveType.Cylinder);
            var cube = PrimitiveMesh(PrimitiveType.Cube);
            var poles = new List<CombineInstance>();
            var heads = new List<CombineInstance>();
            float edge = WorldLayout.HighwayHalf - 0.4f;
            for (int k = 0; k < road.Count; k++)
            {
                int a = k, b = road.Wrap(k + 1);
                Vector3 pa = road.pts[a], pb = road.pts[b], ra = road.right[a], rb = road.right[b];
                if (!Gap(k, 45f)) Barrier(median, pa, pb, ra, rb, 0f, 0.3f, 0.9f);
                // Guard rails: the outer one is continuous, the inner one opens at junctions.
                Barrier(rails, pa, pb, ra, rb, edge, 0.08f, 0.8f);
                if (!Gap(k, 40f)) Barrier(rails, pa, pb, ra, rb, -edge, 0.08f, 0.8f);

                if (k % 15 == 0 && !Gap(k, 50f))
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
            MeshObject("GuardRails", go, rails.Build(), true, lampPole);
            MeshObject("LampPoles", go, Combine(poles), false, lampPole).layer = cityLayer;
            MeshObject("LampHeads", go, Combine(heads), false, lampHead).layer = cityLayer;
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
                mb.Quad(0, pi + ri * a, pi + ri * b, pj + rj * b, pj + rj * a, 1f);
            }
        }

        static void QuadUV(MeshBuilder mb, Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3, float u0, float u1, float va, float vb)
        {
            mb.QuadUV(0, v0, v1, v2, v3, new Vector2(u0, va), new Vector2(u1, va), new Vector2(u1, vb), new Vector2(u0, vb));
        }

        // ---- Forests ----

        // Low-poly pines and broadleaf trees in noisy clumps, kept off the roads and out of the city.
        void BuildForest(GameObject parent)
        {
            var rng = new System.Random(seed + 5);
            float R() => (float)rng.NextDouble();
            var trunkMesh = Prism(6, 0.22f, 0.16f, 1f);
            var pineMesh = Cone(7, 1f, 1f);
            var leafMesh = Blob();
            int chunks = worldChunks;
            var trunks = new List<CombineInstance>[chunks * chunks];
            var crowns = new List<CombineInstance>[chunks * chunks];
            var colliders = new GameObject[chunks * chunks];
            float span = (WorldLayout.Max - WorldLayout.Min) / chunks;
            for (float z = WorldLayout.Min + 20f; z < WorldLayout.Max - 20f; z += treeSpacing)
            for (float x = WorldLayout.Min + 20f; x < WorldLayout.Max - 20f; x += treeSpacing)
            {
                float px = x + (R() - 0.5f) * treeSpacing, pz = z + (R() - 0.5f) * treeSpacing;
                float density = Mathf.PerlinNoise(px / 260f + 11.3f, pz / 260f + 4.7f);
                if (R() > Mathf.InverseLerp(0.5f, 0.8f, density)) continue;
                if (px > CityLayout.Min - 30f && px < CityLayout.Max + 30f && pz > CityLayout.Min - 30f && pz < CityLayout.Max + 30f) continue;
                if (WorldLayout.RoadClearance(px, pz) < WorldLayout.Verge + 2f) continue;
                float y = WorldLayout.Height(px, pz);
                int ci = Mathf.Clamp(Mathf.FloorToInt((px - WorldLayout.Min) / span), 0, chunks - 1);
                int cj = Mathf.Clamp(Mathf.FloorToInt((pz - WorldLayout.Min) / span), 0, chunks - 1);
                int c = cj * chunks + ci;
                trunks[c] ??= new List<CombineInstance>();
                crowns[c] ??= new List<CombineInstance>();
                float s = 0.8f + R() * 0.7f;
                bool pine = Mathf.PerlinNoise(px / 400f, pz / 400f) + R() * 0.3f > 0.6f;
                var rot = Quaternion.Euler(0f, R() * 360f, 0f);
                Vector3 p = new Vector3(px, y - 0.3f, pz);
                trunks[c].Add(Inst(trunkMesh, p, rot, new Vector3(s, (pine ? 3f : 2.6f) * s, s)));
                if (pine)
                    crowns[c].Add(Inst(pineMesh, p + Vector3.up * 2f * s, rot, new Vector3(2.4f, 7.5f, 2.4f) * s));
                else
                    crowns[c].Add(Inst(leafMesh, p + Vector3.up * 4.2f * s, rot, new Vector3(3.2f, 3f, 3.2f) * s));
                if (!colliders[c]) colliders[c] = NewObject("TreeColliders_" + c, parent.transform);
                var col = colliders[c].AddComponent<CapsuleCollider>();
                col.center = p + Vector3.up * 2.5f;
                col.radius = 0.35f * s;
                col.height = 5f;
            }
            for (int c = 0; c < trunks.Length; c++)
            {
                if (trunks[c] == null) continue;
                MeshObject("Trunks_" + c, parent, Combine(trunks[c]), false, trunk).layer = cityLayer;
                MeshObject("Crowns_" + c, parent, Combine(crowns[c]), false, leaves).layer = cityLayer;
            }
        }

        static Mesh Prism(int sides, float r0, float r1, float height)
        {
            var mb = new MeshBuilder(1);
            for (int k = 0; k < sides; k++)
            {
                float a0 = k * Mathf.PI * 2f / sides, a1 = (k + 1) * Mathf.PI * 2f / sides;
                Vector3 d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                mb.Quad(0, d1 * r0, d0 * r0, d0 * r1 + Vector3.up * height, d1 * r1 + Vector3.up * height, 1f);
            }
            return mb.Build();
        }

        static Mesh Cone(int sides, float radius, float height)
        {
            var mb = new MeshBuilder(1);
            Vector3 tip = Vector3.up * height;
            for (int k = 0; k < sides; k++)
            {
                float a0 = k * Mathf.PI * 2f / sides, a1 = (k + 1) * Mathf.PI * 2f / sides;
                Vector3 d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * radius, d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * radius;
                mb.Quad(0, d1, d0, tip, tip, 1f);
            }
            return mb.Build();
        }

        // Rough rounded crown: an octahedron-ish blob with its equator split into 8.
        static Mesh Blob()
        {
            var mb = new MeshBuilder(1);
            Vector3 top = Vector3.up, bottom = Vector3.down * 0.6f;
            for (int k = 0; k < 8; k++)
            {
                float a0 = k * Mathf.PI / 4f, a1 = (k + 1) * Mathf.PI / 4f;
                Vector3 e0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)), e1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                Vector3 m0 = e0 * 0.75f + Vector3.up * 0.6f, m1 = e1 * 0.75f + Vector3.up * 0.6f;
                mb.Quad(0, e1, e0, m0, m1, 1f);
                mb.Quad(0, m1, m0, top, top, 1f);
                mb.Quad(0, e0, e1, bottom, bottom, 1f);
            }
            return mb.Build();
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
