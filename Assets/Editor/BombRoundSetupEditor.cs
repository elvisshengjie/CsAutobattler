#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class BombRoundSetupEditor
{
    private const string MenuPath = "Tools/CSGO Manager/Set Up Bomb Round Foundation";
    private const string BombPrefabPath = "Assets/Prefabs/Bomb.prefab";

    [MenuItem(MenuPath)]
    public static void SetupBombRoundFoundation()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("Exit Play Mode before setting up the bomb round.");
            return;
        }

        BombSite siteA = ConfigureSite("BombSite_A", BombSiteId.A);
        BombSite siteB = ConfigureSite("BombSite_B", BombSiteId.B);
        if (siteA == null || siteB == null)
        {
            Debug.LogError(
                "BombSite_A or BombSite_B was not found. Build the DustLike map first, then run setup again.");
            return;
        }

        RoundManager roundManager = Object.FindAnyObjectByType<RoundManager>();
        if (roundManager == null)
        {
            GameObject roundObject = new GameObject("RoundManager");
            Undo.RegisterCreatedObjectUndo(roundObject, "Create Round Manager");
            roundManager = roundObject.AddComponent<RoundManager>();
        }

        ObjectiveManager objectiveManager = Object.FindAnyObjectByType<ObjectiveManager>();
        if (objectiveManager == null)
        {
            GameObject objectiveObject = new GameObject("ObjectiveManager");
            Undo.RegisterCreatedObjectUndo(objectiveObject, "Create Objective Manager");
            objectiveManager = objectiveObject.AddComponent<ObjectiveManager>();
        }

        BombController bombPrefab = CreateOrLoadBombPrefab();

        Undo.RecordObject(roundManager, "Configure Bomb Round Timers");
        roundManager.plantDuration = 4f;
        roundManager.defuseDuration = 4f;

        Undo.RecordObject(objectiveManager, "Configure Objective Manager");
        objectiveManager.roundManager = roundManager;
        objectiveManager.siteA = siteA;
        objectiveManager.siteB = siteB;
        objectiveManager.bombPrefab = bombPrefab;
        EditorUtility.SetDirty(objectiveManager);
        EditorUtility.SetDirty(roundManager);

        Scene activeScene = SceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(activeScene);
        AssetDatabase.SaveAssets();

        Selection.activeGameObject = objectiveManager.gameObject;
        Debug.Log(
            "Bomb round foundation configured. Save the scene, enter Play Mode, wait for Active, " +
            "move the carrier into A or B, and press P for the planting test.");
    }

    private static BombSite ConfigureSite(string objectName, BombSiteId id)
    {
        GameObject siteObject = GameObject.Find(objectName);
        if (siteObject == null)
        {
            return null;
        }

        BoxCollider trigger = siteObject.GetComponent<BoxCollider>();
        if (trigger == null)
        {
            trigger = Undo.AddComponent<BoxCollider>(siteObject);
        }

        Undo.RecordObject(trigger, "Configure Bomb Site Trigger");
        trigger.isTrigger = true;

        float localTriggerHeight = 2.5f /
                                   Mathf.Max(0.001f, Mathf.Abs(siteObject.transform.lossyScale.y));
        trigger.size = new Vector3(1f, localTriggerHeight, 1f);
        trigger.center = new Vector3(0f, localTriggerHeight * 0.5f, 0f);

        BombSite site = siteObject.GetComponent<BombSite>();
        if (site == null)
        {
            site = Undo.AddComponent<BombSite>(siteObject);
        }

        Undo.RecordObject(site, "Configure Bomb Site");
        site.siteId = id;
        EditorUtility.SetDirty(trigger);
        EditorUtility.SetDirty(site);
        return site;
    }

    private static BombController CreateOrLoadBombPrefab()
    {
        GameObject existingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BombPrefabPath);
        if (existingPrefab != null)
        {
            BombController existingController = existingPrefab.GetComponent<BombController>();
            if (existingController != null)
            {
                return existingController;
            }
        }

        GameObject bombObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        bombObject.name = "Bomb";
        bombObject.transform.localScale = Vector3.one * 0.3f;

        BoxCollider collider = bombObject.GetComponent<BoxCollider>();
        collider.isTrigger = true;
        bombObject.AddComponent<BombController>();

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(bombObject, BombPrefabPath);
        Object.DestroyImmediate(bombObject);
        return prefab.GetComponent<BombController>();
    }
}
#endif
