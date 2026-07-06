using UnityEngine;

public class InfluenceMapVisualizer : MonoBehaviour
{
    private InfluenceLayerType layerToVisualize = InfluenceLayerType.Danger;
    private Color groundColor = new Color(0.1f, 0.25f, 0.1f, 1.0f);    // Dark Green
    private Color dangerColor = new Color(0.8f, 0.2f, 0.2f, 1.0f);    // Light Red

    private Renderer heatmapRenderer;
    private Texture2D heatmapTexture;
    private AStarPathfinder3D pathfinder;
    private InfluenceMapManager influenceMap;
    private float nextUpdateTime = 0f;

    private void Start()
    {
        pathfinder = AStarPathfinder3D.Instance;
        influenceMap = InfluenceMapManager.Instance;

        if (pathfinder == null) return;

        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "HeatmapOverlay";
        quad.transform.SetParent(transform);
        quad.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        quad.transform.position = pathfinder.transform.position + Vector3.up * 0.05f;
        quad.transform.localScale = new Vector3(pathfinder.gridWidth, pathfinder.gridDepth, 1f);
        Destroy(quad.GetComponent<Collider>());

        heatmapRenderer = quad.GetComponent<Renderer>();
        Material mat = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color"));
        
        // Transparent Blending Setup
        mat.SetFloat("_Surface", 1); 
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        heatmapRenderer.sharedMaterial = mat;

        int sizeX = Mathf.RoundToInt(pathfinder.gridWidth / pathfinder.cellSize);
        int sizeZ = Mathf.RoundToInt(pathfinder.gridDepth / pathfinder.cellSize);
        
        heatmapTexture = new Texture2D(sizeX, sizeZ, TextureFormat.RGBA32, false);
        heatmapTexture.filterMode = FilterMode.Bilinear;
        mat.mainTexture = heatmapTexture;
    }

    private void Update()
    {
        if (DebugVisualManager.Instance == null || pathfinder == null || influenceMap == null) return;
        heatmapRenderer.gameObject.SetActive(DebugVisualManager.Instance.ShowHeatmap);
        if (!DebugVisualManager.Instance.ShowHeatmap || Time.time < nextUpdateTime) return;
        
        nextUpdateTime = Time.time + 0.25f;
        UpdateTexture();
    }

    private void UpdateTexture()
    {
        int sizeX = heatmapTexture.width;
        int sizeZ = heatmapTexture.height;
        Color[] pixels = new Color[sizeX * sizeZ];
        Vector3 bottomLeft = pathfinder.transform.position - new Vector3(pathfinder.gridWidth / 2f, 0f, pathfinder.gridDepth / 2f);

        float maxVal = 0.001f;
        float[,] values = new float[sizeX, sizeZ];

        for (int x = 0; x < sizeX; x++)
        {
            for (int z = 0; z < sizeZ; z++)
            {
                Vector3 worldPos = bottomLeft + new Vector3(x * pathfinder.cellSize + pathfinder.cellSize/2f, 0f, z * pathfinder.cellSize + pathfinder.cellSize/2f);
                values[x, z] = influenceMap.Sample(layerToVisualize, worldPos);
                if (values[x, z] > maxVal) maxVal = values[x, z];
            }
        }

        for (int x = 0; x < sizeX; x++)
        {
            for (int z = 0; z < sizeZ; z++)
            {
                float normalizedValue = values[x, z] / maxVal;
                pixels[z * sizeX + x] = Color.Lerp(groundColor, dangerColor, normalizedValue);
            }
        }

        heatmapTexture.SetPixels(pixels);
        heatmapTexture.Apply();
    }
}