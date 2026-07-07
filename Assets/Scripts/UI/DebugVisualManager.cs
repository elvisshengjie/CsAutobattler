using UnityEngine;
using UnityEngine.UI;

public class DebugVisualManager : MonoBehaviour
{
    public static DebugVisualManager Instance { get; private set; }

    //  Toggle States
    public bool ShowActions { get; private set; } = true;
    public bool ShowPaths { get; private set; } = true;
    public bool ShowTargets { get; private set; } = true;
    public bool ShowHeatmap { get; private set; } = false;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Initialize()
    {
        if (FindAnyObjectByType<DebugVisualManager>() == null)
        {
            GameObject go = new GameObject("DebugVisualManager");
            go.AddComponent<DebugVisualManager>();
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        BuildDebugUI();
    }

    private void BuildDebugUI()
    {
        Canvas canvas = FindAnyObjectByType<Canvas>();
        if (canvas == null) return;
        HudCanvasScaleUtility.Configure(canvas);

        GameObject panel = new GameObject("DebugPanel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
        panel.transform.SetParent(canvas.transform, false);
        
        RectTransform rect = panel.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0, 1);
        rect.anchorMax = new Vector2(0, 1);
        rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(14, -14);
        rect.sizeDelta = new Vector2(280, 172);
        
        Image bg = panel.GetComponent<Image>();
        bg.color = new Color(0.1f, 0.1f, 0.1f, 0.9f);

        VerticalLayoutGroup layout = panel.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 12, 12);
        layout.spacing = 14;
        layout.childControlWidth = false;  
        layout.childControlHeight = false; 

        AddToggle(panel.transform, "Show AI States", ShowActions, v => ShowActions = v);
        AddToggle(panel.transform, "Show Path Lines", ShowPaths, v => ShowPaths = v);
        AddToggle(panel.transform, "Show Target Markers", ShowTargets, v => ShowTargets = v);
        AddToggle(panel.transform, "Show Heatmap", ShowHeatmap, v => ShowHeatmap = v);
    }

    private void AddToggle(Transform parent, string labelText, bool defaultValue, UnityEngine.Events.UnityAction<bool> onValueChanged)
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        //  Toggle box for mouse click
        GameObject toggleObj = new GameObject(labelText, typeof(RectTransform), typeof(Image), typeof(Toggle));
        toggleObj.transform.SetParent(parent, false);
        
        RectTransform toggleRect = toggleObj.GetComponent<RectTransform>();
        toggleRect.sizeDelta = new Vector2(24, 24);

        Image bgImage = toggleObj.GetComponent<Image>();
        bgImage.color = new Color(0.2f, 0.2f, 0.2f, 1f); 

        Toggle toggle = toggleObj.GetComponent<Toggle>();
        toggle.targetGraphic = bgImage;
        toggle.isOn = defaultValue;
        
        //  Checkmark graphics
        GameObject checkObj = new GameObject("Checkmark", typeof(RectTransform), typeof(Image));
        checkObj.transform.SetParent(toggleObj.transform, false);
        Image checkImage = checkObj.GetComponent<Image>();
        checkImage.color = new Color(0.2f, 0.86f, 0.96f, 1f); 
        
        RectTransform checkRect = checkObj.GetComponent<RectTransform>();
        checkRect.sizeDelta = new Vector2(14, 14);
        checkRect.anchoredPosition = Vector2.zero;
        
        toggle.graphic = checkImage; 
        toggle.onValueChanged.AddListener(onValueChanged);

        //  Create Text Label
        GameObject labelObj = new GameObject("Label", typeof(RectTransform), typeof(Text));
        labelObj.transform.SetParent(toggleObj.transform, false);
        Text text = labelObj.GetComponent<Text>();
        text.font = font;
        text.text = labelText;
        text.color = Color.white;
        text.fontSize = 16;
        text.alignment = TextAnchor.MiddleLeft;

        RectTransform textRect = labelObj.GetComponent<RectTransform>();
        textRect.anchoredPosition = new Vector2(118, 0);
        textRect.sizeDelta = new Vector2(210, 24);
    }
}
