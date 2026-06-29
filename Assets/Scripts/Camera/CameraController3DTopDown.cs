using UnityEngine;
using UnityEngine.InputSystem;

public class CameraController3DTopDown : MonoBehaviour
{
    [Header("Drag Settings")]
    public float dragSpeed = 1f;

    [Header("Orthographic Zoom Settings")]
    public float zoomSpeed = 8f;
    public float minZoom = 5f;
    public float maxZoom = 35f;

    [Header("Perspective Zoom Settings")]
    public float perspectiveZoomStep = 5f;
    public float minFieldOfView = 25f;
    public float maxFieldOfView = 70f;

    [Header("Map Bounds")]
    public Renderer floorRenderer;

    private Camera cam;

    private void Start()
    {
        cam = GetComponent<Camera>();
    }

    private void Update()
    {
        HandleMouseDrag();
        HandleMouseZoomToCursor();
        ClampCameraToFloorByGroundView();
    }

    private void HandleMouseDrag()
    {
        if (Mouse.current == null || cam == null)
        {
            return;
        }

        if (!Mouse.current.rightButton.isPressed)
        {
            return;
        }

        Vector2 mouseDelta = Mouse.current.delta.ReadValue();

        float unitsPerPixel = GetUnitsPerPixelOnGround();

        Vector3 screenRightOnGround = Vector3.ProjectOnPlane(transform.right, Vector3.up).normalized;
        Vector3 screenUpOnGround = Vector3.ProjectOnPlane(transform.up, Vector3.up).normalized;

        Vector3 movement =
            -screenRightOnGround * mouseDelta.x * unitsPerPixel * dragSpeed
            - screenUpOnGround * mouseDelta.y * unitsPerPixel * dragSpeed;

        transform.position += movement;
    }

    private float GetUnitsPerPixelOnGround()
    {
        if (cam.orthographic)
        {
            return (cam.orthographicSize * 2f) / Screen.height;
        }

        Plane groundPlane = new Plane(Vector3.up, Vector3.zero);
        Ray centerRay = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

        if (!groundPlane.Raycast(centerRay, out float distanceToGround))
        {
            return 0.05f;
        }

        float viewHeightAtGround =
            2f * distanceToGround * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);

        return viewHeightAtGround / Screen.height;
    }

    private void HandleMouseZoomToCursor()
    {
        if (Mouse.current == null || cam == null)
        {
            return;
        }

        float scrollValue = Mouse.current.scroll.ReadValue().y;

        if (Mathf.Abs(scrollValue) < 0.01f)
        {
            return;
        }

        bool hasMouseGroundPointBeforeZoom = TryGetMouseGroundPoint(out Vector3 groundPointBeforeZoom);

        float zoomDirection = Mathf.Sign(scrollValue);

        if (cam.orthographic)
        {
            cam.orthographicSize -= zoomDirection * zoomSpeed * Time.deltaTime * 10f;
            cam.orthographicSize = Mathf.Clamp(cam.orthographicSize, minZoom, maxZoom);
        }
        else
        {
            cam.fieldOfView -= zoomDirection * perspectiveZoomStep;
            cam.fieldOfView = Mathf.Clamp(cam.fieldOfView, minFieldOfView, maxFieldOfView);
        }

        bool hasMouseGroundPointAfterZoom = TryGetMouseGroundPoint(out Vector3 groundPointAfterZoom);

        if (!hasMouseGroundPointBeforeZoom || !hasMouseGroundPointAfterZoom)
        {
            return;
        }

        Vector3 cameraCorrection = groundPointBeforeZoom - groundPointAfterZoom;
        cameraCorrection.y = 0f;

        transform.position += cameraCorrection;
    }

    private bool TryGetMouseGroundPoint(out Vector3 groundPoint)
    {
        groundPoint = Vector3.zero;

        if (Mouse.current == null || cam == null)
        {
            return false;
        }

        Vector2 mouseScreenPosition = Mouse.current.position.ReadValue();

        Ray mouseRay = cam.ScreenPointToRay(mouseScreenPosition);
        Plane groundPlane = new Plane(Vector3.up, Vector3.zero);

        if (!groundPlane.Raycast(mouseRay, out float enter))
        {
            return false;
        }

        groundPoint = mouseRay.GetPoint(enter);
        return true;
    }

    private void ClampCameraToFloorByGroundView()
    {
        if (floorRenderer == null || cam == null)
        {
            return;
        }

        if (!TryGetCameraGroundBounds(out float viewMinX, out float viewMaxX, out float viewMinZ, out float viewMaxZ))
        {
            return;
        }

        Bounds mapBounds = floorRenderer.bounds;

        float viewWidth = viewMaxX - viewMinX;
        float viewDepth = viewMaxZ - viewMinZ;

        float mapWidth = mapBounds.max.x - mapBounds.min.x;
        float mapDepth = mapBounds.max.z - mapBounds.min.z;

        Vector3 correction = Vector3.zero;

        if (viewWidth >= mapWidth)
        {
            float viewCenterX = (viewMinX + viewMaxX) * 0.5f;
            correction.x = mapBounds.center.x - viewCenterX;
        }
        else
        {
            if (viewMinX < mapBounds.min.x)
            {
                correction.x += mapBounds.min.x - viewMinX;
            }

            if (viewMaxX > mapBounds.max.x)
            {
                correction.x += mapBounds.max.x - viewMaxX;
            }
        }

        if (viewDepth >= mapDepth)
        {
            float viewCenterZ = (viewMinZ + viewMaxZ) * 0.5f;
            correction.z = mapBounds.center.z - viewCenterZ;
        }
        else
        {
            if (viewMinZ < mapBounds.min.z)
            {
                correction.z += mapBounds.min.z - viewMinZ;
            }

            if (viewMaxZ > mapBounds.max.z)
            {
                correction.z += mapBounds.max.z - viewMaxZ;
            }
        }

        transform.position += correction;
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

        bool foundPoint = false;

        foreach (Vector3 viewportCorner in viewportCorners)
        {
            Ray ray = cam.ViewportPointToRay(viewportCorner);

            if (!groundPlane.Raycast(ray, out float enter))
            {
                continue;
            }

            Vector3 worldPoint = ray.GetPoint(enter);

            minX = Mathf.Min(minX, worldPoint.x);
            maxX = Mathf.Max(maxX, worldPoint.x);
            minZ = Mathf.Min(minZ, worldPoint.z);
            maxZ = Mathf.Max(maxZ, worldPoint.z);

            foundPoint = true;
        }

        return foundPoint;
    }
}