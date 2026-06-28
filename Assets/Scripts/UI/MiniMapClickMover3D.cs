using UnityEngine;
using UnityEngine.EventSystems;

public class MiniMapClickMover3D : MonoBehaviour, IPointerClickHandler, IDragHandler
{
    [Header("References")]
    public Camera mainCamera;
    public Renderer floorRenderer;
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
        float normalizedZ = Mathf.InverseLerp(rect.yMin, rect.yMax, localPoint.y);

        normalizedX = Mathf.Clamp01(normalizedX);
        normalizedZ = Mathf.Clamp01(normalizedZ);

        Bounds mapBounds = floorRenderer.bounds;

        float targetX = Mathf.Lerp(mapBounds.min.x, mapBounds.max.x, normalizedX);
        float targetZ = Mathf.Lerp(mapBounds.min.z, mapBounds.max.z, normalizedZ);

        Vector3 cameraPosition = mainCamera.transform.position;

        Vector2 footprintCenterOffset = GetCameraFootprintCenterOffset();
        Vector2 footprintHalfSize = GetCameraFootprintHalfSize();

        float clampedCenterX = ClampValueOrCenter(
            targetX,
            mapBounds.min.x + footprintHalfSize.x,
            mapBounds.max.x - footprintHalfSize.x,
            mapBounds.center.x
        );

        float clampedCenterZ = ClampValueOrCenter(
            targetZ,
            mapBounds.min.z + footprintHalfSize.y,
            mapBounds.max.z - footprintHalfSize.y,
            mapBounds.center.z
        );

        cameraPosition.x = clampedCenterX - footprintCenterOffset.x;
        cameraPosition.z = clampedCenterZ - footprintCenterOffset.y;

        mainCamera.transform.position = cameraPosition;
    }

    private Vector2 GetCameraFootprintCenterOffset()
    {
        if (!TryGetCameraGroundBounds(out float minX, out float maxX, out float minZ, out float maxZ))
        {
            return Vector2.zero;
        }

        Vector2 footprintCenter = new Vector2(
            (minX + maxX) * 0.5f,
            (minZ + maxZ) * 0.5f
        );

        Vector2 cameraXZ = new Vector2(
            mainCamera.transform.position.x,
            mainCamera.transform.position.z
        );

        return footprintCenter - cameraXZ;
    }

    private Vector2 GetCameraFootprintHalfSize()
    {
        if (!TryGetCameraGroundBounds(out float minX, out float maxX, out float minZ, out float maxZ))
        {
            float halfHeight = mainCamera.orthographicSize;
            float halfWidth = halfHeight * mainCamera.aspect;

            return new Vector2(halfWidth, halfHeight);
        }

        return new Vector2(
            (maxX - minX) * 0.5f,
            (maxZ - minZ) * 0.5f
        );
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

    private float ClampValueOrCenter(float value, float min, float max, float center)
    {
        if (min <= max)
        {
            return Mathf.Clamp(value, min, max);
        }

        return center;
    }
}