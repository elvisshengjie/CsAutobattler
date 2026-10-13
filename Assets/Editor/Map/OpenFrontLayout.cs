using UnityEngine;

/// <summary>
/// Pure height/paint functions for the "Open Front" battlefield: an open,
/// multi-level map in the spirit of Running With Rifles. Everything is
/// analytic so the builder, the mesh and the tests agree exactly.
///
/// Layout (x east, z north; playable area x ±38.5, z ±28.5):
///   A site  - "Hilltop Fort": plateau +3.2m, cliffs, ramps south and west.
///   B site  - "Quarry": pit -2.6m, cliffs, ramps south and east.
///   Mid     - "Overlook": a gentle hill that can be climbed from any side.
///   Mid     - zig-zag trench (-1.4m) in two halves with a gap in the middle.
///   T spawn south, CT spawn north next to a walled compound.
/// </summary>
public static class OpenFrontLayout
{
    public const float HalfX = 40f;
    public const float HalfZ = 30f;
    public const float PlayHalfX = 38.5f;
    public const float PlayHalfZ = 28.5f;

    public const float PlateauHeight = 3.2f;
    public const float PitDepth = 2.6f;
    public const float TrenchDepth = 1.4f;
    public const float CliffWidth = 1.2f;

    public static readonly Vector2 PlateauCenter = new Vector2(26f, 16f);
    public static readonly Vector2 PlateauHalf = new Vector2(10f, 8f);
    public static readonly Vector2 PitCenter = new Vector2(-26f, 16f);
    public static readonly Vector2 PitHalf = new Vector2(9.5f, 7.5f);
    public static readonly Vector2 HillCenter = new Vector2(-2f, 1f);
    public const float HillHeight = 1.6f;
    public const float HillSigma = 4.5f;

    public static readonly Vector2 SiteA = new Vector2(27f, 17f);
    public static readonly Vector2 SiteB = new Vector2(-26f, 16f);
    public static readonly Vector2 SiteSize = new Vector2(11f, 9f);
    public static readonly Vector2 TSpawn = new Vector2(-4f, -22.5f);
    public static readonly Vector2 CTSpawn = new Vector2(2f, 23.5f);
    public static readonly Vector2 SpawnSize = new Vector2(16f, 8f);
    public static readonly Vector2 CompoundCenter = new Vector2(5f, 14f);
    public static readonly Vector2 CompoundHalf = new Vector2(6f, 4.5f);

    public struct Ramp
    {
        public Vector2 edge;      // point on the feature's rim
        public Vector2 direction; // outward, unit length
        public float width;
        public float length;

        public Ramp(Vector2 edge, Vector2 direction, float width, float length)
        {
            this.edge = edge;
            this.direction = direction.normalized;
            this.width = width;
            this.length = length;
        }

        public Vector2 Bottom => edge + direction * length;
    }

    public static readonly Ramp[] PlateauRamps =
    {
        new Ramp(new Vector2(23f, 8f), Vector2.down, 5f, 8f),   // toward T side
        new Ramp(new Vector2(16f, 18f), Vector2.left, 5f, 6f)   // toward mid / CT
    };

    public static readonly Ramp[] PitRamps =
    {
        new Ramp(new Vector2(-29f, 8.5f), Vector2.down, 5f, 7f), // toward T side
        new Ramp(new Vector2(-16.5f, 20f), Vector2.right, 5f, 7f) // toward CT
    };

    public static readonly Vector2[][] Trenches =
    {
        new[] { new Vector2(-16f, -7f), new Vector2(-10f, -4.5f), new Vector2(-4.5f, -8f) },
        new[] { new Vector2(4.5f, -8f), new Vector2(10f, -4.5f), new Vector2(16f, -7f) }
    };
    public const float TrenchHalfWidth = 1.1f;

    public static readonly Vector2[][] Roads =
    {
        // T spawn north through the trench gap to the overlook.
        new[] { new Vector2(-4f, -19f), new Vector2(-2f, -12f), new Vector2(0f, -7f), new Vector2(-1f, 0f) },
        // Overlook west to the quarry's south ramp.
        new[] { new Vector2(-3f, -1f), new Vector2(-14f, 0f), new Vector2(-24f, 0.5f), new Vector2(-29f, 1.5f) },
        // Overlook east to the fort's south ramp.
        new[] { new Vector2(1f, -1f), new Vector2(12f, -2f), new Vector2(23f, -1f), new Vector2(23f, 0f) },
        // CT spawn to the fort's west ramp.
        new[] { new Vector2(5f, 20.5f), new Vector2(10f, 18f) },
        // CT spawn west to the quarry's east ramp.
        new[] { new Vector2(2f, 21f), new Vector2(-4f, 20.5f), new Vector2(-9.5f, 20f) },
        // Compound south door to the overlook.
        new[] { new Vector2(3.5f, 9f), new Vector2(1f, 5f), new Vector2(-1f, 1f) }
    };
    public const float RoadHalfWidth = 1.4f;

