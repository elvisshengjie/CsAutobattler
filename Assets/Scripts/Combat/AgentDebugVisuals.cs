using UnityEngine;

[RequireComponent(typeof(AgentMotor))]
public class AgentDebugVisual : MonoBehaviour
{
    private AgentMotor motor;
    private LineRenderer pathRenderer;
    private GameObject targetMarker;
    private TextMesh debugStateText;
    private Camera targetCamera;

    private float nextPathUpdate = 0f;

    private void Start()
    {
        motor = GetComponent<AgentMotor>();
        targetCamera = Camera.main;

        Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");

        // Setup Path Line
        GameObject lineObj = new GameObject("PathLine");
        lineObj.transform.SetParent(transform, false);
        pathRenderer = lineObj.AddComponent<LineRenderer>();
        pathRenderer.startWidth = 0.05f;
        pathRenderer.endWidth = 0.05f;
        
        //  Cyan path line for Defender
        //  Light reddish for Striker
        Material pathMat = new Material(unlitShader);
        AgentStats stats = GetComponent<AgentStats>();
        if (stats != null)
        {
            if (stats.team == TeamType.Blue)
            {
                pathMat.color = new Color(0.2f, 0.75f, 1f, 1f); 
            }
            else
            {
                pathMat.color = new Color(1f, 0.3f, 0.3f, 1f); 
            }
        }
        else
        {
            pathMat.color = Color.yellow; 
        }
        pathRenderer.material = pathMat;
        pathRenderer.positionCount = 0;
        pathRenderer.gameObject.SetActive(false);

        // Setup Target Marker
        targetMarker = GameObject.CreatePrimitive(PrimitiveType.Quad);
        targetMarker.name = "TargetMarker";
        // Keep the marker under its owning agent so team-vision visibility also
        // hides debug destinations that would otherwise leak an unseen enemy.
        targetMarker.transform.SetParent(transform, true);
        targetMarker.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        targetMarker.transform.localScale = new Vector3(0.5f, 0.5f, 1f);
        Destroy(targetMarker.GetComponent<Collider>());
        
        Material markerMat = new Material(unlitShader);
        markerMat.color = new Color(1f, 0.2f, 0.2f, 0.8f);
        targetMarker.GetComponent<Renderer>().sharedMaterial = markerMat;
        targetMarker.SetActive(false);

        // Setup text state
        GameObject textObj = new GameObject("DebugStateText");
        textObj.transform.SetParent(transform, false);
        textObj.transform.localPosition = new Vector3(0f, 2.5f, 0f); 
        debugStateText = textObj.AddComponent<TextMesh>();
        debugStateText.anchor = TextAnchor.MiddleCenter;
        debugStateText.alignment = TextAlignment.Center;
        debugStateText.characterSize = 0.04f;
        debugStateText.fontSize = 64;
        debugStateText.fontStyle = FontStyle.Bold;
        debugStateText.gameObject.SetActive(false);
    }

    private void LateUpdate()
    {
        bool campaignRound = CampaignManager.Instance != null &&
                             CampaignManager.Instance.IsCampaignScene;
        if (campaignRound || DebugVisualManager.Instance == null)
        {
            HideAllVisuals();
            return;
        }

        UpdatePathVisuals();
        UpdateTargetVisuals();
        UpdateTextVisuals();
    }

    private void HideAllVisuals()
    {
        if (pathRenderer != null)
        {
            pathRenderer.positionCount = 0;
            pathRenderer.gameObject.SetActive(false);
        }
        if (targetMarker != null)
        {
            targetMarker.SetActive(false);
        }
        if (debugStateText != null)
        {
            debugStateText.gameObject.SetActive(false);
        }
    }

    private void OnDisable()
    {
        HideAllVisuals();
    }

    //  Request path data from A* system and updates the lineRenderer 
    private void UpdatePathVisuals()
    {
        pathRenderer.gameObject.SetActive(DebugVisualManager.Instance.ShowPaths);
        
        if (!DebugVisualManager.Instance.ShowPaths || AStarPathfinder3D.Instance == null || motor.RequestedDestination == Vector3.zero) 
            return;

        if (Time.time < nextPathUpdate) return;
        nextPathUpdate = Time.time + 0.5f;

        var pathList = AStarPathfinder3D.Instance.FindPath(transform.position, motor.RequestedDestination);
        if (pathList != null)
        {
            Vector3[] pathArray = pathList.ToArray();
            pathRenderer.positionCount = pathArray.Length + 1;
            
            pathRenderer.SetPosition(0, transform.position + Vector3.up * 0.2f);
            
            for (int i = 0; i < pathArray.Length; i++)
            {
                pathRenderer.SetPosition(i + 1, pathArray[i] + Vector3.up * 0.2f);
            }
        }
        else
        {
            pathRenderer.positionCount = 0;
        }
    }

    private void UpdateTargetVisuals()
    {
        targetMarker.SetActive(DebugVisualManager.Instance.ShowTargets && motor.HasDestination);
        
        if (DebugVisualManager.Instance.ShowTargets && motor.HasDestination)
        {
            targetMarker.transform.position = motor.RequestedDestination + Vector3.up * 0.15f;
        }
    }

    private void UpdateTextVisuals()
    {
        debugStateText.gameObject.SetActive(DebugVisualManager.Instance.ShowActions);

        if (DebugVisualManager.Instance.ShowActions && targetCamera != null)
        {
            debugStateText.transform.rotation = Quaternion.LookRotation(
                debugStateText.transform.position - targetCamera.transform.position,
                targetCamera.transform.up);
        }
    }

    public void SetAIState(string state)
    {
        if (debugStateText != null)
        {
            debugStateText.text = state;
            debugStateText.color = state.Contains("Shoot") ? Color.red : Color.cyan;
        }
    }

    private void OnDestroy()
    {
        if (targetMarker != null) Destroy(targetMarker);
    }
}
