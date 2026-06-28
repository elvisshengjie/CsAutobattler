using UnityEngine;

public class MiniMapViewportIndicator3D : MonoBehaviour
{
    [Header("References")]
    public Camera mainCamera;
    public Renderer floorRenderer;
    public RectTransform miniMapDisplay;
    public RectTransform viewportFrame;

    [Header("Frame Style")]
    public float minimumFrameSize = 8f;

    private void LateUpdate()
    {
        if (mainCamera == null || floorRenderer == null || miniMapDisplay == null || viewportFrame == null)
        {
            return;
        }

        if (!TryGetCameraGroundBounds(out float viewMinX, out float viewMaxX, out float viewMinZ, out float viewMaxZ))
        {
            return;
        }

        Bounds mapBounds = floorRenderer.bounds;

        float normalizedMinX = Mathf.InverseLerp(mapBounds.min.x, mapBounds.max.x, viewMinX);
        float normalizedMaxX = Mathf.InverseLerp(mapBounds.min.x, mapBounds.max.x, viewMaxX);

        float normalizedMinZ = Mathf.InverseLerp(mapBounds.min.z, mapBounds.max.z, viewMinZ);
        float normalizedMaxZ = Mathf.InverseLerp(mapBounds.min.z, mapBounds.max.z, viewMaxZ);

        normalizedMinX = Mathf.Clamp01(normalizedMinX);
        normalizedMaxX = Mathf.Clamp01(normalizedMaxX);
        normalizedMinZ = Mathf.Clamp01(normalizedMinZ);
        normalizedMaxZ = Mathf.Clamp01(normalizedMaxZ);

        float miniMapWidth = miniMapDisplay.rect.width;
        float miniMapHeight = miniMapDisplay.rect.height;

        float frameWidth = Mathf.Max(
            (normalizedMaxX - normalizedMinX) * miniMapWidth,
            minimumFrameSize
        );

        float frameHeight = Mathf.Max(
            (normalizedMaxZ - normalizedMinZ) * miniMapHeight,
            minimumFrameSize
        );

        float frameCenterX = ((normalizedMinX + normalizedMaxX) * 0.5f) * miniMapWidth;
        float frameCenterY = ((normalizedMinZ + normalizedMaxZ) * 0.5f) * miniMapHeight;

        viewportFrame.anchorMin = new Vector2(0f, 0f);
        viewportFrame.anchorMax = new Vector2(0f, 0f);
        viewportFrame.pivot = new Vector2(0.5f, 0.5f);

        viewportFrame.anchoredPosition = new Vector2(frameCenterX, frameCenterY);
        viewportFrame.sizeDelta = new Vector2(frameWidth, frameHeight);
    }

    private bool TryGetCameraGroundBounds(out float minX, out float maxX, out float minZ, out float maxZ)
    {
        minX = float.PositiveInfinity;
        maxX = float.NegativeInfinity;
        minZ = float.PositiveInfinity;
        maxZ = float.NegativeInfinity;

        Plane groundPlane = new Plane(Vector3.up, Vector3.zero);

        Vector3[] viewportCorners =
        {
            new Vector3(0f, 0f, 0f),
            new Vector3(0f, 1f, 0f),
            new Vector3(1f, 0f, 0f),
            new Vector3(1f, 1f, 0f)
        };

        bool foundAnyPoint = false;

        foreach (Vector3 viewportCorner in viewportCorners)
        {
            Ray ray = mainCamera.ViewportPointToRay(viewportCorner);

            if (!groundPlane.Raycast(ray, out float enter))
            {
                continue;
            }

            Vector3 worldPoint = ray.GetPoint(enter);

            minX = Mathf.Min(minX, worldPoint.x);
            maxX = Mathf.Max(maxX, worldPoint.x);
            minZ = Mathf.Min(minZ, worldPoint.z);
            maxZ = Mathf.Max(maxZ, worldPoint.z);

            foundAnyPoint = true;
        }

        return foundAnyPoint;
    }
}