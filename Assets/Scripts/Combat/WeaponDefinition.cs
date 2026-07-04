using UnityEngine;

public enum WeaponType
{
    Rifle,
    SMG,
    Sniper,
    Shotgun
}

[CreateAssetMenu(menuName = "CsAutobattler/Weapon Definition", fileName = "WeaponDefinition")]
public sealed class WeaponDefinition : ScriptableObject
{
    public WeaponType weaponType;
    public float damage = 10f;
    public float fireCooldown = 0.8f;
    public float effectiveMinimumRange = 1f;
    public float effectiveMaximumRange = 12f;
    [Range(0f, 100f)] public float accuracy = 75f;
    [Range(0f, 45f)] public float spreadDegrees = 10f;
    public float projectileSpeed = 18f;
    public float movementSpeedMultiplier = 1f;
    public float preferredCoverDistance = 5f;
    [Header("Fire Pattern")]
    [Min(1)] public int burstCount = 1;
    [Min(0.01f)] public float burstInterval = 0.1f;
    [Min(1)] public int projectilesPerShot = 1;
    [Header("Range Falloff")]
    [Range(0f, 1f)] public float minimumDamageMultiplier = 1f;
    [TextArea] public string behaviorDescription;
}

public static class WeaponDefaults
{
    private static readonly WeaponDefinition[] definitions = new WeaponDefinition[4];

    public static WeaponDefinition Get(WeaponType type)
    {
        int index = (int)type;
        if (definitions[index] != null) return definitions[index];
        WeaponDefinition definition = ScriptableObject.CreateInstance<WeaponDefinition>();
        definition.hideFlags = HideFlags.HideAndDontSave;
        definition.weaponType = type;
        definition.name = type + " Runtime Default";
        switch (type)
        {
            case WeaponType.Rifle:
                Set(definition, 10f, 0.8f, 2f, 14f, 78f, 8f, 20f, 1f, 5f,
                    "Balanced weapon. Reliable in most situations.");
                break;
            case WeaponType.SMG:
                Set(definition, 6f, 0.55f, 0f, 8f, 68f, 15f, 20f, 1.15f, 2.5f,
                    "Rapid three-round bursts with low bullet damage. Strong up close, inaccurate at range.",
                    3, 0.1f);
                break;
            case WeaponType.Sniper:
                Set(definition, 32f, 2.8f, 6f, 25f, 92f, 3f, 30f, 0.85f, 9f,
                    "Very high long-range damage and precision, balanced by a long reload time.");
                break;
            case WeaponType.Shotgun:
                Set(definition, 7f, 1.35f, 0f, 5f, 58f, 20f, 16f, 0.95f, 2f,
                    "Fires eight spread pellets. Devastating nearby, but damage falls sharply with distance.",
                    1, 0.1f, 8, 0.15f);
                break;
        }
        definitions[index] = definition;
        return definition;
    }

    private static void Set(WeaponDefinition d, float damage, float cooldown,
        float minRange, float maxRange, float accuracy, float spread,
        float projectileSpeed, float movement, float coverDistance, string description,
        int burstCount = 1, float burstInterval = 0.1f,
        int projectilesPerShot = 1, float minimumDamageMultiplier = 1f)
    {
        d.damage = damage; d.fireCooldown = cooldown;
        d.effectiveMinimumRange = minRange; d.effectiveMaximumRange = maxRange;
        d.accuracy = accuracy; d.spreadDegrees = spread;
        d.projectileSpeed = projectileSpeed; d.movementSpeedMultiplier = movement;
        d.preferredCoverDistance = coverDistance; d.behaviorDescription = description;
        d.burstCount = burstCount; d.burstInterval = burstInterval;
        d.projectilesPerShot = projectilesPerShot;
        d.minimumDamageMultiplier = minimumDamageMultiplier;
    }
}
