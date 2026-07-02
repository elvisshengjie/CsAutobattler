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

    [Tooltip("How far the camera focus point can stay away from the map edge.")]
    public float edgePadding = 1f;

    private Camera cam;

    private void Start()
    {
        cam = GetComponent<Camera>();
    }

    private void Update()
    {
        if (TeamTacticManager.Instance != null &&
            TeamTacticManager.Instance.IsInitialSelectionBlockingInput)
        {
            return;
        }

        HandleMouseDrag();
        HandleMouseZoomToCursor();
        ClampCameraFocusPointToFloor();
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

    private bool TryGetCameraFocusGroundPoint(out Vector3 focusPoint)
    {
        focusPoint = Vector3.zero;

        if (cam == null)
        {
            return false;
        }

        Ray centerRay = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        Plane groundPlane = new Plane(Vector3.up, Vector3.zero);

        if (!groundPlane.Raycast(centerRay, out float enter))
        {
            return false;
        }

        focusPoint = centerRay.GetPoint(enter);
        return true;
    }

    private void ClampCameraFocusPointToFloor()
    {
        if (floorRenderer == null || cam == null)
        {
            return;
        }

        if (!TryGetCameraFocusGroundPoint(out Vector3 focusPoint))
        {
            return;
        }

        Bounds mapBounds = floorRenderer.bounds;

        float minX = mapBounds.min.x + edgePadding;
        float maxX = mapBounds.max.x - edgePadding;
        float minZ = mapBounds.min.z + edgePadding;
        float maxZ = mapBounds.max.z - edgePadding;

        Vector3 clampedFocusPoint = focusPoint;

        clampedFocusPoint.x = Mathf.Clamp(focusPoint.x, minX, maxX);
        clampedFocusPoint.z = Mathf.Clamp(focusPoint.z, minZ, maxZ);

        Vector3 correction = clampedFocusPoint - focusPoint;
        correction.y = 0f;

        transform.position += correction;
    }
}
