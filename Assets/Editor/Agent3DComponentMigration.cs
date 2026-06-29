#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class Agent3DComponentMigration
{
    private const string MenuPath = "Tools/CSGO Manager/Make 3D AI Components Editable";

    [MenuItem(MenuPath)]
    public static void MakeComponentsEditable()
    {
        int sceneAgentCount = MigrateOpenScenes();
        int prefabCount = MigratePrefabs();

        AssetDatabase.SaveAssets();
        Debug.Log(
            $"3D AI migration complete. Updated {sceneAgentCount} scene agents and " +
            $"{prefabCount} prefabs. Save the open scene to keep its new components.");
    }

    private static int MigrateOpenScenes()
    {
        AgentController3D[] controllers =
            Object.FindObjectsByType<AgentController3D>(FindObjectsInactive.Include);
        int changedCount = 0;

        foreach (AgentController3D controller in controllers)
        {
            if (EditorUtility.IsPersistent(controller))
            {
                continue;
            }

            if (InstallComponents(controller.gameObject, controller, true))
            {
                changedCount++;
                EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);
            }
        }

        return changedCount;
    }

    private static int MigratePrefabs()
    {
        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs" });
        int changedCount = 0;

        foreach (string guid in prefabGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(path);

            try
            {
                AgentController3D[] controllers =
                    prefabRoot.GetComponentsInChildren<AgentController3D>(true);
                bool prefabChanged = false;

                foreach (AgentController3D controller in controllers)
                {
                    prefabChanged |= InstallComponents(
                        controller.gameObject,
                        controller,
                        false);
                }

                if (prefabChanged)
                {
                    PrefabUtility.SaveAsPrefabAsset(prefabRoot, path);
                    changedCount++;
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        return changedCount;
    }

    private static bool InstallComponents(
        GameObject target,
        AgentController3D legacySettings,
        bool recordUndo)
    {
        bool changed = false;

        AgentSensors sensors = target.GetComponent<AgentSensors>();
        if (sensors == null)
        {
            sensors = AddComponent<AgentSensors>(target, recordUndo);
            sensors.sightRange = legacySettings.sightRange;
            sensors.fieldOfViewAngle = legacySettings.fieldOfViewAngle;
            sensors.proximityDetectionRange = legacySettings.proximityDetectionRange;
            sensors.eyeHeight = legacySettings.eyeHeight;
            sensors.lineOfSightMask = legacySettings.lineOfSightMask;
            changed = true;
        }

        AgentMemory memory = target.GetComponent<AgentMemory>();
        if (memory == null)
        {
            memory = AddComponent<AgentMemory>(target, recordUndo);
            memory.memoryDuration = legacySettings.memoryDuration;
            changed = true;
        }

        AgentMotor motor = target.GetComponent<AgentMotor>();
        if (motor == null)
        {
            motor = AddComponent<AgentMotor>(target, recordUndo);
            motor.pathRefreshTime = legacySettings.pathRefreshTime;
            motor.waypointReachDistance = legacySettings.waypointReachDistance;
            motor.rotationSpeed = legacySettings.rotationSpeed;
            motor.separationRadius = legacySettings.separationRadius;
            motor.separationStrength = legacySettings.separationStrength;
            changed = true;
        }

        if (target.GetComponent<AgentBrain>() == null)
        {
            AddComponent<AgentBrain>(target, recordUndo);
            changed = true;
        }

        if (changed)
        {
            EditorUtility.SetDirty(target);
            EditorUtility.SetDirty(legacySettings);
        }

        return changed;
    }

    private static T AddComponent<T>(GameObject target, bool recordUndo)
        where T : Component
    {
        return recordUndo
            ? Undo.AddComponent<T>(target)
            : target.AddComponent<T>();
    }
}

[CustomEditor(typeof(AgentController3D))]
public class AgentController3DCompatibilityEditor : Editor
{
    public override void OnInspectorGUI()
    {
        AgentController3D controller = (AgentController3D)target;

        EditorGUILayout.HelpBox(
            "Compatibility facade only. Do not configure AI behavior here. " +
            "Edit the dedicated components listed below.",
            MessageType.Info);

        DrawComponentStatus("Decision Making", controller.GetComponent<AgentBrain>());
        DrawComponentStatus("Vision / Detection", controller.GetComponent<AgentSensors>());
        DrawComponentStatus("Enemy Memory", controller.GetComponent<AgentMemory>());
        DrawComponentStatus("Movement / Pathfinding", controller.GetComponent<AgentMotor>());

        if (controller.GetComponent<AgentBrain>() == null ||
            controller.GetComponent<AgentSensors>() == null ||
            controller.GetComponent<AgentMemory>() == null ||
            controller.GetComponent<AgentMotor>() == null)
        {
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "Dedicated components are missing. Run Tools > CSGO Manager > " +
                "Make 3D AI Components Editable.",
                MessageType.Warning);
        }
    }

    private static void DrawComponentStatus(string label, Component component)
    {
        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.ObjectField(label, component, typeof(Component), true);
        }
    }
}
#endif
