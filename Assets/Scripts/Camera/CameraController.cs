using UnityEngine;
using UnityEngine.InputSystem;

public class CameraController : MonoBehaviour
{
    [Header("Drag Settings")]
    public float dragSpeed = 1f;

    [Header("Map Bounds")]
    public SpriteRenderer floorRenderer;

    private Camera cam;

    private void Start()
    {
        cam = GetComponent<Camera>();
    }

    private void Update()
    {
        HandleMouseDrag();
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

        position.x = Mathf.Clamp(position.x, minX, maxX);
        position.y = Mathf.Clamp(position.y, minY, maxY);

        transform.position = position;
    }
}