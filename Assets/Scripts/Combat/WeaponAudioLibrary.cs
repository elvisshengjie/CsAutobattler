using UnityEngine;

/// <summary>
/// Shared weapon clips and playback tuning. The asset lives in Resources so every
/// WeaponSystem can use it without adding audio references to each agent prefab.
/// </summary>
[CreateAssetMenu(menuName = "CsAutobattler/Weapon Audio Library", fileName = "WeaponAudioLibrary")]
public sealed class WeaponAudioLibrary : ScriptableObject
{
    [SerializeField] private AudioClip[] rifleShots;
    [SerializeField] private AudioClip[] smgShots;
    [SerializeField] private AudioClip[] sniperShots;
    [SerializeField] private AudioClip[] shotgunShots;
    [SerializeField] private AudioClip turretShot;
    [SerializeField] private AudioClip turretDeployment;

    private static WeaponAudioLibrary instance;

    public static WeaponAudioLibrary Instance
    {
        get
        {
            if (instance == null)
            {
                instance = Resources.Load<WeaponAudioLibrary>("WeaponAudioLibrary");
            }

            return instance;
        }
    }

    public AudioClip[] GetShotClips(WeaponType weaponType)
    {
        return weaponType switch
        {
            WeaponType.Rifle => rifleShots,
            WeaponType.SMG => smgShots,
            WeaponType.Sniper => sniperShots,
            WeaponType.Shotgun => shotgunShots,
            _ => null
        };
    }

    public AudioClip TurretShot => turretShot;
    public AudioClip TurretDeployment => turretDeployment;

    public static float GetVolume(WeaponType weaponType)
    {
        // The source SMG recordings are semi-auto rifle shots, so keep them
        // lighter and less dominant than the actual rifle.
        return weaponType switch
        {
            WeaponType.SMG => 0.72f,
            WeaponType.Sniper => 1f,
            WeaponType.Shotgun => 0.95f,
            _ => 0.85f
        };
    }

    public static float GetBasePitch(WeaponType weaponType)
    {
        return weaponType == WeaponType.SMG ? 1.14f : 1f;
    }

    public static float GetMaximumDuration(WeaponType weaponType)
    {
        // Cutting the long rifle tail is the main change that makes the reused
        // semi-auto recordings read as compact SMG shots during a burst.
        return weaponType == WeaponType.SMG ? 0.32f : 0f;
    }
}
