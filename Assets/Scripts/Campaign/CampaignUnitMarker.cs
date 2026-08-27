using UnityEngine;

[DisallowMultipleComponent]
public sealed class CampaignUnitMarker : MonoBehaviour
{
    public int CharacterId { get; private set; }
    public CharacterTier Tier { get; private set; }

    public void Configure(int characterId, CharacterTier tier)
    {
        CharacterId = characterId;
        Tier = tier;
    }
}
