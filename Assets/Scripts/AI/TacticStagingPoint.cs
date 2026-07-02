using UnityEngine;

public enum TacticStagingPurpose
{
    FeintRealGroup
}

/// <summary>
/// Optional map-authored staging marker. Feint and Rotate can work without these,
/// but a level designer can place one to provide a deliberate hidden B setup.
/// </summary>
public sealed class TacticStagingPoint : MonoBehaviour
{
    public BombSiteId site = BombSiteId.B;
    public TacticStagingPurpose purpose = TacticStagingPurpose.FeintRealGroup;
    public bool mustBeOutsideSite = true;
    public bool mustBeHiddenFromDefenders = true;
    [Min(1f)] public float spaceForAgents = 2f;

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.1f, 0.9f, 1f, 0.8f);
        Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.15f, 0.65f);
        Gizmos.DrawLine(
            transform.position + Vector3.left * spaceForAgents * 0.5f,
            transform.position + Vector3.right * spaceForAgents * 0.5f);
    }
}
