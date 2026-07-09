using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

/// <summary>
/// Lets the player manually command a controlled teammate's role ability.
/// The abilities themselves remain authoritative for cooldowns and validation.
/// </summary>
[DefaultExecutionOrder(50)]
public sealed class PlayerAbilityCommandController : MonoBehaviour
{
    private const int MaximumUsesPerRound = 3;
    private const int RingSegments = 40;

    private AgentStats selectedAgent;
    private AgentRole selectedRole;
    private AgentRoleAbilities selectedAbilities;
    private RoundManager roundManager;
    private Camera targetCamera;
    private GameObject previewRoot;
    private LineRenderer supportLine;
    private LineRenderer selectionRing;
    private Renderer[] previewRenderers;
    private Material validPreviewMaterial;
    private Material invalidPreviewMaterial;
    private Material blinkPreviewMaterial;
    private Material lineMaterial;
    private Vector3 pointerWorldPosition;
    private bool hasPointerWorldPosition;
    private bool currentTargetValid;
    private string currentHint = "Left-click one of your players";
    private string feedback = string.Empty;
    private float feedbackUntil;
    private int remainingUses = MaximumUsesPerRound;

    public int RemainingUses => remainingUses;
    public int MaximumUses => MaximumUsesPerRound;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureInstance()
    {
        if (FindAnyObjectByType<RoundManager>() != null &&
            FindAnyObjectByType<PlayerAbilityCommandController>() == null)
        {
            new GameObject("Player Ability Command Controller")
                .AddComponent<PlayerAbilityCommandController>();
        }
    }

    private void Awake()
    {
        targetCamera = Camera.main;
        BindRoundManager();
    }

    private void Start()
    {
        BindRoundManager();
        remainingUses = MaximumUsesPerRound;
    }

    private void Update()
    {
        BindRoundManager();
        targetCamera ??= Camera.main;
        if (!SelectionIsUsable())
        {
            CancelSelection();
        }

        if (selectedAgent != null)
        {
            UpdatePreview();
        }

        if (Mouse.current == null)
        {
            return;
        }

        if (Mouse.current.rightButton.wasPressedThisFrame ||
            (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame))
        {
            CancelSelection();
            SetFeedback("Ability command cancelled", 1.25f);
            return;
        }

        if (!Mouse.current.leftButton.wasPressedThisFrame ||
            (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()))
        {
            return;
        }

        if (selectedAgent == null)
        {
            TrySelectAgent();
        }
        else
        {
            TryConfirmAbility();
        }
    }

    private void BindRoundManager()
    {
        RoundManager candidate = RoundManager.Instance != null
            ? RoundManager.Instance
            : FindAnyObjectByType<RoundManager>();
        if (candidate == roundManager)
        {
            return;
        }

        if (roundManager != null)
        {
            roundManager.StateChanged -= OnRoundStateChanged;
        }

        roundManager = candidate;
        if (roundManager != null)
        {
            roundManager.StateChanged += OnRoundStateChanged;
        }
    }

    private void OnRoundStateChanged(RoundState state)
    {
        if (state == RoundState.Preparation)
        {
            remainingUses = MaximumUsesPerRound;
            CancelSelection();
            SetFeedback("Manual abilities refreshed: 3 uses", 2f);
        }
        else if (state == RoundState.RoundEnd || state == RoundState.Defused ||
                 state == RoundState.Exploded)
        {
            CancelSelection();
        }
    }

    private void TrySelectAgent()
    {
        if (!RoundAcceptsCommands())
        {
            SetFeedback("Abilities are unavailable in this round state", 1.5f);
            return;
        }

        if (remainingUses <= 0)
        {
            SetFeedback("No manual ability uses left this round", 1.8f);
            return;
        }

        AgentStats candidate = GetAgentUnderPointer();
        if (!IsControlledLivingAgent(candidate))
        {
            SetFeedback("Left-click one of your living players", 1.4f);
            return;
        }

        AgentRole role = candidate.GetComponent<AgentRole>();
        AgentRoleAbilities abilities = candidate.GetComponent<AgentRoleAbilities>();
        if (role == null || abilities == null)
        {
            SetFeedback("That player has no role ability", 1.5f);
            return;
        }

        selectedAgent = candidate;
        selectedRole = role;
        selectedAbilities = abilities;
        selectedAbilities.SetPlayerCommandSelected(true);
        CreatePreview();
        currentHint = GetInstruction(role.SelectedRole);
        float cooldown = abilities.ManualCooldownRemaining;
        if (float.IsPositiveInfinity(cooldown) || cooldown > 0f)
        {
            string cooldownText = float.IsPositiveInfinity(cooldown)
                ? $"{role.SelectedRole} ability is not armed yet"
                : $"{role.SelectedRole} ability cooldown: {Mathf.CeilToInt(cooldown)}s";
            SetFeedback(cooldownText, 3.5f);
        }
        else
        {
            SetFeedback(currentHint, 1.5f);
        }
    }