    /// <summary>
    /// High-ground positions for the AI (see HighGround): each sits about 2.5m
    /// back from a cliff edge (clear of the pathfinder's cliff-lip margin) or on a hilltop, on walkable ground.
    /// </summary>
    public static readonly (string name, Vector2 position)[] Vantages =
    {
        ("Vantage_Overlook", HillCenter),
        ("Vantage_Fort_SouthRim", new Vector2(28.5f, 9.4f)),   // above the south ramp
        ("Vantage_Fort_WestRim", new Vector2(18.5f, 21.5f)),   // above the CT side
        ("Vantage_Quarry_SouthRim", new Vector2(-22.5f, 5.9f)), // looks down into B
        ("Vantage_Quarry_EastRim", new Vector2(-13.9f, 12.5f)),
        ("Vantage_Quarry_NorthRim", new Vector2(-27f, 26.2f))
    };

    /// <summary>Round length for this map: it is larger and more open than the blockout.</summary>
    public const float RoundDuration = 210f;

    // --- Height -------------------------------------------------------------

    public static float Height(float x, float z)
    {
        Vector2 p = new Vector2(x, z);
        float flat = FlatMask(p);
        float h = (Noise(x, z) + Hill(p)) * (1f - flat);
        h = Mathf.Max(h, Plateau(p));
        h += Mathf.Min(Pit(p), Trench(p));
        h += Berm(p);
        return h;
    }

    public static float Plateau(Vector2 p)
    {
        float d = SdRoundRect(p - PlateauCenter, PlateauHalf, 2.5f);
        float value = PlateauHeight * (1f - Smooth01((d + 0.2f) / CliffWidth));
        foreach (Ramp ramp in PlateauRamps)
        {
            value = Mathf.Max(value, PlateauHeight * RampProfile(ramp, p));
        }
        return value;
    }

    public static float Pit(Vector2 p)
    {
        float d = SdRoundRect(p - PitCenter, PitHalf, 2f);
        float value = -PitDepth * (1f - Smooth01((d + 0.2f) / CliffWidth));
        foreach (Ramp ramp in PitRamps)
        {
            value = Mathf.Min(value, -PitDepth * RampProfile(ramp, p));
        }
        return value;
    }

