using UnityEngine;
using UnityEngine.EventSystems;

public class MiniMapClickMover : MonoBehaviour, IPointerClickHandler, IDragHandler
{
    [Header("References")]
    public Camera mainCamera;
    public SpriteRenderer floorRenderer;
    public RectTransform miniMapDisplay;

    [Header("Options")]
    public bool moveWhileDragging = true;

    private void Awake()
    {
        if (miniMapDisplay == null)
        {
            miniMapDisplay = GetComponent<RectTransform>();
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left)
        {
            return;
        }

        MoveCameraToMiniMapPosition(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!moveWhileDragging)
        {
            return;
        }

        if (eventData.button != PointerEventData.InputButton.Left)
        {
            return;
        }

        MoveCameraToMiniMapPosition(eventData);
    }

    private void MoveCameraToMiniMapPosition(PointerEventData eventData)
    {
        if (mainCamera == null || floorRenderer == null || miniMapDisplay == null)
        {
            return;
        }

        bool insideMiniMap = RectTransformUtility.ScreenPointToLocalPointInRectangle(
            miniMapDisplay,
            eventData.position,
            eventData.pressEventCamera,
            out Vector2 localPoint
        );

        if (!insideMiniMap)
        {
            return;
        }

        Rect rect = miniMapDisplay.rect;

        float normalizedX = Mathf.InverseLerp(rect.xMin, rect.xMax, localPoint.x);
        float normalizedY = Mathf.InverseLerp(rect.yMin, rect.yMax, localPoint.y);

        normalizedX = Mathf.Clamp01(normalizedX);
        normalizedY = Mathf.Clamp01(normalizedY);

        Bounds mapBounds = floorRenderer.bounds;

        float targetX = Mathf.Lerp(mapBounds.min.x, mapBounds.max.x, normalizedX);
        float targetY = Mathf.Lerp(mapBounds.min.y, mapBounds.max.y, normalizedY);

        Vector3 cameraPosition = mainCamera.transform.position;

        cameraPosition.x = targetX;
        cameraPosition.y = targetY;

        cameraPosition = ClampCameraToMap(cameraPosition, mapBounds);

        mainCamera.transform.position = cameraPosition;
    }

    private Vector3 ClampCameraToMap(Vector3 cameraPosition, Bounds mapBounds)
    {
        float cameraHalfHeight = mainCamera.orthographicSize;
        float cameraHalfWidth = cameraHalfHeight * mainCamera.aspect;

        float minX = mapBounds.min.x + cameraHalfWidth;
        float maxX = mapBounds.max.x - cameraHalfWidth;
        float minY = mapBounds.min.y + cameraHalfHeight;
        float maxY = mapBounds.max.y - cameraHalfHeight;

        if (minX <= maxX)
        {
            cameraPosition.x = Mathf.Clamp(cameraPosition.x, minX, maxX);
        }
        else
        {
            cameraPosition.x = mapBounds.center.x;
        }

        if (minY <= maxY)
        {
            cameraPosition.y = Mathf.Clamp(cameraPosition.y, minY, maxY);
        }
        else
        {
            cameraPosition.y = mapBounds.center.y;
        }

        return cameraPosition;
    }
}