    private void TryConfirmAbility()
    {
        if (remainingUses <= 0 || selectedAbilities == null || selectedRole == null)
        {
            CancelSelection();
            SetFeedback("No manual ability uses left this round", 1.5f);
            return;
        }

        bool used = false;
        switch (selectedRole.SelectedRole)
        {
            case AgentRoleType.Defender:
                if (hasPointerWorldPosition)
                {
                    used = selectedAbilities.TryManualDeployWall(pointerWorldPosition);
                }
                break;
            case AgentRoleType.Flanker:
                if (hasPointerWorldPosition)
                {
                    used = selectedAbilities.TryManualShadowBlink(pointerWorldPosition);
                }
                break;
            case AgentRoleType.Assaulter:
                if (hasPointerWorldPosition)
                {
                    used = selectedAbilities.TryManualInstallTurret(pointerWorldPosition);
                }
                break;
            case AgentRoleType.Support:
                AgentStats target = GetAgentUnderPointer();
                HealthSystem targetHealth = target != null
                    ? target.GetComponent<HealthSystem>()
                    : null;
                used = selectedAbilities.TryManualHeal(targetHealth);
                break;
        }

        if (!used)
        {
            SetFeedback(currentHint, 1.4f);
            return;
        }

        remainingUses = Mathf.Max(0, remainingUses - 1);
        int left = remainingUses;
        CancelSelection();
        SetFeedback($"Ability commanded - {left} use{(left == 1 ? "" : "s")} left", 2f);
    }

    private void UpdatePreview()
    {
        hasPointerWorldPosition = TryGetPointerWorldPosition(out pointerWorldPosition);
        currentTargetValid = false;
        if (selectedAbilities == null || selectedRole == null)
        {
            return;
        }

        switch (selectedRole.SelectedRole)
        {
            case AgentRoleType.Defender:
                if (hasPointerWorldPosition)
                {
                    currentTargetValid = selectedAbilities.CanManuallyDeployWall(
                        pointerWorldPosition,
                        out Quaternion wallRotation,
                        out currentHint);
                    SetPreviewPose(pointerWorldPosition, wallRotation);
                }
                break;
            case AgentRoleType.Flanker:
                if (hasPointerWorldPosition)
                {
                    currentTargetValid = selectedAbilities.CanManuallyShadowBlink(
                        pointerWorldPosition,
                        out currentHint);
                    SetPreviewPose(pointerWorldPosition, Quaternion.identity);
                }
                break;
            case AgentRoleType.Assaulter:
                if (hasPointerWorldPosition)
                {
                    currentTargetValid = selectedAbilities.CanManuallyInstallTurret(
                        pointerWorldPosition,
                        out Quaternion turretRotation,
                        out currentHint);
                    SetPreviewPose(pointerWorldPosition, turretRotation);
                }
                break;
            case AgentRoleType.Support:
                UpdateSupportPreview();
                break;
        }

        ApplyPreviewValidity(currentTargetValid);
        UpdateSelectionRing();
    }

    private void UpdateSupportPreview()
    {
        AgentStats target = GetAgentUnderPointer();
        HealthSystem targetHealth = target != null ? target.GetComponent<HealthSystem>() : null;
        currentTargetValid = selectedAbilities.CanManuallyHeal(targetHealth, out currentHint);
        Vector3 end = target != null
            ? target.transform.position + Vector3.up * 0.9f
            : hasPointerWorldPosition
                ? pointerWorldPosition + Vector3.up * 0.12f
                : selectedAgent.transform.position;
        if (supportLine != null)
        {
            supportLine.SetPosition(0, selectedAgent.transform.position + Vector3.up * 0.9f);
            supportLine.SetPosition(1, end);
            supportLine.startColor = currentTargetValid
                ? new Color(0.2f, 1f, 0.4f, 0.9f)
                : new Color(0.2f, 1f, 0.4f, 0.35f);
            supportLine.endColor = supportLine.startColor;
        }
    }

