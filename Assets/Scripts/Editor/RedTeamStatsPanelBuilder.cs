#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class RedTeamStatsPanelBuilder
{
    private const string MenuPath = "Tools/CS Auto Battler/Build Red Team Stats Panel";
    private const string AssignPortraitDataMenuPath =
        "Tools/CS Auto Battler/Assign Default Red Agent Portrait Data";
    private const string PanelName = "RedTeamStatsPanel";
    private const string TemporaryPanelName = "RedTeamStatsPanel__Building";
    private const int SlotCount = 8;
    private const float SlotWidth = RedTeamStatsSlotUI.DisplayWidth;
    private const float SlotHeight = RedTeamStatsSlotUI.DisplayHeight;
    private const float SlotSpacing = 5f;

    private static readonly Color[] DefaultFallbackPortraitColors =
    {
        new Color(0.35f, 0.06f, 0.06f, 1f),
        new Color(0.85f, 0.25f, 0.08f, 1f),
        new Color(0.42f, 0.22f, 0.12f, 1f),
        new Color(0.48f, 0.08f, 0.22f, 1f),
        new Color(0.64f, 0.28f, 0.34f, 1f),
        new Color(0.30f, 0.12f, 0.46f, 1f),
        new Color(0.72f, 0.18f, 0.24f, 1f),
        new Color(0.38f, 0.16f, 0.10f, 1f)
    };

    [MenuItem(AssignPortraitDataMenuPath, false, 2009)]
    private static void AssignDefaultRedAgentPortraitData()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("Red agent portrait data assignment skipped: exit Play Mode first.");
            return;
        }

        Scene activeScene = SceneManager.GetActiveScene();
        if (!activeScene.IsValid() || !activeScene.isLoaded)
        {
            Debug.LogError("Red agent portrait data assignment failed: there is no valid, loaded active scene.");
            return;
        }

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Assign Default Red Agent Portrait Data");

        int configuredCount = 0;
        for (int index = 0; index < SlotCount; index++)
        {
            string agentName = "RedAgent3D_" + (index + 1);
            GameObject agentObject = FindSceneObjectByName(agentName);
            if (agentObject == null)
            {
                Debug.LogWarning("Could not assign portrait data because " + agentName + " was not found.");
                continue;
            }

            AgentPortraitData portraitData = agentObject.GetComponent<AgentPortraitData>();
            if (portraitData == null)
            {
                portraitData = Undo.AddComponent<AgentPortraitData>(agentObject);
            }

            Renderer sourceRenderer = agentObject.GetComponent<Renderer>();
            if (sourceRenderer == null)
            {
                sourceRenderer = agentObject.GetComponentInChildren<Renderer>(true);
            }

            Undo.RecordObject(portraitData, "Configure " + agentName + " Portrait Data");
            if (portraitData.sourceRenderer == null)
            {
                portraitData.sourceRenderer = sourceRenderer;
            }

            portraitData.fallbackPortraitColor = DefaultFallbackPortraitColors[index];
            EditorUtility.SetDirty(portraitData);
            configuredCount++;
        }

        if (configuredCount > 0)
        {
            EditorSceneManager.MarkSceneDirty(activeScene);
        }

        Undo.CollapseUndoOperations(undoGroup);
        Debug.Log("Configured portrait data for " + configuredCount + " red agent(s).");
    }

    [MenuItem(AssignPortraitDataMenuPath, true)]
    private static bool ValidateAssignDefaultRedAgentPortraitData()
    {
        return !EditorApplication.isPlayingOrWillChangePlaymode;
    }

    [MenuItem(MenuPath, false, 2010)]
    private static void BuildRedTeamStatsPanel()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("Red Team Stats Panel build skipped: exit Play Mode first.");
            return;
        }

        Scene activeScene = SceneManager.GetActiveScene();
        if (!activeScene.IsValid() || !activeScene.isLoaded)
        {
            Debug.LogError("Red Team Stats Panel build failed: there is no valid, loaded active scene.");
            return;
        }

        Font defaultFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (defaultFont == null)
        {
            Debug.LogError(
                "Red Team Stats Panel build failed: Unity's LegacyRuntime default font could not be loaded.");
            return;
        }

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Build Red Team Stats Panel");

        GameObject canvasObject = null;
        GameObject panelObject = null;
        bool createdCanvas = false;
        bool panelCommitted = false;

        try
        {
            Canvas canvas = FindOrCreateCanvas(activeScene, out createdCanvas);
            canvasObject = canvas.gameObject;

            if (canvas.renderMode == RenderMode.WorldSpace)
            {
                Debug.LogWarning(
                    "Canvas3D is a World Space Canvas. The stats panel was anchored to the bottom of that Canvas " +
                    "without changing its render mode, so verify its placement in the Game view.");
            }

            panelObject = CreateUIObject(TemporaryPanelName, canvas.transform);
            RectTransform panelRect = panelObject.GetComponent<RectTransform>();
            ConfigureBottomPanelRect(panelRect);

            HorizontalLayoutGroup layout = Undo.AddComponent<HorizontalLayoutGroup>(panelObject);
            layout.padding = new RectOffset(0, 0, 0, 0);
            layout.spacing = SlotSpacing;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            RedTeamStatsPanelUI panelUI = Undo.AddComponent<RedTeamStatsPanelUI>(panelObject);
            RedTeamStatsSlotUI[] slots = new RedTeamStatsSlotUI[SlotCount];

            for (int index = 0; index < SlotCount; index++)
            {
                slots[index] = CreateSlot(panelRect, index + 1, defaultFont);
            }

            panelUI.SetSlots(slots);

            DeleteExistingGeneratedPanel(canvas.transform);
            Undo.RecordObject(panelObject, "Name Red Team Stats Panel");
            panelObject.name = PanelName;
            panelCommitted = true;

            Selection.activeGameObject = panelObject;
            EditorGUIUtility.PingObject(panelObject);
            EditorSceneManager.MarkSceneDirty(activeScene);
            Undo.CollapseUndoOperations(undoGroup);

            Debug.Log(
                "Built RedTeamStatsPanel with exactly eight slots. No minimap or Legacy2D objects were changed.");
        }
        catch (Exception exception)
        {
            if (!panelCommitted)
            {
                if (createdCanvas && canvasObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(canvasObject);
                }
                else if (panelObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(panelObject);
                }
            }

            Undo.CollapseUndoOperations(undoGroup);
            Debug.LogException(exception);
            Debug.LogError(
                "Red Team Stats Panel build did not complete. Existing generated UI was preserved unless the " +
                "replacement had already completed; any scene changes are grouped into one Undo step.");
        }
    }

    [MenuItem(MenuPath, true)]
    private static bool ValidateBuildRedTeamStatsPanel()
    {
        return !EditorApplication.isPlayingOrWillChangePlaymode;
    }

    private static Canvas FindOrCreateCanvas(Scene activeScene, out bool createdCanvas)
    {
        createdCanvas = false;

        GameObject canvasObject = FindSceneObjectByName("Canvas3D");
        Canvas canvas = canvasObject != null ? canvasObject.GetComponent<Canvas>() : null;

        if (canvas == null)
        {
            canvasObject = FindSceneObjectByName("Canvas");
            canvas = canvasObject != null ? canvasObject.GetComponent<Canvas>() : null;
        }

        if (canvas != null)
        {
            return canvas;
        }

        canvasObject = new GameObject(
            "Canvas3D",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster));

        SceneManager.MoveGameObjectToScene(canvasObject, activeScene);
        Undo.RegisterCreatedObjectUndo(canvasObject, "Create Canvas3D");

        canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = HudCanvasScaleUtility.ReferenceResolution;
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        createdCanvas = true;
        return canvas;
    }

    private static RedTeamStatsSlotUI CreateSlot(
        RectTransform panelRect,
        int slotNumber,
        Font defaultFont)
    {
        GameObject slotObject = CreateUIObject("RedTeamSlot_" + slotNumber, panelRect);
        RectTransform slotRect = slotObject.GetComponent<RectTransform>();
        slotRect.sizeDelta = new Vector2(SlotWidth, SlotHeight);

        LayoutElement layoutElement = Undo.AddComponent<LayoutElement>(slotObject);
        layoutElement.minWidth = SlotWidth;
        layoutElement.preferredWidth = SlotWidth;
        layoutElement.minHeight = SlotHeight;
        layoutElement.preferredHeight = SlotHeight;
        layoutElement.flexibleWidth = 0f;
        layoutElement.flexibleHeight = 0f;

        Image background = Undo.AddComponent<Image>(slotObject);
        background.color = new Color(0.12f, 0.055f, 0.045f, 0.9f);
        background.raycastTarget = false;

        Undo.AddComponent<CanvasGroup>(slotObject);
        RedTeamStatsSlotUI slotUI = Undo.AddComponent<RedTeamStatsSlotUI>(slotObject);

        Image portrait = CreatePortrait(slotRect);
        Text hpText = CreateLabel(
            "HPText",
            slotRect,
            defaultFont,
            "HP: --",
            new Vector2(0f, 0.5f),
            new Vector2(1f, 1f),
            new Vector2(63f, 0f),
            new Vector2(-6f, -5f));

        Text tacticText = CreateLabel(
            "TacticText",
            slotRect,
            defaultFont,
            "Role: --",
            new Vector2(0f, 0f),
            new Vector2(1f, 0.5f),
            new Vector2(63f, 5f),
            new Vector2(-6f, 0f));

        slotUI.Configure(portrait, hpText, tacticText, background);
        return slotUI;
    }

    private static Image CreatePortrait(RectTransform slotRect)
    {
        GameObject portraitObject = CreateUIObject("Portrait", slotRect);
        RectTransform portraitRect = portraitObject.GetComponent<RectTransform>();
        portraitRect.anchorMin = new Vector2(0f, 0.5f);
        portraitRect.anchorMax = new Vector2(0f, 0.5f);
        portraitRect.pivot = new Vector2(0f, 0.5f);
        portraitRect.anchoredPosition = new Vector2(7f, 0f);
        portraitRect.sizeDelta = new Vector2(50f, 50f);

        Image portrait = Undo.AddComponent<Image>(portraitObject);
        portrait.color = Color.white;
        portrait.raycastTarget = false;
        return portrait;
    }

    private static Text CreateLabel(
        string objectName,
        RectTransform parent,
        Font font,
        string initialText,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 offsetMin,
        Vector2 offsetMax)
    {
        GameObject textObject = CreateUIObject(objectName, parent);
        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = anchorMin;
        textRect.anchorMax = anchorMax;
        textRect.pivot = new Vector2(0.5f, 0.5f);
        textRect.offsetMin = offsetMin;
        textRect.offsetMax = offsetMax;

        Text label = Undo.AddComponent<Text>(textObject);
        label.font = font;
        label.text = initialText;
        label.fontSize = 13;
        label.fontStyle = FontStyle.Bold;
        label.color = new Color(1f, 0.92f, 0.88f, 1f);
        label.alignment = TextAnchor.MiddleLeft;
        label.horizontalOverflow = HorizontalWrapMode.Wrap;
        label.verticalOverflow = VerticalWrapMode.Truncate;
        label.resizeTextForBestFit = true;
        label.resizeTextMinSize = 10;
        label.resizeTextMaxSize = 13;
        label.raycastTarget = false;
        return label;
    }

    private static GameObject CreateUIObject(string objectName, Transform parent)
    {
        GameObject uiObject = new GameObject(objectName, typeof(RectTransform));
        uiObject.transform.SetParent(parent, false);
        Undo.RegisterCreatedObjectUndo(uiObject, "Create " + objectName);
        return uiObject;
    }

    private static void ConfigureBottomPanelRect(RectTransform panelRect)
    {
        float panelWidth = (SlotCount * SlotWidth) + ((SlotCount - 1) * SlotSpacing);
        panelRect.anchorMin = new Vector2(0.5f, 0f);
        panelRect.anchorMax = new Vector2(0.5f, 0f);
        panelRect.pivot = new Vector2(0.5f, 0f);
        panelRect.anchoredPosition = new Vector2(0f, 14f);
        panelRect.sizeDelta = new Vector2(panelWidth, SlotHeight);
        panelRect.localRotation = Quaternion.identity;
        panelRect.localScale = Vector3.one;
    }

    private static void DeleteExistingGeneratedPanel(Transform canvasTransform)
    {
        for (int index = canvasTransform.childCount - 1; index >= 0; index--)
        {
            Transform child = canvasTransform.GetChild(index);
            if (child != null && string.Equals(child.name, PanelName, StringComparison.Ordinal))
            {
                // Only direct generated children with the exact name are replaced.
                Undo.DestroyObjectImmediate(child.gameObject);
            }
        }
    }

    private static GameObject FindSceneObjectByName(string objectName)
    {
        Scene activeScene = SceneManager.GetActiveScene();
        foreach (GameObject root in activeScene.GetRootGameObjects())
        {
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            foreach (Transform candidate in transforms)
            {
                if (candidate != null &&
                    string.Equals(candidate.name, objectName, StringComparison.Ordinal) &&
                    !IsPartOfLegacy2D(candidate))
                {
                    return candidate.gameObject;
                }
            }
        }

        return null;
    }

    private static bool IsPartOfLegacy2D(Transform candidate)
    {
        Transform current = candidate;
        while (current != null)
        {
            if (string.Equals(current.name, "Legacy2D", StringComparison.Ordinal))
            {
                return true;
            }

            current = current.parent;
        }

        return false;
    }
}
#endif
