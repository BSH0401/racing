using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Racing
{
    // Generates the open city described by CityLayout: hilly terrain with a street grid, curbs,
    // lane markings, crosswalks, city blocks with buildings or parks, street lamps, a boundary wall
    // and the start gantry on the race route (TrackPath).
    // Everything is rebuilt on enable (edit and play mode) and never saved into the scene.
    [ExecuteAlways, RequireComponent(typeof(TrackPath))]
    public class TrackBuilder : MonoBehaviour
    {
        public Material road, line, yellowLine, sidewalk, curbs, grass, barrier, farGround, checkerBlack, gantry, banner, lampPole, lampHead, trunk, leaves;
        [Header("Buildings: boxes wrapped in photographed facades")]
        public Material[] facadeMaterials = new Material[0];
        [Tooltip("Metres covered by one repeat of each facade texture (same order as facadeMaterials).")]
        public float[] facadeTiles = new float[0];
        public Material roof;
        public float lotSize = 30f;

        [Header("Street props (Poly Haven)")]
        public GameObject hydrant, trashCan, roadBarrier;
        public Material hydrantMaterial, trashCanMaterial, roadBarrierMaterial;
        public float cell = 2f;
        public int seed = 7;
        public int cityLayer = 30;

        const string RootName = "_Generated";

        // World positions of lamp heads along the race route (filled by Build), used for night lighting.
        public readonly List<Vector3> LampLightPositions = new List<Vector3>();

        static float H(float x, float z) => CityLayout.Height(x, z);

        void OnEnable() => Build();

        public void Build()
        {
            var path = GetComponent<TrackPath>();
            if (path.controlPoints == null || path.controlPoints.Length < 4) return;
            path.Rebuild();

            var old = transform.Find(RootName);
            if (old) DestroyGenerated(old.gameObject);
            var root = NewObject(RootName, transform);

            BuildTerrain(root);
            BuildCurbs(root);
            BuildMarkings(root);
            BuildBoundary(root);
            BuildBlocks(root);
            BuildLamps(path, root);
            BuildStart(path, root);
            BuildProps(path, root);

            SetFlags(root);
        }

        // ---- Ground ----

        enum Surface { Road, Walk, Grass }

        Surface Classify(float x, float z)
        {
            if (CityLayout.IsRoad(x, z)) return Surface.Road;
            if (CityLayout.IsSidewalk(x, z)) return Surface.Walk;
            return CityLayout.IsPark(CityLayout.BlockIndex(x), CityLayout.BlockIndex(z)) ? Surface.Grass : Surface.Walk;
        }

        void BuildTerrain(GameObject root)
        {
            float min = CityLayout.Min, max = CityLayout.Max;
            int n = Mathf.CeilToInt((max - min) / cell);
            var grids = new[] { new GridMesh(n, 0f, 4f), new GridMesh(n, CityLayout.CurbHeight, 3f), new GridMesh(n, CityLayout.CurbHeight, 3f) };
            for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                float cx = min + (i + 0.5f) * cell, cz = min + (j + 0.5f) * cell;
                grids[(int)Classify(cx, cz)].AddCell(i, j, min, cell);
            }
            MeshObject("Roads", root, grids[0].Build(), true, road);
            MeshObject("Sidewalks", root, grids[1].Build(), true, sidewalk);
            MeshObject("Parks", root, grids[2].Build(), true, grass).AddComponent<TrackSurface>();

            // Distant flat ground below the plateau, seen past the boundary wall.
            var mb = new MeshBuilder(1);
            float size = 5000f, c = CityLayout.Size * 0.5f;
            Vector3 o = new Vector3(c, -10f, c), hx = new Vector3(size * 0.5f, 0f, 0f), hz = new Vector3(0f, 0f, size * 0.5f);
            mb.Quad(0, o - hx - hz, o + hx - hz, o + hx + hz, o - hx + hz, size / 8f, 0f, size / 8f);
            MeshObject("FarGround", root, mb.Build(), false, farGround);
        }

        // Vertical curb faces between the road surface and the raised sidewalks.
        void BuildCurbs(GameObject root)
        {
            var mb = new MeshBuilder(1);
            float p = CityLayout.Pitch, r = CityLayout.RoadHalf, size = CityLayout.Size;
            int lines = CityLayout.Lines;
            for (int axis = 0; axis < 2; axis++)
            for (int k = 0; k < lines; k++)
            for (int s = -1; s <= 1; s += 2)
            {
                float edge = k * p + s * r;
                bool outer = (k == 0 && s < 0) || (k == lines - 1 && s > 0);
                if (outer) Curb(mb, axis, edge, -r, size + r);
                else for (int j = 0; j < lines - 1; j++) Curb(mb, axis, edge, j * p + r, (j + 1) * p - r);
            }
            MeshObject("Curbs", root, mb.Build(), true, curbs ? curbs : sidewalk);
        }

        void Curb(MeshBuilder mb, int axis, float edge, float from, float to)
        {
            for (float t = from; t < to - 0.01f; t += cell)
            {
                float t1 = Mathf.Min(t + cell, to);
                Vector3 a = Pt(axis, edge, t), b = Pt(axis, edge, t1);
                Vector3 at = a + Vector3.up * CityLayout.CurbHeight, bt = b + Vector3.up * CityLayout.CurbHeight;
                mb.Quad(0, a, b, bt, at, 0.2f);
                mb.Quad(0, b, a, at, bt, 0.2f);
            }
        }

        // axis 0: line along z at x = across; axis 1: line along x at z = across.
        static Vector3 Pt(int axis, float across, float along, float lift = 0f)
        {
            float x = axis == 0 ? across : along, z = axis == 0 ? along : across;
            return new Vector3(x, H(x, z) + lift, z);
        }

        void BuildMarkings(GameObject root)
        {
            var white = new MeshBuilder(1);
            var yellow = new MeshBuilder(1);
            float p = CityLayout.Pitch, r = CityLayout.RoadHalf;
            int lines = CityLayout.Lines;
            const float lift = 0.03f;
            for (int axis = 0; axis < 2; axis++)
            for (int k = 0; k < lines; k++)
            {
                float c = k * p;
                for (int j = 0; j < lines - 1; j++)
                {
                    // Segment between intersections j and j+1.
                    float from = j * p + r, to = (j + 1) * p - r;

                    // Double yellow centre line.
                    Stripe(yellow, axis, c - 0.35f, c - 0.15f, from + 1f, to - 1f, lift);
                    Stripe(yellow, axis, c + 0.15f, c + 0.35f, from + 1f, to - 1f, lift);
                    // Dashed lane dividers.
                    for (float t = from + 4f; t < to - 4f; t += 6f)
                    {
                        Stripe(white, axis, c - 4.1f, c - 3.9f, t, Mathf.Min(t + 3f, to - 4f), lift);
                        Stripe(white, axis, c + 3.9f, c + 4.1f, t, Mathf.Min(t + 3f, to - 4f), lift);
                    }
                    // Crosswalks at both ends.
                    for (float w = -r + 1f; w < r - 1f; w += 1.4f)
                    {
                        Stripe(white, axis, c + w, c + w + 0.7f, from + 0.6f, from + 3.6f, lift);
                        Stripe(white, axis, c + w, c + w + 0.7f, to - 3.6f, to - 0.6f, lift);
                    }
                }
            }
            MeshObject("Markings", root, white.Build(), false, line);
            MeshObject("CentreLines", root, yellow.Build(), false, yellowLine);
        }

        // Thin strip [a,b] across the street, [from,to] along it, draped on the terrain.
        void Stripe(MeshBuilder mb, int axis, float a, float b, float from, float to, float lift)
        {
            for (float t = from; t < to - 0.01f; t += cell)
            {
                float t1 = Mathf.Min(t + cell, to);
                Vector3 v0 = Pt(axis, a, t, lift), v1 = Pt(axis, b, t, lift), v2 = Pt(axis, b, t1, lift), v3 = Pt(axis, a, t1, lift);
                if (axis == 0) mb.Quad(0, v0, v1, v2, v3, 1f);
                else mb.Quad(0, v1, v0, v3, v2, 1f);
            }
        }

        // Concrete wall around the edge of the city.
        void BuildBoundary(GameObject root)
        {
            var mb = new MeshBuilder(1);
            float min = CityLayout.Min, max = CityLayout.Max;
            const float step = 4f, height = 3f;
            for (int side = 0; side < 4; side++)
            for (float t = min; t < max - 0.01f; t += step)
            {
                float t1 = Mathf.Min(t + step, max);
                Vector3 a, b;
                switch (side)
                {
                    case 0: a = new Vector3(t, 0f, min); b = new Vector3(t1, 0f, min); break;
                    case 1: a = new Vector3(t, 0f, max); b = new Vector3(t1, 0f, max); break;
                    case 2: a = new Vector3(min, 0f, t); b = new Vector3(min, 0f, t1); break;
                    default: a = new Vector3(max, 0f, t); b = new Vector3(max, 0f, t1); break;
                }
                Vector3 at = new Vector3(a.x, H(a.x, a.z) + height, a.z), bt = new Vector3(b.x, H(b.x, b.z) + height, b.z);
                a.y = -12f; b.y = -12f;
                mb.Quad(0, a, b, bt, at, 1f);
                mb.Quad(0, b, a, at, bt, 1f);
            }
            MeshObject("BoundaryWall", root, mb.Build(), true, barrier);
        }

        // ---- Blocks: buildings, plazas and parks ----

        // Each block is split into ~30 m lots; the lots along the block edge get a building whose box is
        // wrapped in a photographed facade (towers more likely downtown, some with a setback crown).
        // Buildings reach down below the lowest ground under them so slopes never show a gap.
        void BuildBlocks(GameObject root)
        {
            var rng = new System.Random(seed);
            float R() => (float)rng.NextDouble();
            float p = CityLayout.Pitch, inset = CityLayout.RoadHalf + CityLayout.Sidewalk;
            int lines = CityLayout.Lines;
            int fc = Mathf.Max(1, facadeMaterials.Length);
            int roofSub = fc;
            var mb = new MeshBuilder(fc + 1);
            var cyl = PrimitiveMesh(PrimitiveType.Cylinder);
            var sph = PrimitiveMesh(PrimitiveType.Sphere);
            var trunks = new List<CombineInstance>();
            var crowns = new List<CombineInstance>();
            Vector2 centre = new Vector2(CityLayout.Size * 0.5f, CityLayout.Size * 0.5f);

            for (int bi = -1; bi < lines; bi++)
            for (int bj = -1; bj < lines; bj++)
            {
                float x0 = bi < 0 ? CityLayout.Min + 3f : bi * p + inset;
                float x1 = bi >= lines - 1 ? CityLayout.Max - 3f : (bi + 1) * p - inset;
                float z0 = bj < 0 ? CityLayout.Min + 3f : bj * p + inset;
                float z1 = bj >= lines - 1 ? CityLayout.Max - 3f : (bj + 1) * p - inset;

                if (CityLayout.IsPark(bi, bj))
                {
                    for (int t = 0; t < 14; t++)
                        AddTree(trunks, crowns, cyl, sph, new Vector3(Mathf.Lerp(x0 + 4f, x1 - 4f, R()), 0f, Mathf.Lerp(z0 + 4f, z1 - 4f, R())), 0.8f + R() * 0.7f, R());
                    continue;
                }

                int nx = Mathf.Max(1, Mathf.FloorToInt((x1 - x0) / lotSize));
                int nz = Mathf.Max(1, Mathf.FloorToInt((z1 - z0) / lotSize));
                float lw = (x1 - x0) / nx, ld = (z1 - z0) / nz;
                for (int lx = 0; lx < nx; lx++)
                for (int lz = 0; lz < nz; lz++)
                {
                    float cx = x0 + (lx + 0.5f) * lw, cz = z0 + (lz + 0.5f) * ld;
                    bool edge = lx == 0 || lz == 0 || lx == nx - 1 || lz == nz - 1;
                    if (!edge) continue;

                    float w = lw - 1f - R() * 4f, d = ld - 1f - R() * 4f;
                    float lo = float.MaxValue, hi = float.MinValue;
                    foreach (var c in new[] { new Vector2(-w, -d), new Vector2(w, -d), new Vector2(-w, d), new Vector2(w, d), Vector2.zero })
                    {
                        float h = H(cx + c.x * 0.5f, cz + c.y * 0.5f);
                        lo = Mathf.Min(lo, h);
                        hi = Mathf.Max(hi, h);
                    }
                    float downtown = Mathf.Clamp01(1f - Vector2.Distance(new Vector2(cx, cz), centre) / 520f);
                    float height = Mathf.Lerp(12f, 34f, R()) * Mathf.Lerp(0.8f, 1.7f, downtown);
                    if (R() < 0.1f + 0.3f * downtown) height = 55f + R() * 90f;

                    int sub = rng.Next(fc);
                    float tile = sub < facadeTiles.Length ? facadeTiles[sub] : 24f;
                    float baseY = lo - 1.5f;
                    float top = hi + CityLayout.CurbHeight + height;
                    mb.Box(sub, roofSub, new Vector3(cx, baseY, cz), new Vector3(w, top - baseY, d), tile, tile);
                    if (height > 50f && R() < 0.65f)
                    {
                        // Setback crown in the same facade.
                        float crown = 8f + R() * 24f;
                        mb.Box(sub, roofSub, new Vector3(cx, top, cz), new Vector3(w * 0.65f, crown, d * 0.65f), tile, tile);
                    }
                    else
                    {
                        // Rooftop plant room.
                        mb.Box(roofSub, roofSub, new Vector3(cx + (R() - 0.5f) * w * 0.4f, top, cz + (R() - 0.5f) * d * 0.4f), new Vector3(5f, 3f, 4f), 4f, 4f);
                    }
                }
            }

            var mats = new Material[fc + 1];
            for (int i = 0; i < fc; i++) mats[i] = facadeMaterials.Length > 0 ? facadeMaterials[i] : roof;
            mats[roofSub] = roof;
            MeshObject("Buildings", root, mb.Build(), true, mats).layer = cityLayer;
            MeshObject("Trunks", root, Combine(trunks), false, trunk).layer = cityLayer;
            MeshObject("Crowns", root, Combine(crowns), false, leaves).layer = cityLayer;
        }

        // Fire hydrants and bins along the kerbs, concrete barriers lining the start straight.
        void BuildProps(TrackPath path, GameObject root)
        {
            var rng = new System.Random(seed + 1);
            float R() => (float)rng.NextDouble();
            var props = NewObject("Props", root.transform);
            float p = CityLayout.Pitch, r = CityLayout.RoadHalf;
            int lines = CityLayout.Lines;
            for (int axis = 0; axis < 2; axis++)
            for (int k = 0; k < lines; k++)
            for (int j = 0; j < lines - 1; j++)
            for (int side = -1; side <= 1; side += 2)
            {
                float along = j * p + r + 12f + R() * (p - 2f * r - 24f);
                Vector3 pos = Pt(axis, k * p + side * (r + 0.9f), along, CityLayout.CurbHeight);
                float roll = R();
                if (roll < 0.35f) Prop(props.transform, hydrant, hydrantMaterial, pos, R() * 360f, 0.85f);
                else if (roll < 0.65f) Prop(props.transform, trashCan, trashCanMaterial, pos, R() * 360f, 1.0f);
            }

            // Barriers on both kerbs either side of the start line.
            int si = path.StartIndex;
            float step = 3.2f / path.Spacing;
            for (int n = -14; n <= 14; n++)
            {
                int idx = path.Wrap(si + Mathf.RoundToInt(n * step));
                Vector3 f = path.FlatTangent(idx), rt = path.Right(idx), c = path.Point(idx);
                foreach (float s in new[] { -1f, 1f })
                {
                    Vector3 pos = c + rt * s * (r + 0.7f);
                    pos.y = H(pos.x, pos.z) + CityLayout.CurbHeight;
                    Prop(props.transform, roadBarrier, roadBarrierMaterial, pos, Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg + 90f, 1.0f, true);
                }
            }
        }

        // Instantiates a prop scaled to the given height (models may come in any unit).
        void Prop(Transform parent, GameObject prefab, Material mat, Vector3 pos, float yaw, float height, bool collider = false)
        {
            if (!prefab) return;
            var go = Instantiate(prefab, parent);
            go.name = prefab.name;
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            var rends = go.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) return;
            Bounds b = rends[0].bounds;
            foreach (var rr in rends) b.Encapsulate(rr.bounds);
            float s = height / Mathf.Max(b.size.y, 0.001f);
            go.transform.localScale *= s;
            b = rends[0].bounds;
            foreach (var rr in rends) b.Encapsulate(rr.bounds);
            go.transform.position += Vector3.up * (pos.y - b.min.y);
            foreach (var rr in rends)
            {
                if (mat) rr.sharedMaterial = mat;
                rr.gameObject.layer = cityLayer;
            }
            if (collider)
            {
                var box = go.AddComponent<BoxCollider>();
                b = rends[0].bounds;
                foreach (var rr in rends) b.Encapsulate(rr.bounds);
                box.center = go.transform.InverseTransformPoint(b.center);
                Vector3 size = go.transform.InverseTransformVector(b.size);
                box.size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));
            }
        }

        static void AddTree(List<CombineInstance> trunks, List<CombineInstance> crowns, Mesh cyl, Mesh sph, Vector3 pos, float s, float spin)
        {
            pos.y = H(pos.x, pos.z) + CityLayout.CurbHeight;
            trunks.Add(Inst(cyl, pos + Vector3.up * 1.5f * s, Quaternion.identity, new Vector3(0.45f, 1.5f, 0.45f) * s));
            crowns.Add(Inst(sph, pos + Vector3.up * 4.4f * s, Quaternion.Euler(0f, spin * 360f, 0f), new Vector3(3.6f, 4.2f, 3.6f) * s));
        }

        // ---- Lamps and start ----

        void BuildLamps(TrackPath path, GameObject root)
        {
            var cyl = PrimitiveMesh(PrimitiveType.Cylinder);
            var cube = PrimitiveMesh(PrimitiveType.Cube);
            var poles = new List<CombineInstance>();
            var heads = new List<CombineInstance>();
            LampLightPositions.Clear();
            float p = CityLayout.Pitch, r = CityLayout.RoadHalf;
            int lines = CityLayout.Lines;
            float off = r + 1.3f;
            for (int axis = 0; axis < 2; axis++)
            for (int k = 0; k < lines; k++)
            for (int j = 0; j < lines - 1; j++)
            {
                float from = j * p + r + 8f, to = (j + 1) * p - r - 8f;
                int idx = 0;
                for (float t = from; t <= to + 0.01f; t += (to - from) / 2f, idx++)
                {
                    float side = (idx + k + j) % 2 == 0 ? 1f : -1f;
                    Vector3 basePos = Pt(axis, k * p + side * off, t, CityLayout.CurbHeight);
                    Vector3 inward = axis == 0 ? new Vector3(-side, 0f, 0f) : new Vector3(0f, 0f, -side);
                    var face = Quaternion.LookRotation(inward);
                    poles.Add(Inst(cyl, basePos + Vector3.up * 4f, Quaternion.identity, new Vector3(0.22f, 4f, 0.22f)));
                    poles.Add(Inst(cube, basePos + Vector3.up * 7.9f + inward * 1.3f, face, new Vector3(0.14f, 0.14f, 2.6f)));
                    Vector3 head = basePos + Vector3.up * 7.75f + inward * 2.5f;
                    heads.Add(Inst(cube, head, face, new Vector3(0.5f, 0.18f, 0.9f)));
                    if (path.MinHorizontalDistance(head) < r + 4f) LampLightPositions.Add(head - Vector3.up * 0.25f);
                }
            }
            MeshObject("LampPoles", root, Combine(poles), false, lampPole).layer = cityLayer;
            MeshObject("LampHeads", root, Combine(heads), false, lampHead).layer = cityLayer;
        }

        void BuildStart(TrackPath path, GameObject root)
        {
            int si = path.StartIndex;
            Vector3 p = path.Point(si), r = path.Right(si), f = path.FlatTangent(si);
            float hw = path.roadHalfWidth;

            var mb = new MeshBuilder(2);
            int cols = Mathf.RoundToInt(hw * 2f);
            for (int row = 0; row < 2; row++)
            for (int col = 0; col < cols; col++)
            {
                Vector3 o = p + r * (-hw + col) + f * (row - 1f);
                Vector3 a = Lift(o), b = Lift(o + r), c = Lift(o + r + f), d = Lift(o + f);
                mb.Quad((row + col) % 2, a, b, c, d, 1f);
            }
            MeshObject("StartLine", root, mb.Build(), false, checkerBlack, line);

            float span = hw + 2f;
            var rot = Quaternion.LookRotation(f);
            Vector3 left = p - r * span, right = p + r * span;
            left.y = H(left.x, left.z);
            right.y = H(right.x, right.z);
            float top = Mathf.Max(left.y, right.y) + 7.5f;
            Cube("GantryL", root, new Vector3(left.x, (left.y + top) / 2f, left.z), rot, new Vector3(1f, top - left.y, 1f), gantry);
            Cube("GantryR", root, new Vector3(right.x, (right.y + top) / 2f, right.z), rot, new Vector3(1f, top - right.y, 1f), gantry);
            Vector3 mid = new Vector3(p.x, top, p.z);
            Cube("GantryBeam", root, mid, rot, new Vector3(span * 2f + 1f, 1.4f, 1f), gantry);
            Cube("GantryBanner", root, mid - f * 0.55f, rot, new Vector3(span * 2f - 2f, 0.9f, 0.1f), banner);

            static Vector3 Lift(Vector3 v) => new Vector3(v.x, H(v.x, v.z) + 0.035f, v.z);
        }

        // ---- Helpers ----

        static CombineInstance Inst(Mesh m, Vector3 pos, Quaternion rot, Vector3 scale) =>
            new CombineInstance { mesh = m, transform = Matrix4x4.TRS(pos, rot, scale) };

        static Mesh Combine(List<CombineInstance> list)
        {
            var m = new Mesh { indexFormat = IndexFormat.UInt32, hideFlags = HideFlags.DontSave };
            m.CombineMeshes(list.ToArray(), true, true);
            return m;
        }

        static Mesh PrimitiveMesh(PrimitiveType type)
        {
            var go = GameObject.CreatePrimitive(type);
            var mesh = go.GetComponent<MeshFilter>().sharedMesh;
            DestroyImmediate(go);
            return mesh;
        }

        static GameObject NewObject(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go;
        }

        static GameObject MeshObject(string name, GameObject parent, Mesh mesh, bool collider, params Material[] mats)
        {
            var go = NewObject(name, parent.transform);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = mats;
            if (collider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
            return go;
        }

        static void Cube(string name, GameObject parent, Vector3 pos, Quaternion rot, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent.transform, false);
            go.transform.SetPositionAndRotation(pos, rot);
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        // Edit mode only: keeps generated objects out of the saved scene. At runtime they are ordinary scene
        // objects, so they are torn down with the scene (DontSave objects outlive physics shutdown and crash on quit).
        static void SetFlags(GameObject go)
        {
            if (Application.isPlaying) return;
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.hideFlags = HideFlags.DontSave;
        }

        static void DestroyGenerated(GameObject go)
        {
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                var m = mf.sharedMesh;
                if (m && (m.hideFlags & HideFlags.DontSave) != 0) DestroyImmediate(m);
            }
            DestroyImmediate(go);
        }

        void OnDisable()
        {
            // Only needed in the editor; tearing down thousands of objects while the player quits can crash.
            if (Application.isPlaying) return;
            var old = transform.Find(RootName);
            if (old) DestroyGenerated(old.gameObject);
        }

        // Height-field grid where only some cells are emitted; vertices are shared and compacted.
        class GridMesh
        {
            readonly int n;
            readonly float lift, uvScale;
            readonly int[] map;
            readonly List<Vector3> verts = new List<Vector3>();
            readonly List<Vector2> uvs = new List<Vector2>();
            readonly List<int> tris = new List<int>();

            public GridMesh(int cells, float lift, float uvScale)
            {
                n = cells;
                this.lift = lift;
                this.uvScale = uvScale;
                map = new int[(n + 1) * (n + 1)];
                for (int i = 0; i < map.Length; i++) map[i] = -1;
            }

            int Vertex(int i, int j, float min, float cell)
            {
                int key = j * (n + 1) + i;
                if (map[key] >= 0) return map[key];
                float x = min + i * cell, z = min + j * cell;
                map[key] = verts.Count;
                verts.Add(new Vector3(x, H(x, z) + lift, z));
                uvs.Add(new Vector2(x / uvScale, z / uvScale));
                return map[key];
            }

            public void AddCell(int i, int j, float min, float cell)
            {
                int a = Vertex(i, j, min, cell), b = Vertex(i + 1, j, min, cell), c = Vertex(i + 1, j + 1, min, cell), d = Vertex(i, j + 1, min, cell);
                tris.Add(a); tris.Add(d); tris.Add(c);
                tris.Add(a); tris.Add(c); tris.Add(b);
            }

            public Mesh Build()
            {
                var m = new Mesh { indexFormat = IndexFormat.UInt32, hideFlags = HideFlags.DontSave };
                m.SetVertices(verts);
                m.SetUVs(0, uvs);
                m.SetTriangles(tris, 0);
                m.RecalculateNormals();
                m.RecalculateBounds();
                return m;
            }
        }

        class MeshBuilder
        {
            readonly List<Vector3> verts = new List<Vector3>();
            readonly List<Vector2> uvs = new List<Vector2>();
            readonly List<int>[] tris;

            public MeshBuilder(int submeshes)
            {
                tris = new List<int>[submeshes];
                for (int i = 0; i < submeshes; i++) tris[i] = new List<int>();
            }

            // As seen from the front: v0 bottom-left, v1 bottom-right, v2 top-right, v3 top-left.
            public void Quad(int sub, Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3, float uScale, float vStart = 0f, float vEnd = 1f)
            {
                int b = verts.Count;
                verts.Add(v0); verts.Add(v1); verts.Add(v2); verts.Add(v3);
                uvs.Add(new Vector2(0f, vStart)); uvs.Add(new Vector2(uScale, vStart));
                uvs.Add(new Vector2(uScale, vEnd)); uvs.Add(new Vector2(0f, vEnd));
                var t = tris[sub];
                t.Add(b); t.Add(b + 3); t.Add(b + 2);
                t.Add(b); t.Add(b + 2); t.Add(b + 1);
            }

            // Axis-aligned box standing on bottom-centre c; side UVs in metres / tile size.
            public void Box(int sideSub, int topSub, Vector3 c, Vector3 size, float tileW, float tileH)
            {
                float x0 = c.x - size.x / 2f, x1 = c.x + size.x / 2f;
                float z0 = c.z - size.z / 2f, z1 = c.z + size.z / 2f;
                float y0 = c.y, y1 = c.y + size.y;
                float uX = size.x / tileW, uZ = size.z / tileW, vH = size.y / tileH;
                Quad(sideSub, new Vector3(x1, y0, z0), new Vector3(x1, y0, z1), new Vector3(x1, y1, z1), new Vector3(x1, y1, z0), uZ, 0f, vH);
                Quad(sideSub, new Vector3(x0, y0, z1), new Vector3(x0, y0, z0), new Vector3(x0, y1, z0), new Vector3(x0, y1, z1), uZ, 0f, vH);
                Quad(sideSub, new Vector3(x1, y0, z1), new Vector3(x0, y0, z1), new Vector3(x0, y1, z1), new Vector3(x1, y1, z1), uX, 0f, vH);
                Quad(sideSub, new Vector3(x0, y0, z0), new Vector3(x1, y0, z0), new Vector3(x1, y1, z0), new Vector3(x0, y1, z0), uX, 0f, vH);
                Quad(topSub, new Vector3(x0, y1, z0), new Vector3(x1, y1, z0), new Vector3(x1, y1, z1), new Vector3(x0, y1, z1), size.x / 8f, 0f, size.z / 8f);
            }

            public Mesh Build()
            {
                var m = new Mesh { indexFormat = IndexFormat.UInt32, hideFlags = HideFlags.DontSave };
                m.SetVertices(verts);
                m.SetUVs(0, uvs);
                m.subMeshCount = tris.Length;
                for (int i = 0; i < tris.Length; i++) m.SetTriangles(tris[i], i);
                m.RecalculateNormals();
                m.RecalculateBounds();
                return m;
            }
        }
    }
}