    private void CreatePreview()
    {
        DestroyPreview();
        EnsureMaterials();
        previewRoot = new GameObject("Manual Ability Preview");

        switch (selectedRole.SelectedRole)
        {
            case AgentRoleType.Defender:
                GameObject wall = CreatePrimitivePreview(
                    "Wall Preview",
                    PrimitiveType.Cube,
                    previewRoot.transform,
                    Vector3.zero,
                    selectedAbilities.WallPreviewSize);
                previewRenderers = new[] { wall.GetComponent<Renderer>() };
                break;
            case AgentRoleType.Assaulter:
                previewRenderers = CreateTurretPreview(previewRoot.transform);
                break;
            case AgentRoleType.Flanker:
                CreateBlinkPreview(previewRoot.transform);
                break;
            case AgentRoleType.Support:
                supportLine = previewRoot.AddComponent<LineRenderer>();
                supportLine.useWorldSpace = true;
                supportLine.positionCount = 2;
                supportLine.startWidth = 0.075f;
                supportLine.endWidth = 0.035f;
                supportLine.sharedMaterial = lineMaterial;
                supportLine.shadowCastingMode = ShadowCastingMode.Off;
                supportLine.receiveShadows = false;
                break;
        }

        GameObject ringObject = new GameObject("Selected Player Ring");
        ringObject.transform.SetParent(previewRoot.transform, false);
        selectionRing = CreateRing(ringObject, 0.75f, lineMaterial);
        selectionRing.startColor = new Color(1f, 0.22f, 0.12f, 0.95f);
        selectionRing.endColor = selectionRing.startColor;
        UpdateSelectionRing();
    }

    private Renderer[] CreateTurretPreview(Transform parent)
    {
        GameObject baseObject = CreatePrimitivePreview(
            "Turret Base Preview", PrimitiveType.Cylinder, parent,
            new Vector3(0f, 0.22f, 0f), new Vector3(0.75f, 0.22f, 0.75f));
        GameObject head = CreatePrimitivePreview(
            "Turret Head Preview", PrimitiveType.Cube, parent,
            new Vector3(0f, 0.72f, 0f), new Vector3(0.7f, 0.42f, 0.65f));
        GameObject barrel = CreatePrimitivePreview(
            "Turret Barrel Preview", PrimitiveType.Cube, parent,
            new Vector3(0f, 0.75f, 0.48f), new Vector3(0.2f, 0.2f, 1.15f));
        return new[]
        {
            baseObject.GetComponent<Renderer>(),
            head.GetComponent<Renderer>(),
            barrel.GetComponent<Renderer>()
        };
    }

    private void CreateBlinkPreview(Transform parent)
    {
        for (int index = 0; index < 2; index++)
        {
            GameObject ringObject = new GameObject("Shadow Blink Preview Ring " + index);
            ringObject.transform.SetParent(parent, false);
            ringObject.transform.localPosition = Vector3.up * (0.06f + index * 0.42f);
            LineRenderer ring = CreateRing(
                ringObject,
                index == 0 ? 1.4f : 0.85f,
                blinkPreviewMaterial);
            ring.startWidth = index == 0 ? 0.12f : 0.07f;
            ring.endWidth = ring.startWidth;
            ring.startColor = new Color(0.92f, 0.3f, 1f, 0.42f);
            ring.endColor = new Color(0.35f, 0.05f, 0.48f, 0.2f);
        }
    }

    private static GameObject CreatePrimitivePreview(
        string objectName,
        PrimitiveType primitiveType,
        Transform parent,
        Vector3 localPosition,
        Vector3 localScale)
    {
        GameObject part = GameObject.CreatePrimitive(primitiveType);
        part.name = objectName;
        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPosition;
        part.transform.localScale = localScale;
        Collider collider = part.GetComponent<Collider>();
        if (collider != null)
        {
            Destroy(collider);
        }
        return part;
    }

    private static LineRenderer CreateRing(
        GameObject owner,
        float radius,
        Material material)
    {
        LineRenderer ring = owner.AddComponent<LineRenderer>();
        ring.useWorldSpace = false;
        ring.loop = true;
        ring.positionCount = RingSegments;
        ring.startWidth = 0.055f;
        ring.endWidth = 0.055f;
        ring.sharedMaterial = material;
        ring.shadowCastingMode = ShadowCastingMode.Off;
        ring.receiveShadows = false;
        for (int i = 0; i < RingSegments; i++)
        {
            float angle = i * Mathf.PI * 2f / RingSegments;
            ring.SetPosition(i, new Vector3(
                Mathf.Cos(angle) * radius,
                0f,
                Mathf.Sin(angle) * radius));
        }
        return ring;
    }

