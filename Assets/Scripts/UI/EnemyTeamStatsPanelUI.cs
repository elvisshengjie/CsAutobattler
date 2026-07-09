using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Creates a five-row enemy roster on the left side of the battle HUD.</summary>
public sealed class EnemyTeamStatsPanelUI : MonoBehaviour
{
    private const int SlotCount = 5;
    private const float Spacing = 8f;
    private readonly RedTeamStatsSlotUI[] slots = new RedTeamStatsSlotUI[SlotCount];
    private float nextRefreshTime;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateRuntimePanel()
    {
        if (FindAnyObjectByType<EnemyTeamStatsPanelUI>() != null) return;

        Canvas canvas = null;
        foreach (Canvas candidate in FindObjectsByType<Canvas>(FindObjectsInactive.Include))
        {
            if (candidate.renderMode == RenderMode.WorldSpace) continue;
            if (canvas == null || candidate.name == "Canvas3D") canvas = candidate;
        }
        if (canvas == null) return;

        GameObject panelObject = new GameObject(
            "EnemyTeamStatsPanel", typeof(RectTransform), typeof(VerticalLayoutGroup),
            typeof(EnemyTeamStatsPanelUI));
        panelObject.transform.SetParent(canvas.transform, false);

        RectTransform rect = panelObject.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.anchoredPosition = new Vector2(18f, 110f);
        rect.sizeDelta = new Vector2(
            RedTeamStatsSlotUI.DisplayWidth,
            SlotCount * RedTeamStatsSlotUI.DisplayHeight + (SlotCount - 1) * Spacing);
        HudLayoutUtility.TryCopyPreviewRect("EnemyFlashcardsPreview", rect);

        VerticalLayoutGroup layout = panelObject.GetComponent<VerticalLayoutGroup>();
        layout.spacing = Spacing;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
    }

    private void Start()
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        for (int i = 0; i < SlotCount; i++) slots[i] = CreateSlot(i + 1, font);
        AssignEnemyAgents();
        nextRefreshTime = Time.unscaledTime + 0.2f;
    }

    private void Update()
    {
        if (Time.unscaledTime < nextRefreshTime) return;
        nextRefreshTime = Time.unscaledTime + 0.2f;
        foreach (RedTeamStatsSlotUI slot in slots)
            if (slot != null) slot.Refresh();
    }

    private void AssignEnemyAgents()
    {
        TeamTacticManager tacticManager = FindAnyObjectByType<TeamTacticManager>();
        TeamType controlledTeam = tacticManager != null ? tacticManager.ControlledTeam : TeamType.Red;
        TeamType enemyTeam = controlledTeam == TeamType.Red ? TeamType.Blue : TeamType.Red;

        List<AgentStats> enemies = new List<AgentStats>();
        foreach (AgentStats agent in FindObjectsByType<AgentStats>(FindObjectsInactive.Include))
        {
            if (agent != null && agent.gameObject.scene == SceneManager.GetActiveScene() &&
                agent.team == enemyTeam)
                enemies.Add(agent);
        }
        enemies.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.Ordinal));

        for (int i = 0; i < slots.Length; i++)
            slots[i].SetAgent(i < enemies.Count ? enemies[i] : null);
    }

    private RedTeamStatsSlotUI CreateSlot(int number, Font font)
    {
        GameObject slotObject = CreateUIObject("EnemyTeamSlot_" + number, transform);
        RectTransform slotRect = slotObject.GetComponent<RectTransform>();
        slotRect.sizeDelta = new Vector2(
            RedTeamStatsSlotUI.DisplayWidth, RedTeamStatsSlotUI.DisplayHeight);

        LayoutElement element = slotObject.AddComponent<LayoutElement>();
        element.minWidth = element.preferredWidth = RedTeamStatsSlotUI.DisplayWidth;
        element.minHeight = element.preferredHeight = RedTeamStatsSlotUI.DisplayHeight;

        Image background = slotObject.AddComponent<Image>();
        background.color = new Color(0.035f, 0.10f, 0.17f, 0.92f);
        background.raycastTarget = false;
        slotObject.AddComponent<CanvasGroup>();

        Image portrait = CreateUIObject("Portrait", slotRect).AddComponent<Image>();
        portrait.rectTransform.anchorMin = portrait.rectTransform.anchorMax = new Vector2(0f, 0.5f);
        portrait.rectTransform.pivot = new Vector2(0f, 0.5f);
        portrait.rectTransform.anchoredPosition = new Vector2(7f, 0f);
        portrait.rectTransform.sizeDelta = new Vector2(58f, 58f);
        portrait.raycastTarget = false;

        Text hp = CreateText("HPText", slotRect, font, new Vector2(0f, 0.5f), new Vector2(1f, 1f));
        Text details = CreateText("DetailsText", slotRect, font, new Vector2(0f, 0f), new Vector2(1f, 0.5f));

        RedTeamStatsSlotUI slot = slotObject.AddComponent<RedTeamStatsSlotUI>();
        slot.Configure(portrait, hp, details, background);
        return slot;
    }

    private static Text CreateText(string name, RectTransform parent, Font font,
        Vector2 anchorMin, Vector2 anchorMax)
    {
        Text text = CreateUIObject(name, parent).AddComponent<Text>();
        text.font = font;
        text.fontSize = 12;
        text.fontStyle = FontStyle.Bold;
        text.color = Color.white;
        text.alignment = TextAnchor.MiddleLeft;
        text.raycastTarget = false;
        text.rectTransform.anchorMin = anchorMin;
        text.rectTransform.anchorMax = anchorMax;
        text.rectTransform.offsetMin = new Vector2(72f, 2f);
        text.rectTransform.offsetMax = new Vector2(-6f, -2f);
        return text;
    }

    private static GameObject CreateUIObject(string name, Transform parent)
    {
        GameObject result = new GameObject(name, typeof(RectTransform));
        result.transform.SetParent(parent, false);
        return result;
    }
}
