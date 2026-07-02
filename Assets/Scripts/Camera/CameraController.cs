using UnityEngine;
using UnityEngine.InputSystem;

public class CameraController : MonoBehaviour
{
    [Header("Drag Settings")]
    public float dragSpeed = 1f;

    [Header("Zoom Settings")]
    public float zoomSpeed = 3f;
    public float minZoom = 3f;
    public float maxZoom = 12f;

    [Header("Map Bounds")]
    public SpriteRenderer floorRenderer;

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
        HandleMouseZoom();
        ClampCameraPositionToFloor();
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

        float unitsPerPixel = (cam.orthographicSize * 2f) / Screen.height;

        Vector3 movement = new Vector3(
            -mouseDelta.x * unitsPerPixel * dragSpeed,
            -mouseDelta.y * unitsPerPixel * dragSpeed,
            0f
        );

        transform.position += movement;
    }

    private void HandleMouseZoom()
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

        cam.orthographicSize -= scrollValue * zoomSpeed * Time.deltaTime;
        cam.orthographicSize = Mathf.Clamp(cam.orthographicSize, minZoom, maxZoom);
    }

    private void ClampCameraPositionToFloor()
    {
        if (floorRenderer == null || cam == null)
        {
            return;
        }

        Bounds floorBounds = floorRenderer.bounds;

        float cameraHalfHeight = cam.orthographicSize;
        float cameraHalfWidth = cameraHalfHeight * cam.aspect;

        float minX = floorBounds.min.x + cameraHalfWidth;
        float maxX = floorBounds.max.x - cameraHalfWidth;
        float minY = floorBounds.min.y + cameraHalfHeight;
        float maxY = floorBounds.max.y - cameraHalfHeight;

        Vector3 position = transform.position;

        if (minX <= maxX)
        {
            position.x = Mathf.Clamp(position.x, minX, maxX);
        }
        else
        {
            position.x = floorBounds.center.x;
        }

        if (minY <= maxY)
        {
            position.y = Mathf.Clamp(position.y, minY, maxY);
        }
        else
        {
            position.y = floorBounds.center.y;
        }

        transform.position = position;
    }
}
