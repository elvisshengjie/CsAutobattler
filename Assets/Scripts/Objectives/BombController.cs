using UnityEngine;

public enum BombState
{
    Carried,
    Dropped,
    Planting,
    Planted,
    Defused,
    Exploded
}

[RequireComponent(typeof(Collider))]
public class BombController : MonoBehaviour
{
    public Vector3 carryOffset = new Vector3(0f, 1.25f, 0f);

    [Header("Runtime (Read Only)")]
    [SerializeField] private BombState currentState = BombState.Dropped;
    [SerializeField] private BombCarrier currentCarrier;
    [SerializeField] private BombSite plantedSite;

    public BombState CurrentState => currentState;
    public BombCarrier CurrentCarrier => currentCarrier;
    public BombSite PlantedSite => plantedSite;
    public float TimeRemaining => RoundManager.Instance != null
        ? RoundManager.Instance.BombTimeRemaining
        : 0f;

    private Collider pickupCollider;
    private TextMesh countdownText;
    private Camera targetCamera;

    private void Awake()
    {
        pickupCollider = GetComponent<Collider>();
        pickupCollider.isTrigger = true;
        CreateCountdownText();
    }

    private void LateUpdate()
    {
        if ((currentState == BombState.Carried || currentState == BombState.Planting) &&
            currentCarrier != null)
        {
            transform.position = currentCarrier.transform.position + carryOffset;
        }

        UpdateCountdownText();
    }

    public void AssignToCarrier(BombCarrier carrier)
    {
        if (carrier == null)
        {
            return;
        }

        if (currentCarrier != null && currentCarrier != carrier)
        {
            currentCarrier.ClearBombReference(this);
        }

        currentCarrier = carrier;
        plantedSite = null;
        currentState = BombState.Carried;
        pickupCollider.enabled = false;
        carrier.AssignBomb(this);
        transform.position = carrier.transform.position + carryOffset;
    }

    public void BeginPlanting()
    {
        if (currentState == BombState.Carried)
        {
            currentState = BombState.Planting;
        }
    }

    public void CancelPlanting()
    {
        if (currentState == BombState.Planting)
        {
            currentState = BombState.Carried;
        }
    }

    public void CompletePlanting(BombSite site)
    {
        if (currentState != BombState.Planting || site == null)
        {
            return;
        }

        BombCarrier previousCarrier = currentCarrier;
        Vector3 nearestPlantPosition = site.GetNearestPlantPosition(
            previousCarrier != null ? previousCarrier.transform.position : site.PlantPosition);
        currentCarrier = null;
        plantedSite = site;
        currentState = BombState.Planted;
        pickupCollider.enabled = false;
        transform.SetParent(null);
        transform.position = nearestPlantPosition;
        previousCarrier?.ClearBombReference(this);
    }

    public void Drop(Vector3 dropPosition)
    {
        if (currentState != BombState.Carried && currentState != BombState.Planting)
        {
            return;
        }

        BombCarrier previousCarrier = currentCarrier;
        currentCarrier = null;
        plantedSite = null;
        currentState = BombState.Dropped;
        transform.SetParent(null);
        transform.position = dropPosition + Vector3.up * 0.2f;
        pickupCollider.enabled = true;
        previousCarrier?.ClearBombReference(this);
    }

    public void Defuse()
    {
        if (currentState != BombState.Planted)
        {
            return;
        }

        currentState = BombState.Defused;
        RoundManager.Instance?.NotifyBombDefused();
    }

    public void Explode()
    {
        if (currentState != BombState.Planted)
        {
            return;
        }

        currentState = BombState.Exploded;
        RoundManager.Instance?.NotifyBombExploded();
    }

    private void CreateCountdownText()
    {
        GameObject textObject = new GameObject("BombCountdown");
        textObject.transform.SetParent(transform, false);
        textObject.transform.localPosition = new Vector3(0f, 1.1f, 0f);

        countdownText = textObject.AddComponent<TextMesh>();
        countdownText.anchor = TextAnchor.MiddleCenter;
        countdownText.alignment = TextAlignment.Center;
        countdownText.fontSize = 72;
        countdownText.characterSize = 0.055f;
        countdownText.fontStyle = FontStyle.Bold;
        countdownText.color = new Color(1f, 0.25f, 0.1f);
        countdownText.text = string.Empty;
        textObject.SetActive(false);
    }

    private void UpdateCountdownText()
    {
        if (countdownText == null)
        {
            return;
        }

        bool showCountdown = currentState == BombState.Planted &&
                             RoundManager.Instance != null;
        countdownText.gameObject.SetActive(showCountdown);
        if (!showCountdown)
        {
            return;
        }

        countdownText.text = $"BOMB {TimeRemaining:0.0}s";

        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }

        if (targetCamera != null)
        {
            countdownText.transform.rotation = Quaternion.LookRotation(
                countdownText.transform.position - targetCamera.transform.position,
                targetCamera.transform.up);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (currentState != BombState.Dropped)
        {
            return;
        }

        AgentStats agent = other.GetComponentInParent<AgentStats>();
        if (agent != null)
        {
            ObjectiveManager.Instance?.TryPickupBomb(agent.gameObject);
        }
    }
}
