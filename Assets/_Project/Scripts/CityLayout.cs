using UnityEngine;

namespace Racing
{
    // Shared description of the open city: a hilly heightfield with a square street grid on top.
    // Street centre lines run along x = k * Pitch and z = k * Pitch for k = 0..Lines-1.
    public static class CityLayout
    {
        public const float Pitch = 96f;
        public const int Lines = 9;
        public const float RoadHalf = 8f;
        public const float Sidewalk = 4f;
        public const float CurbHeight = 0.15f;
        public const float Margin = 100f;

        public static float Size => Pitch * (Lines - 1);
        public static float Min => -Margin;
        public static float Max => Size + Margin;

        public static Vector3 Intersection(int i, int j)
        {
            float x = i * Pitch, z = j * Pitch;
            return new Vector3(x, Height(x, z), z);
        }

        // Rolling hills plus one big hill north-east of centre. Grades stay under ~15%.
        public static float Height(float x, float z)
        {
            float h = 9f * Mathf.Sin(x / 140f + 1f) * Mathf.Cos(z / 175f)
                    + 5f * Mathf.Sin((x - z) / 105f)
                    + 4f * Mathf.Cos((x + 0.5f * z) / 80f);
            float dx = x - 420f, dz = z - 470f;
            h += 20f * Mathf.Exp(-(dx * dx + dz * dz) / (220f * 220f));
            return h + 12f;
        }

        // Distance from a coordinate to the nearest street centre line (only lines inside the city).
        static float LineDistance(float v)
        {
            float k = Mathf.Clamp(Mathf.Round(v / Pitch), 0, Lines - 1);
            return Mathf.Abs(v - k * Pitch);
        }

        static bool InSpan(float v, float pad) => v > -pad && v < Size + pad;

        public static bool IsRoad(float x, float z)
        {
            return (LineDistance(x) < RoadHalf && InSpan(z, RoadHalf)) || (LineDistance(z) < RoadHalf && InSpan(x, RoadHalf))
                || InExit(x, z, RoadHalf);
        }

        public static bool IsSidewalk(float x, float z)
        {
            if (IsRoad(x, z)) return false;
            float w = RoadHalf + Sidewalk;
            return (LineDistance(x) < w && InSpan(z, w)) || (LineDistance(z) < w && InSpan(x, w)) || InExit(x, z, w);
        }

        // Streets that carry on past the outer ring road to the edge of the city square, where the
        // national roads (WorldLayout) start: east, north, south and west.
        // alongZ: the street runs along z at x = line * Pitch; [from, to] is the stretch outside the grid.
        public static readonly (bool alongZ, int line, float from, float to)[] ExitStrips =
        {
            (false, 4, Pitch * (Lines - 1), Pitch * (Lines - 1) + Margin),
            (true, 4, Pitch * (Lines - 1), Pitch * (Lines - 1) + Margin),
            (true, 1, -Margin, 0f),
            (false, 6, -Margin, 0f),
        };

        public static bool InExit(float x, float z, float half)
        {
            foreach (var e in ExitStrips)
            {
                float across = e.alongZ ? x : z, along = e.alongZ ? z : x;
                if (Mathf.Abs(across - e.line * Pitch) < half && along >= e.from - 0.01f && along <= e.to + 0.01f) return true;
            }
            return false;
        }

        // Blocks are indexed -1..Lines-1; -1 and Lines-1 are the built-up edge blocks outside the outer streets.
        public static bool IsPark(int bi, int bj)
        {
            if (bi < 0 || bj < 0 || bi >= Lines - 1 || bj >= Lines - 1) return false;
            return (bi * 7 + bj * 13) % 11 == 3;
        }

        public static int BlockIndex(float v) => Mathf.Clamp(Mathf.FloorToInt(v / Pitch), -1, Lines - 1);

        public static bool InBounds(Vector3 p, float inset = 0f) =>
            p.x > Min + inset && p.x < Max - inset && p.z > Min + inset && p.z < Max - inset;
    }
}
