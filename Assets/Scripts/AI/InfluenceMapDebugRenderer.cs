using UnityEngine;

[DisallowMultipleComponent]
public sealed class InfluenceMapDebugRenderer : MonoBehaviour
{
    public bool drawDebug;
    public InfluenceLayerType displayedLayer = InfluenceLayerType.Danger;
    [Range(0.05f, 1f)] public float alpha = 0.25f;

    private void OnDrawGizmos()
    {
        if (Application.isPlaying)
        {
            return;
        }

        if (!drawDebug || InfluenceMapManager.Instance == null) return;
        InfluenceMapManager map = InfluenceMapManager.Instance;
        for (int y = 0; y < map.Height; y++) for (int x = 0; x < map.Width; x++)
        {
            Vector3 center = map.GetCellCenter(x, y);
            float value = map.Sample(displayedLayer, center);
            if (Mathf.Abs(value) < 0.02f) continue;
            Gizmos.color = value >= 0f
                ? new Color(1f, 0.15f, 0.05f, Mathf.Clamp01(Mathf.Abs(value)) * alpha)
                : new Color(0.1f, 0.4f, 1f, Mathf.Clamp01(Mathf.Abs(value)) * alpha);
            Gizmos.DrawCube(center + Vector3.up * 0.04f,
                new Vector3(map.CellSize * 0.92f, 0.05f, map.CellSize * 0.92f));
        }
    }
}
