using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Racing
{
    // Procedural trees built from photographed bark and alpha-cut foliage cards (see
    // Tools/foliage_cards.py): broadleaf trees with a few limbs and a clumped crown of leaf cards, and
    // conifers with whorls of drooping branch cards. Card normals point away from the crown centre, so
    // the canopy shades like a volume instead of a pile of flat quads.
    public static class TreeMeshes
    {
        public class Tree
        {
            public Mesh wood, foliage, foliageFar; // foliageFar: fewer, bigger cards for distant trees
            public bool pine;
            public float height;
        }

        public static Tree Broadleaf(int seed)
        {
            var rng = new System.Random(seed);
            float R() => (float)rng.NextDouble();
            var wood = new Builder();
            var leaves = new Builder();
            float trunkH = 3.6f + R() * 1.4f;
            Vector3 lean = new Vector3(R() - 0.5f, 0f, R() - 0.5f) * 0.5f;
            Vector3 top = Vector3.up * trunkH + lean;
            wood.Limb(Vector3.down * 0.4f, top, 0.3f, 0.17f, 8);
            Vector3 crown = top + Vector3.up * (1.6f + R() * 0.6f);
            var radii = new Vector3(3f + R() * 0.8f, 2.5f + R() * 0.6f, 3f + R() * 0.8f);
            int limbs = 4 + rng.Next(2);
            for (int i = 0; i < limbs; i++)
            {
                float az = (i + R() * 0.5f) * Mathf.PI * 2f / limbs;
                Vector3 from = Vector3.Lerp(Vector3.zero, top, 0.75f + R() * 0.2f);
                Vector3 to = crown + new Vector3(Mathf.Cos(az) * radii.x * 0.6f, (R() - 0.2f) * radii.y * 0.6f, Mathf.Sin(az) * radii.z * 0.6f);
                wood.Limb(from, to, 0.12f, 0.04f, 5);
            }
            var far = new Builder();
            for (int i = 0; i < 40; i++)
            {
                Vector3 dir = Random3(rng);
                dir.y = dir.y * 0.8f + 0.15f;
                dir.Normalize();
                float shell = 0.5f + 0.5f * Mathf.Sqrt(R());
                Vector3 p = crown + Vector3.Scale(dir, radii) * shell;
                float size = 2.4f + R() * 1.4f;
                Vector3 facing = (Random3(rng) + dir * 0.6f).normalized;
                leaves.Card(p, facing, dir, size, size, 0.3f, crown, 0.75f);
                if (i % 4 == 0) far.Card(crown + Vector3.Scale(dir, radii) * 0.55f, facing, dir, size * 1.9f, size * 1.9f, 0.4f, crown, 0.75f);
            }
            return new Tree { wood = wood.Build(), foliage = leaves.Build(), foliageFar = far.Build(), pine = false, height = crown.y + radii.y };
        }

        public static Tree Pine(int seed)
        {
            var rng = new System.Random(seed);
            float R() => (float)rng.NextDouble();
            var wood = new Builder();
            var needles = new Builder();
            float height = 10f + R() * 5f;
            float reach = 2.6f + R() * 0.9f;
            wood.Limb(Vector3.down * 0.4f, Vector3.up * height, 0.32f, 0.04f, 7);
            float y0 = 1.6f + R() * 0.8f, step = 0.68f;
            var far = new Builder();
            int whorl = 0;
            for (float y = y0; y < height - 0.6f; y += step, whorl++)
            {
                float t = (y - y0) / (height - y0);
                float len = reach * Mathf.Pow(1f - t, 0.85f) + 0.5f;
                int count = t > 0.8f ? 4 : 6;
                for (int b = 0; b < count; b++)
                {
                    float az = (b + whorl * 0.47f + R() * 0.35f) * Mathf.PI * 2f / count;
                    Vector3 outward = new Vector3(Mathf.Cos(az), 0f, Mathf.Sin(az));
                    float droop = Mathf.Lerp(0.5f, 0.15f, t) + R() * 0.2f;
                    Vector3 along = (outward - Vector3.up * droop).normalized;
                    Vector3 basePt = Vector3.up * y;
                    Vector3 normal = (outward * 0.45f + Vector3.up * 0.9f).normalized;
                    // Flat spray plus a steeper one rolled about the branch, so it has body from the side.
                    needles.Branch(basePt, along, len * 1.15f, len * 0.95f, normal, 0f);
                    if ((b + whorl) % 2 == 0) needles.Branch(basePt, along, len * 1.05f, len * 0.85f, normal, 55f * (b % 4 == 0 ? 1f : -1f));
                    if (whorl % 3 == 0 && b % 2 == 0) far.Branch(basePt, along, len * 1.3f, len * 1.6f, normal, 0f);
                }
            }
            // Leader at the top.
            Vector3 tip = Vector3.up * (height - 1.4f);
            foreach (var bld in new[] { needles, far })
            {
                bld.Card(tip + Vector3.up * 0.7f, Vector3.right, Vector3.up, 1.6f, 1.6f, 0.5f, tip, 0f);
                bld.Card(tip + Vector3.up * 0.7f, Vector3.forward, Vector3.up, 1.6f, 1.6f, 0.5f, tip, 0f);
            }
            return new Tree { wood = wood.Build(), foliage = needles.Build(), foliageFar = far.Build(), pine = true, height = height };
        }

        static Vector3 Random3(System.Random rng)
        {
            while (true)
            {
                var v = new Vector3((float)rng.NextDouble() * 2f - 1f, (float)rng.NextDouble() * 2f - 1f, (float)rng.NextDouble() * 2f - 1f);
                float m = v.sqrMagnitude;
                if (m > 0.01f && m <= 1f) return v / Mathf.Sqrt(m);
            }
        }

        class Builder
        {
            readonly List<Vector3> v = new List<Vector3>();
            readonly List<Vector3> n = new List<Vector3>();
            readonly List<Vector2> uv = new List<Vector2>();
            readonly List<int> t = new List<int>();

            // Tapered round limb from a to b; bark UVs in metres (u around twice, v up).
            public void Limb(Vector3 a, Vector3 b, float r0, float r1, int sides)
            {
                Vector3 axis = (b - a).normalized;
                Vector3 side = Vector3.Cross(axis, Mathf.Abs(axis.y) > 0.9f ? Vector3.right : Vector3.up).normalized;
                Vector3 side2 = Vector3.Cross(axis, side);
                float len = Vector3.Distance(a, b);
                int start = v.Count;
                for (int k = 0; k <= sides; k++)
                {
                    float ang = k * Mathf.PI * 2f / sides;
                    Vector3 d = side * Mathf.Cos(ang) + side2 * Mathf.Sin(ang);
                    v.Add(a + d * r0); n.Add(d); uv.Add(new Vector2(k * 2f / sides, 0f));
                    v.Add(b + d * r1); n.Add(d); uv.Add(new Vector2(k * 2f / sides, len / 2f));
                }
                for (int k = 0; k < sides; k++)
                {
                    int i = start + k * 2;
                    t.Add(i); t.Add(i + 1); t.Add(i + 3);
                    t.Add(i); t.Add(i + 3); t.Add(i + 2);
                }
            }

            // Square leaf card centred near p, facing 'facing', its image's bottom (twig end) towards
            // the crown centre. Normals blend outward from 'centre' with up.
            public void Card(Vector3 p, Vector3 facing, Vector3 outward, float w, float h, float baseFrac, Vector3 centre, float spherical)
            {
                Vector3 up = outward - facing * Vector3.Dot(outward, facing);
                if (up.sqrMagnitude < 1e-3f) up = Vector3.up - facing * facing.y;
                up.Normalize();
                Vector3 right = Vector3.Cross(up, facing).normalized;
                Vector3 b0 = p - up * h * baseFrac, b1 = p + up * h * (1f - baseFrac);
                Quad(b0 - right * w * 0.5f, b0 + right * w * 0.5f, b1 + right * w * 0.5f, b1 - right * w * 0.5f, centre, spherical);
            }

            // Conifer branch card: image u runs from the trunk (base) to the tip, the twig at v ~ 0.52.
            public void Branch(Vector3 basePt, Vector3 along, float length, float width, Vector3 normal, float roll)
            {
                Vector3 side = Quaternion.AngleAxis(roll, along) * Vector3.Cross(Vector3.up, along).normalized;
                Vector3 lo = side * (-0.52f * width), hi = side * (0.48f * width);
                int s = v.Count;
                v.Add(basePt + lo); v.Add(basePt + along * length + lo); v.Add(basePt + along * length + hi); v.Add(basePt + hi);
                uv.Add(new Vector2(0f, 0f)); uv.Add(new Vector2(1f, 0f)); uv.Add(new Vector2(1f, 1f)); uv.Add(new Vector2(0f, 1f));
                for (int k = 0; k < 4; k++) n.Add(normal);
                t.Add(s); t.Add(s + 3); t.Add(s + 2);
                t.Add(s); t.Add(s + 2); t.Add(s + 1);
            }

            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 centre, float spherical)
            {
                int s = v.Count;
                v.Add(a); v.Add(b); v.Add(c); v.Add(d);
                uv.Add(new Vector2(0f, 0f)); uv.Add(new Vector2(1f, 0f)); uv.Add(new Vector2(1f, 1f)); uv.Add(new Vector2(0f, 1f));
                foreach (var q in new[] { a, b, c, d })
                    n.Add(Vector3.Slerp(Vector3.up, (q - centre).normalized, spherical).normalized);
                t.Add(s); t.Add(s + 3); t.Add(s + 2);
                t.Add(s); t.Add(s + 2); t.Add(s + 1);
            }

            public Mesh Build()
            {
                var m = new Mesh { indexFormat = IndexFormat.UInt32, hideFlags = HideFlags.DontSave };
                m.SetVertices(v);
                m.SetNormals(n);
                m.SetUVs(0, uv);
                m.SetTriangles(t, 0);
                m.RecalculateBounds();
                return m;
            }
        }
    }
}
