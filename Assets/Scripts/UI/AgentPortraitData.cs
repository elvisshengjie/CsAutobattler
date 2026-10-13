using System.Collections.Generic;
using UnityEngine;

public sealed class AgentPortraitData : MonoBehaviour
{
    private static readonly Dictionary<string, Sprite> RenderedPortraits =
        new Dictionary<string, Sprite>();

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

    /// <summary>
    /// Portrait rendered from the agent's actual character model and weapon
    /// (Resources/UI/Portrait_{Soldier|Enemy}_{Weapon}, made by UiArtRenderer).
    /// </summary>
    internal static bool TryGetRenderedPortrait(GameObject agent, out Sprite sprite)
    {
        sprite = null;
        if (agent == null)
        {
            return false;
        }

        AgentStats stats = agent.GetComponent<AgentStats>();
        WeaponLoadout loadout = agent.GetComponent<WeaponLoadout>();
        string model = stats == null || stats.team == TeamType.Red ? "Soldier" : "Enemy";
        WeaponType weapon = loadout != null ? loadout.SelectedWeapon : WeaponType.Rifle;
        string key = $"Portrait_{model}_{weapon}";
        if (!RenderedPortraits.TryGetValue(key, out sprite))
        {
            Texture2D texture = Resources.Load<Texture2D>("UI/" + key);
            sprite = texture != null
                ? Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f))
                : null;
            RenderedPortraits[key] = sprite;
        }
        return sprite != null;
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
