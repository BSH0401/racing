using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Racing
{
    // Generates a street circuit from the TrackPath: road, curbs, concrete barriers with ad panels,
    // sidewalks, street lamps, start gantry, grandstand and city blocks.
    // Everything is rebuilt on enable (edit and play mode) and never saved into the scene.
    [ExecuteAlways, RequireComponent(typeof(TrackPath))]
    public class TrackBuilder : MonoBehaviour
    {
        public Material road, curbRed, curbWhite, line, sidewalk, barrier, adA, adB, ground, checkerBlack, gantry, stands, roof, lampPole, lampHead;
        public Material[] facades = new Material[0];

        public float curbWidth = 0.8f;
        public float runoffWidth = 2f;
        public float barrierHeight = 1.1f;
        public float barrierThickness = 0.6f;
        public float sidewalkWidth = 5f;
        public float lotPitch = 46f;
        public float cityMargin = 280f;
        public int seed = 7;
        public int cityLayer = 30;

        const string RootName = "_Generated";

        public float WallOffset => GetComponent<TrackPath>().roadHalfWidth + curbWidth + runoffWidth;

        void OnEnable() => Build();

        public void Build()
        {
            var path = GetComponent<TrackPath>();
            if (path.controlPoints == null || path.controlPoints.Length < 4) return;
            path.Rebuild();

            var old = transform.Find(RootName);
            if (old) DestroyGenerated(old.gameObject);

            var root = NewObject(RootName, transform);
            float hw = path.roadHalfWidth;
            int n = path.Count;
            float c0 = hw + curbWidth, wall = WallOffset;

            // Road, curbs and asphalt runoff (one collider mesh, three submeshes).
            var mb = new MeshBuilder(3);
            for (int i = 0; i < n; i++)
            {
                mb.Strip(path, i, -wall, -c0, 0f, 0);
                mb.Strip(path, i, -hw, hw, 0f, 0);
                mb.Strip(path, i, c0, wall, 0f, 0);
                int curbSub = (i / 2) % 2 == 0 ? 1 : 2;
                mb.Strip(path, i, -c0, -hw, 0.01f, curbSub);
                mb.Strip(path, i, hw, c0, 0.01f, curbSub);
            }
            MeshObject("Road", root, mb.Build(), true, road, curbRed, curbWhite);

            // Painted lines (visual only).
            var lb = new MeshBuilder(1);
            for (int i = 0; i < n; i++)
            {
                lb.Strip(path, i, -hw + 0.3f, -hw + 0.55f, 0.012f, 0);
                lb.Strip(path, i, hw - 0.55f, hw - 0.3f, 0.012f, 0);
                if ((i / 2) % 3 == 0) lb.Strip(path, i, -0.12f, 0.12f, 0.012f, 0);
            }
            MeshObject("Lines", root, lb.Build(), false, line);

            // Concrete barriers: inner face carries alternating ad panels.
            var bb = new MeshBuilder(3);
            float t = barrierThickness;
            for (int i = 0; i < n; i++)
            {
                int face = (i / 6) % 4 == 1 ? 1 : (i / 6) % 4 == 3 ? 2 : 0;
                bb.Wall(path, i, -wall, barrierHeight, face);
                bb.Wall(path, i, -wall - t, barrierHeight, 0);
                bb.Strip(path, i, -wall - t, -wall, barrierHeight, 0);
                bb.Wall(path, i, wall, barrierHeight, face);
                bb.Wall(path, i, wall + t, barrierHeight, 0);
                bb.Strip(path, i, wall, wall + t, barrierHeight, 0);
            }
            MeshObject("Barriers", root, bb.Build(), true, barrier, adA, adB);

            // Raised sidewalks behind the barriers.
            var sb = new MeshBuilder(1);
            float s0 = wall + t, s1 = s0 + sidewalkWidth;
            for (int i = 0; i < n; i++)
            {
                sb.Strip(path, i, -s1, -s0, 0.15f, 0);
                sb.Strip(path, i, s0, s1, 0.15f, 0);
                sb.Wall(path, i, -s1, 0.15f, 0);
                sb.Wall(path, i, s1, 0.15f, 0);
            }
            var walk = MeshObject("Sidewalks", root, sb.Build(), true, sidewalk);
            walk.AddComponent<TrackSurface>();

            BuildGround(path, root);
            Bounds standsBox = BuildStart(path, root);
            BuildLamps(path, root);
            BuildCity(path, root, standsBox);

            SetFlags(root);
        }

        void BuildGround(TrackPath path, GameObject root)
        {
            var b = path.GetBounds();
            float size = 3000f;
            var mb = new MeshBuilder(1);
            Vector3 c = new Vector3(b.center.x, -0.1f, b.center.z);
            Vector3 h = new Vector3(size * 0.5f, 0f, 0f), v = new Vector3(0f, 0f, size * 0.5f);
            mb.Quad(0, c - h - v, c + h - v, c + h + v, c - h + v, size / 6f, 0f, size / 6f);
            var go = MeshObject("Ground", root, mb.Build(), false, ground);
            var box = go.AddComponent<BoxCollider>();
            box.center = new Vector3(c.x, c.y - 0.5f, c.z);
            box.size = new Vector3(size, 1f, size);
            go.AddComponent<TrackSurface>();
        }

        // Returns the grandstand footprint (axis-aligned) so buildings keep clear of it.
        Bounds BuildStart(TrackPath path, GameObject root)
        {
            int si = path.StartIndex;
            Vector3 p = path.Point(si), r = path.Right(si), f = path.FlatTangent(si);
            float hw = path.roadHalfWidth;

            // Checkered line: two rows of 1m squares.
            var mb = new MeshBuilder(2);
            int cols = Mathf.RoundToInt(hw * 2f);
            for (int row = 0; row < 2; row++)
            for (int col = 0; col < cols; col++)
            {
                Vector3 o = p + r * (-hw + col) + f * (row - 1f) + Vector3.up * 0.015f;
                mb.Quad((row + col) % 2, o, o + r, o + r + f, o + f, 1f);
            }
            MeshObject("StartLine", root, mb.Build(), false, checkerBlack, line);

            // Gantry over the line.
            float span = WallOffset + barrierThickness + 0.8f;
            var rot = Quaternion.LookRotation(f);
            Cube("GantryL", root, p - r * span + Vector3.up * 3.5f, rot, new Vector3(1f, 7f, 1f), gantry);
            Cube("GantryR", root, p + r * span + Vector3.up * 3.5f, rot, new Vector3(1f, 7f, 1f), gantry);
            Cube("GantryBeam", root, p + Vector3.up * 7.2f, rot, new Vector3(span * 2f + 1f, 1.4f, 1f), gantry);
            Cube("GantryBanner", root, p + Vector3.up * 7.2f - f * 0.55f, rot, new Vector3(span * 2f - 2f, 0.9f, 0.1f), curbRed);

            // Grandstand on the left of the start straight.
            float off = WallOffset + barrierThickness + 3f;
            var standsBox = new Bounds(p - r * (off + 6f), Vector3.zero);
            for (int step = 0; step < 5; step++)
            {
                Vector3 sp = p - r * (off + step * 2.5f) + Vector3.up * (0.6f + step * 1.1f) - f * 10f;
                var size = new Vector3(2.5f, 1.2f + step * 2.2f, 80f);
                Cube("Stand" + step, root, sp, rot, size, stands);
                standsBox.Encapsulate(sp + f * 42f);
                standsBox.Encapsulate(sp - f * 42f);
            }
            Cube("StandRoof", root, p - r * (off + 6f) + Vector3.up * 13f - f * 10f, rot, new Vector3(14f, 0.4f, 82f), gantry);
            standsBox.Expand(new Vector3(8f, 0f, 8f));
            return standsBox;
        }

        void BuildLamps(TrackPath path, GameObject root)
        {
            var cyl = PrimitiveMesh(PrimitiveType.Cylinder);
            var cube = PrimitiveMesh(PrimitiveType.Cube);
            var poles = new List<CombineInstance>();
            var heads = new List<CombineInstance>();
            float off = WallOffset + barrierThickness + 1.2f;
            int step = Mathf.Max(1, Mathf.RoundToInt(36f / path.Spacing));
            for (int i = 0; i < path.Count; i += step)
            {
                float side = (i / step) % 2 == 0 ? 1f : -1f;
                Vector3 r = path.Right(i) * side;
                Vector3 basePos = path.Point(i) + r * off;
                var face = Quaternion.LookRotation(-r);
                poles.Add(Inst(cyl, basePos + Vector3.up * 4f, Quaternion.identity, new Vector3(0.22f, 4f, 0.22f)));
                poles.Add(Inst(cube, basePos + Vector3.up * 7.9f - r * 1.3f, face, new Vector3(0.14f, 0.14f, 2.6f)));
                heads.Add(Inst(cube, basePos + Vector3.up * 7.75f - r * 2.5f, face, new Vector3(0.5f, 0.18f, 0.9f)));
            }
            SetLayer(MeshObject("LampPoles", root, Combine(poles), false, lampPole));
            SetLayer(MeshObject("LampHeads", root, Combine(heads), false, lampHead));
        }

        void BuildCity(TrackPath path, GameObject root, Bounds standsBox)
        {
            var rng = new System.Random(seed);
            float R() => (float)rng.NextDouble();
            var b = path.GetBounds();
            int fc = Mathf.Max(1, facades.Length);
            int roofSub = fc, plinthSub = fc + 1;
            var mb = new MeshBuilder(fc + 2);
            float clear = WallOffset + barrierThickness + sidewalkWidth + 2f;

            for (float x = b.min.x - cityMargin; x <= b.max.x + cityMargin; x += lotPitch)
            for (float z = b.min.z - cityMargin; z <= b.max.z + cityMargin; z += lotPitch)
            {
                float w = 16f + R() * 18f, d = 16f + R() * 18f;
                var c = new Vector3(x + (R() - 0.5f) * (lotPitch - w - 6f), 0f, z + (R() - 0.5f) * (lotPitch - d - 6f));
                float halfDiag = Mathf.Sqrt(w * w + d * d) * 0.5f + 2f;
                float dist = path.MinHorizontalDistance(c);
                if (dist - halfDiag < clear) continue;
                var fp = new Bounds(c, new Vector3(w + 4f, 10f, d + 4f));
                if (fp.Intersects(standsBox)) continue;

                // Taller towers near the circuit, lower blocks further out.
                float nearness = Mathf.Clamp01(1f - (dist - 20f) / 300f);
                float h = Mathf.Lerp(10f, 34f, R()) * Mathf.Lerp(0.8f, 1.5f, nearness);
                if (R() < 0.12f + 0.15f * nearness) h = 55f + R() * 75f;

                int sub = rng.Next(fc);
                mb.Box(plinthSub, plinthSub, new Vector3(c.x, -0.1f, c.z), new Vector3(w + 4f, 0.25f, d + 4f), 4f, 4f);
                mb.Box(sub, roofSub, new Vector3(c.x, 0.15f, c.z), new Vector3(w, h, d), 24f, 28f);
                if (h > 40f && R() < 0.6f)
                    mb.Box(sub, roofSub, new Vector3(c.x, 0.15f + h, c.z), new Vector3(w * 0.6f, 4f + R() * 10f, d * 0.6f), 24f, 28f);
                else if (R() < 0.5f)
                    mb.Box(roofSub, roofSub, new Vector3(c.x + (R() - 0.5f) * w * 0.4f, 0.15f + h, c.z + (R() - 0.5f) * d * 0.4f), new Vector3(4f, 2.2f, 3f), 4f, 4f);
            }

            var mats = new Material[fc + 2];
            for (int i = 0; i < fc; i++) mats[i] = facades.Length > 0 ? facades[i] : roof;
            mats[roofSub] = roof;
            mats[plinthSub] = sidewalk;
            SetLayer(MeshObject("City", root, mb.Build(), false, mats));
        }

        void SetLayer(GameObject go) => go.layer = cityLayer;

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

        static GameObject NewObject(string name, GameObject parent) => NewObject(name, parent.transform);

        static GameObject MeshObject(string name, GameObject parent, Mesh mesh, bool collider, params Material[] mats)
        {
            var go = NewObject(name, parent);
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

        static void SetFlags(GameObject go)
        {
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
            var old = transform.Find(RootName);
            if (old) DestroyGenerated(old.gameObject);
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

            public void Strip(TrackPath p, int i, float a, float bOff, float h, int sub)
            {
                Vector3 up = Vector3.up * h;
                Vector3 p0 = p.Point(i), p1 = p.Point(i + 1), r0 = p.Right(i), r1 = p.Right(i + 1);
                float v0 = i * p.Spacing / 4f, v1 = (i + 1) * p.Spacing / 4f;
                Quad(sub, p0 + r0 * a + up, p0 + r0 * bOff + up, p1 + r1 * bOff + up, p1 + r1 * a + up, (bOff - a) / 4f, v0, v1);
            }

            // Double-sided vertical strip from below ground to height above the road.
            public void Wall(TrackPath p, int i, float off, float height, int sub)
            {
                Vector3 p0 = p.Point(i), p1 = p.Point(i + 1), r0 = p.Right(i), r1 = p.Right(i + 1);
                Vector3 b0 = p0 + r0 * off, b1 = p1 + r1 * off;
                Vector3 t0 = b0 + Vector3.up * height, t1 = b1 + Vector3.up * height;
                b0.y = -0.3f; b1.y = -0.3f;
                Quad(sub, b0, b1, t1, t0, 1f);
                Quad(sub, b1, b0, t0, t1, 1f);
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