    private void SetPreviewPose(Vector3 position, Quaternion rotation)
    {
        if (previewRoot != null)
        {
            position.y = selectedAgent != null ? selectedAgent.transform.position.y : position.y;
            previewRoot.transform.SetPositionAndRotation(position, rotation);
        }
    }

    private void ApplyPreviewValidity(bool valid)
    {
        if (previewRenderers == null)
        {
            return;
        }

        Material material = valid ? validPreviewMaterial : invalidPreviewMaterial;
        foreach (Renderer renderer in previewRenderers)
        {
            if (renderer != null)
            {
                renderer.sharedMaterial = material;
            }
        }
    }

    private void UpdateSelectionRing()
    {
        if (selectionRing != null && selectedAgent != null)
        {
            selectionRing.transform.position = selectedAgent.transform.position +
                                               Vector3.up * 0.08f;
            selectionRing.transform.rotation = Quaternion.identity;
        }
    }

    private void EnsureMaterials()
    {
        validPreviewMaterial ??= CreateTransparentMaterial(
            "Valid Ability Preview",
            new Color(0.9f, 0.035f, 0.02f, 0.34f));
        invalidPreviewMaterial ??= CreateTransparentMaterial(
            "Invalid Ability Preview",
            new Color(0.5f, 0.04f, 0.035f, 0.16f));
        blinkPreviewMaterial ??= CreateTransparentMaterial(
            "Shadow Blink Preview",
            new Color(0.86f, 0.24f, 1f, 0.3f), true);
        lineMaterial ??= CreateTransparentMaterial(
            "Ability Command Line",
            new Color(0.2f, 1f, 0.4f, 0.75f), true);
    }

    private static Material CreateTransparentMaterial(
        string materialName,
        Color color,
        bool unlit = false)
    {
        Shader shader = Shader.Find(unlit
            ? "Universal Render Pipeline/Unlit"
            : "Universal Render Pipeline/Lit");
        shader ??= Shader.Find(unlit ? "Unlit/Color" : "Standard");
        Material material = new Material(shader)
        {
            name = materialName,
            color = color,
            hideFlags = HideFlags.DontSave
        };
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
        if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
        material.SetOverrideTag("RenderType", "Transparent");
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.EnableKeyword("_ALPHABLEND_ON");
        material.renderQueue = (int)RenderQueue.Transparent;
        return material;
    }

    private bool TryGetPointerWorldPosition(out Vector3 worldPosition)
    {
        worldPosition = default;
        if (targetCamera == null || Mouse.current == null)
        {
            return false;
        }

        Ray ray = targetCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
        float height = selectedAgent != null ? selectedAgent.transform.position.y : 0f;
        Plane plane = new Plane(Vector3.up, new Vector3(0f, height, 0f));
        if (!plane.Raycast(ray, out float enter))
        {
            return false;
        }

        worldPosition = ray.GetPoint(enter);
        worldPosition.y = height;
        return true;
    }

