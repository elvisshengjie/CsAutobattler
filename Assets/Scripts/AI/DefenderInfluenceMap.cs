using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Small, query-based influence field used by defender decisions. Positive sources
/// attract agents and negative sources repel them; influence fades to zero at radius.
/// </summary>
public sealed class DefenderInfluenceMap
{
    private struct Source
    {
        public Vector3 position;
        public float strength;
        public float radius;
    }

    private readonly List<Source> sources = new List<Source>();

    public void Clear()
    {
        sources.Clear();
    }

    public void Add(Vector3 position, float strength, float radius)
    {
        if (radius <= 0.01f || Mathf.Approximately(strength, 0f))
        {
            return;
        }

        position.y = 0f;
        sources.Add(new Source
        {
            position = position,
            strength = strength,
            radius = radius
        });
    }

    public float Evaluate(Vector3 position)
    {
        position.y = 0f;
        float total = 0f;
        foreach (Source source in sources)
        {
            float distance = Vector3.Distance(position, source.position);
            if (distance >= source.radius)
            {
                continue;
            }

            float normalized = 1f - distance / source.radius;
            total += source.strength * normalized * normalized;
        }

        return total;
    }
}
