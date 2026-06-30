using UnityEngine;

public sealed class AgentPortraitData : MonoBehaviour
{
    public Sprite portraitSprite;
    public Color fallbackPortraitColor = Color.white;
    public Renderer sourceRenderer;

    public Color GetPortraitColor()
    {
        return TryGetRendererColor(sourceRenderer, out Color rendererColor)
            ? rendererColor
            : fallbackPortraitColor;
    }

    public Sprite GetPortraitSprite()
    {
        return portraitSprite;
    }

    internal static bool TryGetRendererColor(Renderer renderer, out Color color)
    {
        color = Color.white;

        if (renderer == null)
        {
            return false;
        }

        Material material = renderer.sharedMaterial;
        if (material == null)
        {
            return false;
        }

        if (material.HasProperty("_BaseColor"))
        {
            color = material.GetColor("_BaseColor");
            return true;
        }

        if (material.HasProperty("_Color"))
        {
            color = material.GetColor("_Color");
            return true;
        }

        return false;
    }
}