    private AgentStats GetAgentUnderPointer()
    {
        if (targetCamera == null || Mouse.current == null)
        {
            return null;
        }

        Ray ray = targetCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
        RaycastHit[] hits = Physics.RaycastAll(
            ray,
            Mathf.Infinity,
            ~0,
            QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
        foreach (RaycastHit hit in hits)
        {
            AgentStats agent = hit.collider != null
                ? hit.collider.GetComponentInParent<AgentStats>()
                : null;
            if (agent != null)
            {
                return agent;
            }
        }

        return null;
    }

    private bool IsControlledLivingAgent(AgentStats agent)
    {
        if (agent == null || agent.team != GetControlledTeam())
        {
            return false;
        }

        HealthSystem health = agent.GetComponent<HealthSystem>();
        return health != null && !health.IsDead && agent.gameObject.activeInHierarchy;
    }

    private TeamType GetControlledTeam()
    {
        return TeamTacticManager.Instance != null
            ? TeamTacticManager.Instance.ControlledTeam
            : TeamType.Red;
    }

    private bool SelectionIsUsable()
    {
        return selectedAgent == null ||
               (IsControlledLivingAgent(selectedAgent) && RoundAcceptsCommands());
    }

    private bool RoundAcceptsCommands()
    {
        if (roundManager == null)
        {
            return true;
        }

        return roundManager.CurrentState != RoundState.Preparation &&
               roundManager.CurrentState != RoundState.RoundEnd &&
               roundManager.CurrentState != RoundState.Defused &&
               roundManager.CurrentState != RoundState.Exploded;
    }

    private static string GetInstruction(AgentRoleType role)
    {
        return role switch
        {
            AgentRoleType.Defender => "Move the wall preview, then click empty ground",
            AgentRoleType.Flanker => "Move the Shadow Blink preview, then click empty ground",
            AgentRoleType.Assaulter => "Move the turret preview, then click empty ground",
            AgentRoleType.Support => "Aim the green line, then click a damaged teammate",
            _ => "Choose an ability target"
        };
    }

    private void CancelSelection()
    {
        selectedAbilities?.SetPlayerCommandSelected(false);
        selectedAgent = null;
        selectedRole = null;
        selectedAbilities = null;
        currentHint = "Left-click one of your players";
        currentTargetValid = false;
        DestroyPreview();
    }

    private void DestroyPreview()
    {
        if (previewRoot != null)
        {
            Destroy(previewRoot);
        }

        previewRoot = null;
        previewRenderers = null;
        supportLine = null;
        selectionRing = null;
    }

    private void SetFeedback(string message, float duration)
    {
        feedback = message;
        feedbackUntil = Time.time + duration;
    }

    private void OnGUI()
    {
        const float width = 390f;
        float height = selectedAgent != null ? 156f : 110f;
        Rect panel;
        if (!HudLayoutUtility.TryGetGuiRect("ManualAbilityPreview", out panel))
        {
            panel = new Rect(
                18f,
                18f,
                width,
                height);
        }
        GUI.Box(panel, GUIContent.none);

        GUIStyle title = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.UpperLeft,
            fontSize = 23,
            fontStyle = FontStyle.Bold,
            normal = { textColor = remainingUses > 0
                ? new Color(1f, 0.35f, 0.25f)
                : new Color(0.65f, 0.65f, 0.65f) }
        };
        GUI.Label(
            new Rect(panel.x + 12f, panel.y + 8f, panel.width - 24f, 28f),
            $"MANUAL ABILITIES LEFT: {remainingUses}/{MaximumUsesPerRound}",
            title);

        GUIStyle body = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.UpperLeft,
            fontSize = 17,
            wordWrap = true,
            normal = { textColor = Color.white }
        };
        string line = selectedAgent != null && selectedRole != null
            ? $"{selectedAgent.name} - {selectedRole.SelectedRole}  [AI AUTO-CAST PAUSED]\n" +
              $"{currentHint}\nRight-click / Esc to cancel"
            : "Left-click a player to command their role ability";
        if (Time.time < feedbackUntil && !string.IsNullOrEmpty(feedback))
        {
            line = feedback + (selectedAgent != null ? "\n" + line : string.Empty);
        }
        GUI.Label(
            new Rect(panel.x + 12f, panel.y + 40f, panel.width - 24f, panel.height - 46f),
            line,
            body);

        if (Time.time < feedbackUntil && !string.IsNullOrEmpty(feedback))
        {
            const float toastWidth = 520f;
            Rect toast = new Rect(
                (Screen.width - toastWidth) * 0.5f,
                18f,
                toastWidth,
                48f);
            GUI.Box(toast, GUIContent.none);
            GUIStyle toastStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                normal = { textColor = feedback.IndexOf(
                        "cooldown",
                        System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    feedback.IndexOf(
                        "not armed",
                        System.StringComparison.OrdinalIgnoreCase) >= 0
                        ? new Color(1f, 0.78f, 0.2f)
                        : Color.white }
            };
            GUI.Label(toast, feedback, toastStyle);
        }
    }

    private void OnDestroy()
    {
        if (roundManager != null)
        {
            roundManager.StateChanged -= OnRoundStateChanged;
        }

        selectedAbilities?.SetPlayerCommandSelected(false);
        DestroyPreview();
        Destroy(validPreviewMaterial);
        Destroy(invalidPreviewMaterial);
        Destroy(blinkPreviewMaterial);
        Destroy(lineMaterial);
    }
}
