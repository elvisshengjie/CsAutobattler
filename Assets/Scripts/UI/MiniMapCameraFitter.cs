using UnityEngine;

public class MiniMapCameraFitter : MonoBehaviour
{
    [Header("References")]
    public SpriteRenderer floorRenderer;

    [Header("Padding")]
    public float padding = 1f;

    private Camera miniMapCamera;

    private void Start()
    {
        miniMapCamera = GetComponent<Camera>();
        FitCameraToFloor();
    }

    private void OnValidate()
    {
        if (Application.isPlaying)
        {
            return;
        }

        miniMapCamera = GetComponent<Camera>();

        if (miniMapCamera != null && floorRenderer != null)
        {
            FitCameraToFloor();
        }
    }

    private void FitCameraToFloor()
    {
        if (miniMapCamera == null || floorRenderer == null)
        {
            return;
        }

        Bounds floorBounds = floorRenderer.bounds;

        transform.position = new Vector3(
            floorBounds.center.x,
            floorBounds.center.y,
            transform.position.z
        );

        float mapHeight = floorBounds.size.y;
        float mapWidth = floorBounds.size.x;

        float cameraAspect = miniMapCamera.aspect;

        float sizeByHeight = mapHeight / 2f;
        float sizeByWidth = mapWidth / (2f * cameraAspect);

        miniMapCamera.orthographic = true;
        miniMapCamera.orthographicSize = Mathf.Max(sizeByHeight, sizeByWidth) + padding;
    }
}