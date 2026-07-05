using UnityEngine;

[DisallowMultipleComponent]
public sealed class WeaponLoadout : MonoBehaviour
{
    [SerializeField] private WeaponType selectedWeapon = WeaponType.Rifle;
    [SerializeField] private WeaponDefinition customDefinition;

    public WeaponType SelectedWeapon => selectedWeapon;
    public WeaponDefinition Definition => customDefinition != null &&
        customDefinition.weaponType == selectedWeapon
        ? customDefinition : WeaponDefaults.Get(selectedWeapon);
    public float Damage => Definition.damage;
    public float FireCooldown => Definition.fireCooldown;
    public float MinimumRange => Definition.effectiveMinimumRange;
    public float MaximumRange => Definition.effectiveMaximumRange;
    public float Accuracy => Definition.accuracy;
    public float SpreadDegrees => Definition.spreadDegrees;
    public float ProjectileSpeed => Definition.projectileSpeed;
    public float MovementSpeedMultiplier => Definition.movementSpeedMultiplier;
    public float PreferredCoverDistance => Definition.preferredCoverDistance;
    public int magazineSize => Definition.magazineSize;
    public float ReloadTime => Definition.reloadTime;
    public int BurstCount => Definition.burstCount;
    public float BurstInterval => Definition.burstInterval;
    public int ProjectilesPerShot => Definition.projectilesPerShot;
    public float MinimumDamageMultiplier => Definition.minimumDamageMultiplier;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AttachToAgents()
    {
        RoundManager roundManager = FindFirstObjectByType<RoundManager>();
        foreach (AgentStats agent in FindObjectsByType<AgentStats>(FindObjectsInactive.Include))
        {
            WeaponLoadout loadout = agent.GetComponent<WeaponLoadout>();
            if (loadout == null) loadout = agent.gameObject.AddComponent<WeaponLoadout>();

            // The non-attacking team receives varied defensive loadouts. Attacker
            // weapons remain untouched so the preparation UI selection is preserved.
            if (roundManager != null && agent.team != roundManager.attackingTeam)
            {
                loadout.SelectWeapon((WeaponType)Random.Range(0, 4));
                AgentRole role = agent.GetComponent<AgentRole>();
                if (role == null) role = agent.gameObject.AddComponent<AgentRole>();
                role.SetRole((AgentRoleType)Random.Range(0, 4));
            }
        }
    }

    public void SelectWeapon(WeaponType type)
    {
        selectedWeapon = type;
    }

    public static WeaponLoadout Get(GameObject agent)
    {
        if (agent == null) return null;
        WeaponLoadout loadout = agent.GetComponent<WeaponLoadout>();
        return loadout != null ? loadout : agent.AddComponent<WeaponLoadout>();
    }
}
