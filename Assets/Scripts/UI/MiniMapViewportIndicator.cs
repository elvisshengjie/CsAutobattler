using UnityEngine;

public class MiniMapViewportIndicator : MonoBehaviour
{
    [Header("References")]
    public Camera mainCamera;
    public SpriteRenderer floorRenderer;
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

        Bounds mapBounds = floorRenderer.bounds;

        float cameraHalfHeight = mainCamera.orthographicSize;
        float cameraHalfWidth = cameraHalfHeight * mainCamera.aspect;

        Vector2 cameraCenter = mainCamera.transform.position;

        float normalizedCenterX = Mathf.InverseLerp(mapBounds.min.x, mapBounds.max.x, cameraCenter.x);
        float normalizedCenterY = Mathf.InverseLerp(mapBounds.min.y, mapBounds.max.y, cameraCenter.y);

        float normalizedWidth = (cameraHalfWidth * 2f) / mapBounds.size.x;
        float normalizedHeight = (cameraHalfHeight * 2f) / mapBounds.size.y;

        float miniMapWidth = miniMapDisplay.rect.width;
        float miniMapHeight = miniMapDisplay.rect.height;

        float frameWidth = Mathf.Max(normalizedWidth * miniMapWidth, minimumFrameSize);
        float frameHeight = Mathf.Max(normalizedHeight * miniMapHeight, minimumFrameSize);

        float frameX = normalizedCenterX * miniMapWidth;
        float frameY = normalizedCenterY * miniMapHeight;

        viewportFrame.anchorMin = new Vector2(0f, 0f);
        viewportFrame.anchorMax = new Vector2(0f, 0f);
        viewportFrame.pivot = new Vector2(0.5f, 0.5f);

        viewportFrame.anchoredPosition = new Vector2(frameX, frameY);
        viewportFrame.sizeDelta = new Vector2(frameWidth, frameHeight);
    }
}