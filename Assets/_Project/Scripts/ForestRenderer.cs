using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Racing
{
    // Draws the countryside forests with GPU instancing: trees are bucketed into square chunks, each
    // chunk in view draws its trees per kind - full trees near the camera, fewer and bigger leaf cards
    // (no trunks) further out, nothing past the fog.
    [ExecuteAlways]
    public class ForestRenderer : MonoBehaviour
    {
        public float nearDistance = 300f, farDistance = 1500f;
        public int layer = 30;

        TreeMeshes.Tree[] kinds;
        Material wood, broadleaf, pine;
        readonly List<Chunk> chunks = new List<Chunk>();
        readonly Plane[] planes = new Plane[6];

        class Chunk
        {
            public Bounds bounds;
            public List<Matrix4x4>[] perKind;
        }

        public void Init(TreeMeshes.Tree[] treeKinds, Material woodMat, Material broadleafMat, Material pineMat)
        {
            kinds = treeKinds;
            wood = woodMat;
            broadleaf = broadleafMat;
            pine = pineMat;
            chunks.Clear();
        }

        public void Add(int chunkIndex, Vector3 centre, float size, int kind, Matrix4x4 m)
        {
            while (chunks.Count <= chunkIndex) chunks.Add(null);
            var c = chunks[chunkIndex];
            if (c == null)
            {
                c = chunks[chunkIndex] = new Chunk { bounds = new Bounds(centre, new Vector3(size, 1f, size)), perKind = new List<Matrix4x4>[kinds.Length] };
            }
            (c.perKind[kind] ??= new List<Matrix4x4>()).Add(m);
            Vector3 p = m.GetColumn(3);
            c.bounds.Encapsulate(new Bounds(p + Vector3.up * 8f, new Vector3(12f, 18f, 12f)));
        }

        public int TreeCount
        {
            get
            {
                int n = 0;
                foreach (var c in chunks)
                    if (c != null)
                        foreach (var l in c.perKind) n += l?.Count ?? 0;
                return n;
            }
        }

        void Start()
        {
            nearDistance = DevFlags.GetFloat("-forestnear", nearDistance);
            farDistance = DevFlags.GetFloat("-forestfar", farDistance);
        }

        // Per-frame batches of the distant trees, per kind.
        List<Matrix4x4>[] farBatch;

        void Update()
        {
            if (kinds == null || kinds[0].wood == null || DevFlags.Has("-noforest")) return;
            var cam = Camera.main;
#if UNITY_EDITOR
            if (!Application.isPlaying && UnityEditor.SceneView.lastActiveSceneView) cam = UnityEditor.SceneView.lastActiveSceneView.camera;
#endif
            if (!cam) return;
            if (farBatch == null || farBatch.Length != kinds.Length)
            {
                farBatch = new List<Matrix4x4>[kinds.Length];
                for (int k = 0; k < kinds.Length; k++) farBatch[k] = new List<Matrix4x4>();
            }
            foreach (var l in farBatch) l.Clear();

            GeometryUtility.CalculateFrustumPlanes(cam, planes);
            Vector3 eye = cam.transform.position;
            var all = new Bounds(eye, Vector3.zero);
            foreach (var c in chunks)
            {
                if (c == null || !GeometryUtility.TestPlanesAABB(planes, c.bounds)) continue;
                float d = Mathf.Sqrt(c.bounds.SqrDistance(eye));
                if (d > farDistance) continue;
                if (d < nearDistance)
                {
                    // Near chunks draw on their own: their tight bounds let shadow cascades cull them.
                    var rp = new RenderParams(wood) { layer = layer, shadowCastingMode = ShadowCastingMode.On, receiveShadows = true, worldBounds = c.bounds };
                    for (int k = 0; k < kinds.Length; k++)
                    {
                        var list = c.perKind[k];
                        if (list == null || list.Count == 0) continue;
                        rp.material = wood;
                        Draw(rp, kinds[k].wood, list);
                        rp.material = kinds[k].pine ? pine : broadleaf;
                        Draw(rp, kinds[k].foliage, list);
                    }
                    continue;
                }
                all.Encapsulate(c.bounds);
                for (int k = 0; k < kinds.Length; k++)
                    if (c.perKind[k] != null) farBatch[k].AddRange(c.perKind[k]);
            }

            // Far trees: one batch per kind, no shadows. (RenderParams must come from the constructor:
            // a default-initialised one has empty culling bounds.)
            var far = new RenderParams(wood) { layer = layer, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = true, worldBounds = all };
            for (int k = 0; k < kinds.Length; k++)
            {
                far.material = kinds[k].pine ? pine : broadleaf;
                Draw(far, kinds[k].foliageFar, farBatch[k]);
            }
        }

        static void Draw(RenderParams rp, Mesh mesh, List<Matrix4x4> list)
        {
            const int Max = 1023;
            for (int start = 0; start < list.Count; start += Max)
                Graphics.RenderMeshInstanced(rp, mesh, 0, list, Mathf.Min(Max, list.Count - start), start);
        }
    }
}
