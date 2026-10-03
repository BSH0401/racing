using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Racing
{
    // Generates the road, curbs, runoff, walls, start gantry and scenery from the TrackPath.
    // Everything is rebuilt on enable (edit and play mode) and never saved into the scene.
    [ExecuteAlways, RequireComponent(typeof(TrackPath))]
    public class TrackBuilder : MonoBehaviour
    {
        public Material road, curbRed, curbWhite, line, grass, wall, ground, checkerBlack, gantry, trunk, leaves, hills, stands;
        public float curbWidth = 1.2f;
        public float runoffWidth = 10f;
        public float wallHeight = 1.2f;
        public int treeCount = 260;
        public int seed = 7;

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

            // Road + curbs (one collider mesh, three submeshes).
            var mb = new MeshBuilder(3);
            for (int i = 0; i < n; i++)
            {
                mb.Strip(path, i, -hw, hw, 0f, 0);
                int curbSub = (i / 2) % 2 == 0 ? 1 : 2;
                mb.Strip(path, i, -hw - curbWidth, -hw, 0f, curbSub);
                mb.Strip(path, i, hw, hw + curbWidth, 0f, curbSub);
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

            // Grass runoff.
            var gb = new MeshBuilder(1);
            float c0 = hw + curbWidth, c1 = hw + curbWidth + runoffWidth;
            for (int i = 0; i < n; i++)
            {
                gb.Strip(path, i, -c1, -c0, 0f, 0);
                gb.Strip(path, i, c0, c1, 0f, 0);
            }
            var runoff = MeshObject("Runoff", root, gb.Build(), true, grass);
            runoff.AddComponent<TrackSurface>();

            // Walls from below ground up to wallHeight above the road, double sided.
            var wb = new MeshBuilder(1);
            for (int i = 0; i < n; i++)
            {
                wb.Wall(path, i, -c1, wallHeight);
                wb.Wall(path, i, c1, wallHeight);
            }
            MeshObject("Walls", root, wb.Build(), true, wall);

            BuildGround(path, root);
            BuildStart(path, root);
            BuildScenery(path, root);

            SetFlags(root);
        }

        void BuildGround(TrackPath path, GameObject root)
        {
            var b = path.GetBounds();
            float size = 3000f;
            var mb = new MeshBuilder(1);
            Vector3 c = new Vector3(b.center.x, -0.3f, b.center.z);
            Vector3 h = new Vector3(size * 0.5f, 0f, 0f), v = new Vector3(0f, 0f, size * 0.5f);
            mb.Quad(0, c - h - v, c + h - v, c + h + v, c - h + v, size / 8f);
            var go = MeshObject("Ground", root, mb.Build(), false, ground);
            var box = go.AddComponent<BoxCollider>();
            box.center = new Vector3(c.x, c.y - 0.5f, c.z);
            box.size = new Vector3(size, 1f, size);
            go.AddComponent<TrackSurface>();
        }

        void BuildStart(TrackPath path, GameObject root)
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
            float span = hw + curbWidth + 1f;
            var rot = Quaternion.LookRotation(f);
            Cube("GantryL", root, p - r * span + Vector3.up * 3.5f, rot, new Vector3(1f, 7f, 1f), gantry, true);
            Cube("GantryR", root, p + r * span + Vector3.up * 3.5f, rot, new Vector3(1f, 7f, 1f), gantry, true);
            Cube("GantryBeam", root, p + Vector3.up * 7.2f, rot, new Vector3(span * 2f + 1f, 1.4f, 1f), gantry, false);
            Cube("GantryBanner", root, p + Vector3.up * 7.2f - f * 0.55f, rot, new Vector3(span * 2f - 2f, 0.9f, 0.1f), curbRed, false);

            // Grandstand along the outside of the start straight.
            float wallOff = WallOffset;
            for (int step = 0; step < 4; step++)
            {
                Vector3 sp = p - r * (wallOff + 4f + step * 2.5f) + Vector3.up * (0.6f + step * 0.9f) - f * 10f;
                Cube("Stand" + step, root, sp, rot, new Vector3(2.5f, 1.2f + step * 1.8f, 70f), stands, false);
            }
        }

        void BuildScenery(TrackPath path, GameObject root)
        {
            var rng = new System.Random(seed);
            float R() => (float)rng.NextDouble();
            var b = path.GetBounds();
            float clear = WallOffset + 10f;

            var cyl = PrimitiveMesh(PrimitiveType.Cylinder);
            var sph = PrimitiveMesh(PrimitiveType.Sphere);
            var trunks = new List<CombineInstance>();
            var crowns = new List<CombineInstance>();
            int placed = 0;
            for (int attempt = 0; attempt < 4000 && placed < treeCount; attempt++)
            {
                var pos = new Vector3(Mathf.Lerp(b.min.x - 140f, b.max.x + 140f, R()), -0.3f, Mathf.Lerp(b.min.z - 140f, b.max.z + 140f, R()));
                if (path.MinHorizontalDistance(pos) < clear) continue;
                float s = 0.8f + R() * 0.9f;
                trunks.Add(new CombineInstance { mesh = cyl, transform = Matrix4x4.TRS(pos + Vector3.up * 1.5f * s, Quaternion.identity, new Vector3(0.5f, 1.5f, 0.5f) * s) });
                crowns.Add(new CombineInstance { mesh = sph, transform = Matrix4x4.TRS(pos + Vector3.up * 4.6f * s, Quaternion.Euler(0f, R() * 360f, 0f), new Vector3(3.6f, 4.4f, 3.6f) * s) });
                placed++;
            }
            MeshObject("Trunks", root, Combine(trunks), false, trunk);
            MeshObject("Crowns", root, Combine(crowns), false, leaves);

            // Distant hills ring the horizon.
            var hillList = new List<CombineInstance>();
            Vector3 c = b.center;
            float ring = Mathf.Max(b.extents.x, b.extents.z) + 450f;
            for (int i = 0; i < 26; i++)
            {
                float a = i / 26f * Mathf.PI * 2f + R() * 0.2f;
                float rr = ring + R() * 250f;
                var hp = new Vector3(c.x + Mathf.Cos(a) * rr, -20f, c.z + Mathf.Sin(a) * rr);
                var hs = new Vector3(220f + R() * 260f, 90f + R() * 140f, 220f + R() * 260f);
                hillList.Add(new CombineInstance { mesh = sph, transform = Matrix4x4.TRS(hp, Quaternion.Euler(0f, R() * 360f, 0f), hs) });
            }
            MeshObject("Hills", root, Combine(hillList), false, hills);
        }

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

        static void Cube(string name, GameObject parent, Vector3 pos, Quaternion rot, Vector3 scale, Material mat, bool collider)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent.transform, false);
            go.transform.SetPositionAndRotation(pos, rot);
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            if (!collider) DestroyImmediate(go.GetComponent<Collider>());
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

            // v0 near-left, v1 near-right, v2 far-right, v3 far-left (facing up).
            public void Quad(int sub, Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3, float uvScale, float vStart = 0f, float vEnd = 1f)
            {
                int b = verts.Count;
                verts.Add(v0); verts.Add(v1); verts.Add(v2); verts.Add(v3);
                uvs.Add(new Vector2(0f, vStart)); uvs.Add(new Vector2(uvScale, vStart));
                uvs.Add(new Vector2(uvScale, vEnd)); uvs.Add(new Vector2(0f, vEnd));
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

            public void Wall(TrackPath p, int i, float off, float height)
            {
                Vector3 p0 = p.Point(i), p1 = p.Point(i + 1), r0 = p.Right(i), r1 = p.Right(i + 1);
                Vector3 b0 = p0 + r0 * off, b1 = p1 + r1 * off;
                Vector3 t0 = b0 + Vector3.up * height, t1 = b1 + Vector3.up * height;
                b0.y = -0.6f; b1.y = -0.6f;
                Quad(0, b0, b1, t1, t0, 1f);
                Quad(0, b1, b0, t0, t1, 1f);
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
