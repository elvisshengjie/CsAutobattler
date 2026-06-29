using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class BombCarrier : MonoBehaviour
{
    [SerializeField] private BombController bomb;
    [SerializeField] private bool isPlanting;

    private HealthSystem health;

    public bool HasBomb => bomb != null &&
                           (bomb.CurrentState == BombState.Carried ||
                            bomb.CurrentState == BombState.Planting);
    public bool IsPlanting => isPlanting;
    public BombController Bomb => bomb;
    public bool IsAlive => health != null && !health.IsDead;

    private void Awake()
    {
        health = GetComponent<HealthSystem>();
    }

    private void OnEnable()
    {
        if (health == null)
        {
            health = GetComponent<HealthSystem>();
        }

        if (health != null)
        {
            health.Died += OnCarrierDied;
        }
    }

    private void OnDisable()
    {
        if (health != null)
        {
            health.Died -= OnCarrierDied;
        }
    }

    private void Update()
    {
        if (HasBomb && !isPlanting && PlantTestKeyPressed())
        {
            BombSite site = ObjectiveManager.Instance?.FindSiteContaining(gameObject);
            if (site != null)
            {
                ObjectiveManager.Instance.BeginPlant(this, site);
            }
        }
    }

    public void RequestPlant(BombSite site)
    {
        ObjectiveManager.Instance?.BeginPlant(this, site);
    }

    public void CancelPlant()
    {
        ObjectiveManager.Instance?.CancelPlant(this);
    }

    public void AssignBomb(BombController assignedBomb)
    {
        bomb = assignedBomb;
    }

    public void ClearBombReference(BombController clearedBomb)
    {
        if (bomb == clearedBomb)
        {
            bomb = null;
            isPlanting = false;
        }
    }

    public void SetPlanting(bool planting)
    {
        isPlanting = planting;
    }

    private void OnCarrierDied(HealthSystem deadHealth)
    {
        if (bomb == null)
        {
            return;
        }

        ObjectiveManager.Instance?.CancelPlant(this);
        bomb.Drop(transform.position);
        isPlanting = false;
    }

    private static bool PlantTestKeyPressed()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.pKey.wasPressedThisFrame)
        {
            return true;
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKeyDown(KeyCode.P);
#else
        return false;
#endif
    }
}
