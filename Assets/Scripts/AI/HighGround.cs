using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Shared high-ground knowledge for the tactical AI. Ground heights come from
/// the pathfinding grid's probed surface; vantage points are scene markers
/// named "Vantage_*" placed by the map builder on hilltops and rims. On a flat
/// map every query is neutral, so behaviour there is unchanged.
/// </summary>
public static class HighGround
{
    /// <summary>Height difference that counts as real high ground.</summary>
    public const float MeaningfulAdvantage = 1f;
    private const float MaxCountedAdvantage = 3.5f;

    private static readonly List<Transform> vantages = new List<Transform>();
    private static readonly List<Transform> chokePoints = new List<Transform>();
    private static Scene cachedScene;
    private static float nextVantageRefresh;

    public static IReadOnlyList<Transform> Vantages
    {
        get
        {
            RefreshVantages();
            return vantages;
        }
    }

    /// <summary>Scene markers named "ChokePoint_*" (ramp feet, trench gap).</summary>
    public static IReadOnlyList<Transform> ChokePoints
    {
        get
        {
            RefreshVantages();
            return chokePoints;
        }
    }

    public static float GroundHeight(Vector3 position)
    {
        AStarPathfinder3D grid = AStarPathfinder3D.Instance;
        return grid != null && grid.TryGetGroundHeight(position, out float height)
            ? height
            : 0f;
    }

    /// <summary>Metres the ground at <paramref name="position"/> sits above the ground at <paramref name="other"/>.</summary>
    public static float Advantage(Vector3 position, Vector3 other)
    {
        return GroundHeight(position) - GroundHeight(other);
    }

    /// <summary>
    /// Scoring term for AI position choices: positive for standing above the
    /// threat, negative for standing below it, capped so a cliff top never
    /// outweighs actually being covered.
    /// </summary>
    public static float Score(Vector3 position, Vector3 threat, float aboveWeight, float belowWeight)
    {
        float advantage = Mathf.Clamp(Advantage(position, threat), -MaxCountedAdvantage, MaxCountedAdvantage);
        if (Mathf.Abs(advantage) < 0.35f)
        {
            return 0f; // terrain noise, not a tactical feature
        }
        return advantage > 0f ? advantage * aboveWeight : advantage * belowWeight;
    }

    /// <summary>
    /// Picks the vantage point that overlooks <paramref name="target"/> (or the
    /// approach to it) from above with a clear sight line, within
    /// <paramref name="range"/>, preferring ones close to <paramref name="from"/>.
    /// </summary>
    public static bool TryFindVantage(
        Vector3 target,
        Vector3 approach,
        Vector3 from,
        float range,
        out Transform vantage)
    {
        vantage = null;
        float bestScore = float.NegativeInfinity;
        foreach (Transform candidate in Vantages)
        {
            if (candidate == null)
            {
                continue;
            }

            Vector3 position = candidate.position;
            float overTarget = Advantage(position, target);
            float overApproach = Advantage(position, approach);
            bool seesTarget = overTarget >= MeaningfulAdvantage &&
                              FlatDistance(position, target) <= range &&
                              !DefenderTeamCoordinator.IsLineBlocked(position, target);
            bool seesApproach = overApproach >= MeaningfulAdvantage &&
                                FlatDistance(position, approach) <= range &&
                                !DefenderTeamCoordinator.IsLineBlocked(position, approach);
            if (!seesTarget && !seesApproach)
            {
                continue;
            }

            float score = (seesTarget ? overTarget * 3f : 0f) +
                          (seesApproach ? overApproach * 2f : 0f) -
                          FlatDistance(from, position) * 0.12f;
            if (score > bestScore)
            {
                bestScore = score;
                vantage = candidate;
            }
        }
        return vantage != null;
    }

    private static readonly Dictionary<Vector3, Vector3> approachCache = new Dictionary<Vector3, Vector3>();
    private static Scene approachCacheScene;

    /// <summary>
    /// The chokepoint attackers most likely use to reach a site: the
    /// "ChokePoint_*" within 18m of it closest to the attacker spawn (TSpawn),
    /// else a point 12m from the site toward that spawn.
    /// </summary>
    public static Vector3 GetAttackerApproach(Vector3 site)
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene != approachCacheScene)
        {
            approachCache.Clear();
            approachCacheScene = scene;
        }
        if (approachCache.TryGetValue(site, out Vector3 cached))
        {
            return cached;
        }

        GameObject spawn = GameObject.Find("TSpawn");
        Vector3 attackerSide = spawn != null ? spawn.transform.position : Vector3.zero;
        Transform best = null;
        float bestDistance = float.PositiveInfinity;
        foreach (Transform marker in ChokePoints)
        {
            if (marker == null || FlatDistance(marker.position, site) > 18f)
            {
                continue;
            }
            float distance = FlatDistance(marker.position, attackerSide);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = marker;
            }
        }

        Vector3 approach;
        if (best != null)
        {
            approach = best.position;
        }
        else
        {
            Vector3 toSpawn = attackerSide - site;
            toSpawn.y = 0f;
            approach = site + (toSpawn.sqrMagnitude > 0.01f ? toSpawn.normalized * 12f : Vector3.zero);
        }
        approachCache[site] = approach;
        return approach;
    }

    /// <summary>Preference for overwatch duty: long guns first.</summary>
    public static int OverwatchRank(WeaponType weapon)
    {
        return weapon switch
        {
            WeaponType.Sniper => 3,
            WeaponType.Rifle => 2,
            WeaponType.SMG => 1,
            _ => 0
        };
    }

    private static void RefreshVantages()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene == cachedScene && Time.unscaledTime < nextVantageRefresh)
        {
            return;
        }
        cachedScene = scene;
        nextVantageRefresh = Time.unscaledTime + 5f;
        vantages.Clear();
        chokePoints.Clear();
        foreach (Transform transform in Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude))
        {
            if (transform.name.StartsWith("Vantage_"))
            {
                vantages.Add(transform);
            }
            else if (transform.name.StartsWith("ChokePoint_"))
            {
                chokePoints.Add(transform);
            }
        }
        vantages.Sort((left, right) => string.CompareOrdinal(left.name, right.name));
        chokePoints.Sort((left, right) => string.CompareOrdinal(left.name, right.name));
    }

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }
}
