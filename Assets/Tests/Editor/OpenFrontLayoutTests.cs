using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public sealed class OpenFrontLayoutTests
{
    // Mirrors AStarPathfinder3D: 1m cells, neighbours walkable when the height
    // step is at most maximumNeighborHeightDelta, and cells on a cliff lip
    // (any neighbour beyond that step) blocked entirely.
    private const float MaxStep = 0.75f;

    [Test]
    public void Ramps_AreGentleEnoughToWalk()
    {
        foreach (OpenFrontLayout.Ramp ramp in AllRamps())
        {
            float previous = H(ramp.edge - ramp.direction * 2f);
            for (float s = -1.5f; s <= ramp.length + 2f; s += 1f)
            {
                float current = H(ramp.edge + ramp.direction * s);
                Assert.That(Mathf.Abs(current - previous), Is.LessThanOrEqualTo(MaxStep * 0.85f),
                    $"Ramp at {ramp.edge} is too steep at s={s}");
                previous = current;
            }
        }
    }

    [Test]
    public void PlateauAndPit_AreWalledByCliffsAwayFromRamps()
    {
        // North edge of the fort and west edge of the quarry have no ramp.
        AssertCliff(new Vector2(26f, OpenFrontLayout.PlateauCenter.y + OpenFrontLayout.PlateauHalf.y), Vector2.up);
        AssertCliff(new Vector2(OpenFrontLayout.PitCenter.x + OpenFrontLayout.PitHalf.x, 12f), Vector2.right);
    }

    [Test]
    public void Sites_AreFlatAndAtTheirLevels()
    {
        AssertFlat(OpenFrontLayout.SiteA, OpenFrontLayout.SiteSize, OpenFrontLayout.PlateauHeight);
        AssertFlat(OpenFrontLayout.SiteB, OpenFrontLayout.SiteSize, -OpenFrontLayout.PitDepth);
        AssertFlat(OpenFrontLayout.TSpawn, OpenFrontLayout.SpawnSize, 0f);
        AssertFlat(OpenFrontLayout.CTSpawn, OpenFrontLayout.SpawnSize, 0f);
    }

    [Test]
    public void BothSpawns_CanReachBothSitesAndEachOther()
    {
        HashSet<Vector2Int> fromT = Flood(OpenFrontLayout.TSpawn);
        HashSet<Vector2Int> fromCT = Flood(OpenFrontLayout.CTSpawn);
        foreach (Vector2 target in new[] { OpenFrontLayout.SiteA, OpenFrontLayout.SiteB, OpenFrontLayout.CTSpawn })
        {
            Assert.That(fromT.Contains(Cell(target)), Is.True, $"T spawn cannot reach {target}");
        }
        foreach (Vector2 target in new[] { OpenFrontLayout.SiteA, OpenFrontLayout.SiteB })
        {
            Assert.That(fromCT.Contains(Cell(target)), Is.True, $"CT spawn cannot reach {target}");
        }
    }

    [Test]
    public void Map_HasRealHeightVariety()
    {
        float min = float.MaxValue, max = float.MinValue;
        for (float x = -OpenFrontLayout.PlayHalfX; x <= OpenFrontLayout.PlayHalfX; x += 1f)
        for (float z = -OpenFrontLayout.PlayHalfZ; z <= OpenFrontLayout.PlayHalfZ; z += 1f)
        {
            float h = OpenFrontLayout.Height(x, z);
            if (Mathf.Abs(x) < 34f && Mathf.Abs(z) < 25f) { min = Mathf.Min(min, h); max = Mathf.Max(max, h); }
        }
        Assert.That(max - min, Is.GreaterThan(5f), "High ground and pits should span > 5m");
    }

    [Test]
    public void Vantages_AreReachableAndOverlookTheirTargets()
    {
        HashSet<Vector2Int> fromT = Flood(OpenFrontLayout.TSpawn);
        HashSet<Vector2Int> fromCT = Flood(OpenFrontLayout.CTSpawn);
        foreach ((string name, Vector2 position) in OpenFrontLayout.Vantages)
        {
            Assert.That(fromT.Contains(Cell(position)) && fromCT.Contains(Cell(position)), Is.True,
                $"{name} must be reachable by both teams");
        }

        // The quarry rims look down into B, the fort rim looks down its ramp.
        Assert.That(H(Vantage("Vantage_Quarry_SouthRim")) - H(OpenFrontLayout.SiteB), Is.GreaterThan(2f));
        Assert.That(H(Vantage("Vantage_Fort_SouthRim")) - H(OpenFrontLayout.PlateauRamps[0].Bottom), Is.GreaterThan(2.5f));
        Assert.That(H(Vantage("Vantage_Overlook")), Is.GreaterThan(1f));
    }

    private static Vector2 Vantage(string name)
    {
        foreach ((string vantageName, Vector2 position) in OpenFrontLayout.Vantages)
        {
            if (vantageName == name) return position;
        }
        Assert.Fail("Missing vantage " + name);
        return default;
    }

    private static IEnumerable<OpenFrontLayout.Ramp> AllRamps()
    {
        foreach (var ramp in OpenFrontLayout.PlateauRamps) yield return ramp;
        foreach (var ramp in OpenFrontLayout.PitRamps) yield return ramp;
    }

    private static void AssertCliff(Vector2 rim, Vector2 outward)
    {
        float maxStep = 0f;
        for (float s = -2f; s < 3f; s += 1f)
        {
            maxStep = Mathf.Max(maxStep, Mathf.Abs(H(rim + outward * (s + 1f)) - H(rim + outward * s)));
        }
        Assert.That(maxStep, Is.GreaterThan(MaxStep), $"Edge at {rim} should be an unwalkable cliff");
    }

    private static void AssertFlat(Vector2 center, Vector2 size, float level)
    {
        for (float x = -size.x * 0.5f; x <= size.x * 0.5f; x += 1f)
        for (float z = -size.y * 0.5f; z <= size.y * 0.5f; z += 1f)
        {
            Assert.That(H(center + new Vector2(x, z)), Is.EqualTo(level).Within(0.08f),
                $"Zone at {center} is not flat at offset ({x},{z})");
        }
    }

    private static HashSet<Vector2Int> Flood(Vector2 start)
    {
        HashSet<Vector2Int> visited = new HashSet<Vector2Int>();
        Queue<Vector2Int> open = new Queue<Vector2Int>();
        Vector2Int first = Cell(start);
        visited.Add(first);
        open.Enqueue(first);
        while (open.Count > 0)
        {
            Vector2Int cell = open.Dequeue();
            float height = H(cell);
            for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
            {
                Vector2Int next = new Vector2Int(cell.x + dx, cell.y + dz);
                if ((dx == 0 && dz == 0) || visited.Contains(next) ||
                    Mathf.Abs(next.x) > OpenFrontLayout.PlayHalfX - 1f || Mathf.Abs(next.y) > OpenFrontLayout.PlayHalfZ - 1f ||
                    Mathf.Abs(H(next) - height) > MaxStep || IsCliffLip(next))
                {
                    continue;
                }
                visited.Add(next);
                open.Enqueue(next);
            }
        }
        return visited;
    }

    private static bool IsCliffLip(Vector2Int cell)
    {
        float height = H(cell);
        for (int dx = -1; dx <= 1; dx++)
        for (int dz = -1; dz <= 1; dz++)
        {
            if ((dx != 0 || dz != 0) && Mathf.Abs(H(new Vector2Int(cell.x + dx, cell.y + dz)) - height) > MaxStep)
            {
                return true;
            }
        }
        return false;
    }

    private static Vector2Int Cell(Vector2 p) => new Vector2Int(Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.y));
    private static float H(Vector2 p) => OpenFrontLayout.Height(p.x, p.y);
    private static float H(Vector2Int c) => OpenFrontLayout.Height(c.x, c.y);
}

public sealed class ElevationCombatTests
{
    [Test]
    public void HighGround_TightensSpreadAndUphillWidensIt()
    {
        Assert.That(WeaponSystem.GetElevationSpreadMultiplier(-3f), Is.LessThan(1f));
        Assert.That(WeaponSystem.GetElevationSpreadMultiplier(0.4f), Is.EqualTo(1f));
        Assert.That(WeaponSystem.GetElevationSpreadMultiplier(3f), Is.GreaterThan(1f));
    }
}