    /// <summary>1 at the rim, falling linearly to 0 at the ramp's foot.</summary>
    private static float RampProfile(Ramp ramp, Vector2 p)
    {
        Vector2 local = p - ramp.edge;
        float along = Vector2.Dot(local, ramp.direction);
        float across = Mathf.Abs(local.x * ramp.direction.y - local.y * ramp.direction.x);
        if (along < -1.5f)
        {
            return 0f;
        }
        float side = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(ramp.width * 0.5f - 0.2f, ramp.width * 0.5f + 0.7f, across));
        float slope = 1f - Mathf.Clamp01(along / ramp.length);
        return slope * side;
    }

    public static float Hill(Vector2 p)
    {
        float d2 = (p - HillCenter).sqrMagnitude;
        return HillHeight * Mathf.Exp(-d2 / (2f * HillSigma * HillSigma));
    }

    public static float Trench(Vector2 p)
    {
        float value = 0f;
        foreach (Vector2[] line in Trenches)
        {
            float distance = DistanceToPolyline(line, p, out float u);
            float ends = Mathf.SmoothStep(0f, 1f, u / 0.24f) * Mathf.SmoothStep(0f, 1f, (1f - u) / 0.24f);
            float profile = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(TrenchHalfWidth - 0.2f, TrenchHalfWidth + 1f, distance));
            value = Mathf.Min(value, -TrenchDepth * ends * profile);
        }
        return value;
    }

    public static float Berm(Vector2 p)
    {
        // Invisible walls bound play, so the edge only needs to read as a
        // natural rise into the rolling hills outside, not as a cliff.
        float edge = Mathf.Max(Mathf.Abs(p.x) - (HalfX - 2.5f), Mathf.Abs(p.y) - (HalfZ - 2.5f));
        if (edge <= 0f)
        {
            return 0f;
        }
        float rise = 2.2f * Smooth01(edge / 7f);
        float hills = Mathf.Max(0f, edge - 3f) * 0.18f *
                      (0.6f + 1.4f * Mathf.PerlinNoise(p.x * 0.06f + 40f, p.y * 0.06f + 9f));
        return rise + hills;
    }

    private static float Noise(float x, float z)
    {
        return (Mathf.PerlinNoise(x * 0.08f + 11.3f, z * 0.08f + 3.7f) - 0.5f) * 0.55f +
               (Mathf.PerlinNoise(x * 0.23f + 2.1f, z * 0.23f + 7.9f) - 0.5f) * 0.18f;
    }

    /// <summary>Areas that must be flat: sites, spawns, compound, ramps, trenches.</summary>
    public static float FlatMask(Vector2 p)
    {
        float mask = 0f;
        mask = Mathf.Max(mask, RectMask(p, PlateauCenter, PlateauHalf + Vector2.one * 3f, 2f));
        mask = Mathf.Max(mask, RectMask(p, PitCenter, PitHalf + Vector2.one * 3f, 2f));
        mask = Mathf.Max(mask, RectMask(p, TSpawn, SpawnSize * 0.5f + Vector2.one, 2f));
        mask = Mathf.Max(mask, RectMask(p, CTSpawn, SpawnSize * 0.5f + Vector2.one, 2f));
        mask = Mathf.Max(mask, RectMask(p, CompoundCenter, CompoundHalf + Vector2.one, 2f));
        foreach (Ramp ramp in PlateauRamps) mask = Mathf.Max(mask, RampMask(ramp, p));
        foreach (Ramp ramp in PitRamps) mask = Mathf.Max(mask, RampMask(ramp, p));
        foreach (Vector2[] line in Trenches)
        {
            float distance = DistanceToPolyline(line, p, out _);
            mask = Mathf.Max(mask, 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(2.5f, 4.5f, distance)));
        }
        return mask;
    }

    private static float RampMask(Ramp ramp, Vector2 p)
    {
        Vector2 local = p - ramp.edge;
        float along = Vector2.Dot(local, ramp.direction);
        float across = Mathf.Abs(local.x * ramp.direction.y - local.y * ramp.direction.x);
        float inside = along > -1f && along < ramp.length + 2f ? 1f : 0f;
        return inside * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(ramp.width * 0.5f + 1f, ramp.width * 0.5f + 3f, across)));
    }

    // --- Paint ------------------------------------------------------------

    /// <summary>0 = grass, 1 = dirt.</summary>
    public static float Dirt(float x, float z)
    {
        Vector2 p = new Vector2(x, z);
        float dirt = 0f;
        foreach (Vector2[] road in Roads)
        {
            float distance = DistanceToPolyline(road, p, out _);
            float wobble = (Mathf.PerlinNoise(x * 0.3f, z * 0.3f) - 0.5f) * 0.9f;
            dirt = Mathf.Max(dirt, 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(RoadHalfWidth - 0.4f, RoadHalfWidth + 0.8f, distance + wobble)));
        }
        foreach (Vector2[] line in Trenches)
        {
            float distance = DistanceToPolyline(line, p, out _);
            dirt = Mathf.Max(dirt, 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(TrenchHalfWidth + 0.6f, TrenchHalfWidth + 2f, distance)));
        }
        dirt = Mathf.Max(dirt, 1f - Smooth01((SdRoundRect(p - PitCenter, PitHalf, 2f) + 1f) / 2f));
        dirt = Mathf.Max(dirt, RectMask(p, TSpawn, SpawnSize * 0.5f + Vector2.one * 1.5f, 2.5f) * 0.85f);
        dirt = Mathf.Max(dirt, RectMask(p, CTSpawn, SpawnSize * 0.5f + Vector2.one * 1.5f, 2.5f) * 0.85f);
        dirt = Mathf.Max(dirt, RectMask(p, CompoundCenter, CompoundHalf, 1.5f) * 0.9f);
        // Scattered worn patches break up the grass.
        float patches = Mathf.PerlinNoise(x * 0.11f + 5f, z * 0.11f + 1f);
        dirt = Mathf.Max(dirt, Mathf.InverseLerp(0.68f, 0.85f, patches) * 0.7f);
        return Mathf.Clamp01(dirt);
    }

    // --- Helpers ----------------------------------------------------------

    public static float SdRoundRect(Vector2 p, Vector2 half, float radius)
    {
        Vector2 q = new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y)) - half + Vector2.one * radius;
        Vector2 outside = new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f));
        return outside.magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - radius;
    }

    private static float RectMask(Vector2 p, Vector2 center, Vector2 half, float feather)
    {
        float d = SdRoundRect(p - center, half, 0.5f);
        return 1f - Smooth01(d / feather);
    }

    private static float Smooth01(float t)
    {
        return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
    }

    public static float DistanceToPolyline(Vector2[] line, Vector2 p, out float u)
    {
        float best = float.MaxValue;
        float total = 0f;
        for (int i = 0; i < line.Length - 1; i++) total += Vector2.Distance(line[i], line[i + 1]);
        float travelled = 0f;
        u = 0f;
        for (int i = 0; i < line.Length - 1; i++)
        {
            Vector2 a = line[i];
            Vector2 b = line[i + 1];
            Vector2 ab = b - a;
            float length = ab.magnitude;
            float t = length > 0.0001f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / (length * length)) : 0f;
            float distance = Vector2.Distance(p, a + ab * t);
            if (distance < best)
            {
                best = distance;
                u = total > 0f ? (travelled + t * length) / total : 0f;
            }
            travelled += length;
        }
        return best;
    }
}
