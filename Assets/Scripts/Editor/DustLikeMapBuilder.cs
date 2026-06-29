#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class DustLikeMapBuilder
{
    private const string MenuPath = "Tools/CS Auto Battler/Build DustLike Map";
    private const string MapRootName = "Map_DustLike";
    private const string TemporaryMapRootName = "Map_DustLike__Building";
    private const string MaterialFolder = "Assets/Materials/DustLikeMap";
    private const string ObstacleLayerName = "Obstacle3D";

    private static readonly HashSet<string> MissingLayerWarnings = new HashSet<string>();

    [MenuItem(MenuPath, false, 2000)]
    private static void BuildDustLikeMap()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("DustLike map build skipped: exit Play Mode before running the builder.");
            return;
        }

        Scene activeScene = SceneManager.GetActiveScene();
        if (!activeScene.IsValid() || !activeScene.isLoaded)
        {
            Debug.LogError("DustLike map build failed: there is no valid, loaded active scene.");
            return;
        }

        MissingLayerWarnings.Clear();

        Material floorMaterial = CreateOrGetMaterial("Floor_Mat", new Color(0.56f, 0.49f, 0.36f));
        Material wallMaterial = CreateOrGetMaterial("Wall_Mat", new Color(0.66f, 0.55f, 0.38f));
        Material coverMaterial = CreateOrGetMaterial("Cover_Mat", new Color(0.42f, 0.29f, 0.17f));
        Material aSiteMaterial = CreateOrGetMaterial("A_Site_Mat", new Color(0.90f, 0.36f, 0.08f));
        Material bSiteMaterial = CreateOrGetMaterial("B_Site_Mat", new Color(0.10f, 0.43f, 0.88f));
        Material spawnMaterial = CreateOrGetMaterial("Spawn_Mat", new Color(0.12f, 0.72f, 0.32f));

        if (floorMaterial == null || wallMaterial == null || coverMaterial == null ||
            aSiteMaterial == null || bSiteMaterial == null || spawnMaterial == null)
        {
            Debug.LogError(
                "DustLike map build stopped before changing the scene because one or more materials could not be created.");
            return;
        }

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Build DustLike Map");

        GameObject mapRoot = null;
        bool mapCommitted = false;

        try
        {
            // Build under a temporary root first, so an existing generated map remains intact
            // until the replacement has been created successfully.
            mapRoot = new GameObject(TemporaryMapRootName);
            SceneManager.MoveGameObjectToScene(mapRoot, activeScene);
            Undo.RegisterCreatedObjectUndo(mapRoot, "Create DustLike Map");

            CreateCube(
                "Floor3D", mapRoot.transform,
                new Vector3(0f, -0.05f, 0f), new Vector3(80f, 0.1f, 60f),
                floorMaterial, false, true);

            // Boundary walls.
            CreateCube("Wall_Boundary_North", mapRoot.transform, new Vector3(0f, 1.5f, 30.5f), new Vector3(82f, 3f, 1f), wallMaterial, true, true);
            CreateCube("Wall_Boundary_South", mapRoot.transform, new Vector3(0f, 1.5f, -30.5f), new Vector3(82f, 3f, 1f), wallMaterial, true, true);
            CreateCube("Wall_Boundary_West", mapRoot.transform, new Vector3(-40.5f, 1.5f, 0f), new Vector3(1f, 3f, 62f), wallMaterial, true, true);
            CreateCube("Wall_Boundary_East", mapRoot.transform, new Vector3(40.5f, 1.5f, 0f), new Vector3(1f, 3f, 62f), wallMaterial, true, true);

            // Bomb sites are visual markers and deliberately remain on Default without colliders.
            CreateCube("BombSite_A", mapRoot.transform, new Vector3(27f, 0.03f, 18f), new Vector3(12f, 0.05f, 10f), aSiteMaterial, false, false);
            CreateCube("BombSite_B", mapRoot.transform, new Vector3(-27f, 0.03f, 17f), new Vector3(12f, 0.05f, 10f), bSiteMaterial, false, false);

            // Spawn areas are also non-obstacle visual markers.
            CreateCube("TSpawn", mapRoot.transform, new Vector3(-5f, 0.05f, -24f), new Vector3(16f, 0.05f, 8f), spawnMaterial, false, false);
            CreateCube("CTSpawn", mapRoot.transform, new Vector3(8f, 0.05f, 25f), new Vector3(16f, 0.05f, 8f), spawnMaterial, false, false);

            // Internal walls.
            CreateCube("Wall_Mid_Left_Block", mapRoot.transform, new Vector3(-13f, 1.5f, 2f), new Vector3(2f, 3f, 24f), wallMaterial, true, true);
            CreateCube("Wall_Mid_Right_Block", mapRoot.transform, new Vector3(12f, 1.5f, 0f), new Vector3(2f, 3f, 20f), wallMaterial, true, true);
            CreateCube("Wall_A_Long_Block", mapRoot.transform, new Vector3(26f, 1.5f, -8f), new Vector3(4f, 3f, 24f), wallMaterial, true, true);
            CreateCube("Wall_A_Short_Block", mapRoot.transform, new Vector3(15f, 1.5f, 14f), new Vector3(16f, 3f, 2f), wallMaterial, true, true);
            CreateCube("Wall_A_Back_Block", mapRoot.transform, new Vector3(27f, 1.5f, 25f), new Vector3(20f, 3f, 2f), wallMaterial, true, true);
            CreateCube("Wall_B_Tunnel_Right", mapRoot.transform, new Vector3(-23f, 1.5f, -8f), new Vector3(2f, 3f, 24f), wallMaterial, true, true);
            CreateCube("Wall_B_Tunnel_Top", mapRoot.transform, new Vector3(-29f, 1.5f, 4f), new Vector3(14f, 3f, 2f), wallMaterial, true, true);
            CreateCube("Wall_B_Site_Back", mapRoot.transform, new Vector3(-27f, 1.5f, 24f), new Vector3(20f, 3f, 2f), wallMaterial, true, true);
            CreateCube("Wall_CT_Mid_Block", mapRoot.transform, new Vector3(0f, 1.5f, 18f), new Vector3(14f, 3f, 2f), wallMaterial, true, true);

            // Cover boxes.
            CreateCube("Cover_A_1", mapRoot.transform, new Vector3(27f, 0.75f, 16f), new Vector3(3f, 1.5f, 3f), coverMaterial, true, true);
            CreateCube("Cover_A_2", mapRoot.transform, new Vector3(31f, 0.75f, 21f), new Vector3(4f, 1.5f, 2f), coverMaterial, true, true);
            CreateCube("Cover_B_1", mapRoot.transform, new Vector3(-27f, 0.75f, 15f), new Vector3(3f, 1.5f, 3f), coverMaterial, true, true);
            CreateCube("Cover_B_2", mapRoot.transform, new Vector3(-31f, 0.75f, 20f), new Vector3(4f, 1.5f, 2f), coverMaterial, true, true);
            CreateCube("Cover_Mid_1", mapRoot.transform, new Vector3(0f, 0.75f, 0f), new Vector3(4f, 1.5f, 3f), coverMaterial, true, true);
            CreateCube("Cover_Long_1", mapRoot.transform, new Vector3(33f, 0.75f, -4f), new Vector3(3f, 1.5f, 4f), coverMaterial, true, true);
            CreateCube("Cover_Tunnel_1", mapRoot.transform, new Vector3(-31f, 0.75f, -5f), new Vector3(3f, 1.5f, 4f), coverMaterial, true, true);

            ClearExistingMap();
            Undo.RecordObject(mapRoot, "Name DustLike Map");
            mapRoot.name = MapRootName;
            mapCommitted = true;

            UpdatePathfindingGridIfExists();
            MoveAgentsIfTheyExist();

            Selection.activeGameObject = mapRoot;
            EditorGUIUtility.PingObject(mapRoot);
            EditorSceneManager.MarkSceneDirty(activeScene);
            AssetDatabase.SaveAssets();

            Undo.CollapseUndoOperations(undoGroup);
            Debug.Log(
                "Built Map_DustLike successfully. Only a previous root-level Map_DustLike was replaced; Legacy2D was not modified.");
        }
        catch (Exception exception)
        {
            if (!mapCommitted && mapRoot != null)
            {
                UnityEngine.Object.DestroyImmediate(mapRoot);
            }

            Undo.CollapseUndoOperations(undoGroup);
            Debug.LogException(exception);
            Debug.LogError(
                "DustLike map build did not complete. The operation is grouped as one Undo step if any scene changes remain.");
        }
    }

    [MenuItem(MenuPath, true)]
    private static bool ValidateBuildDustLikeMap()
    {
        return !EditorApplication.isPlayingOrWillChangePlaymode;
    }

    private static GameObject CreateCube(
        string objectName,
        Transform parent,
        Vector3 position,
        Vector3 scale,
        Material material,
        bool useObstacleLayer,
        bool keepBoxCollider)
    {
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = objectName;
        cube.transform.SetParent(parent, false);
        cube.transform.localPosition = position;
        cube.transform.localRotation = Quaternion.identity;
        cube.transform.localScale = scale;

        MeshRenderer renderer = cube.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = material;
        }

        BoxCollider boxCollider = cube.GetComponent<BoxCollider>();
        if (keepBoxCollider && boxCollider == null)
        {
            cube.AddComponent<BoxCollider>();
        }
        else if (!keepBoxCollider && boxCollider != null)
        {
            UnityEngine.Object.DestroyImmediate(boxCollider);
        }

        if (useObstacleLayer)
        {
            SetLayerIfExists(cube, ObstacleLayerName);
        }
        else
        {
            cube.layer = 0; // Default. Bomb sites and spawn markers must not be obstacles.
        }

        Undo.RegisterCreatedObjectUndo(cube, "Create " + objectName);
        return cube;
    }

    private static Material CreateOrGetMaterial(string materialName, Color baseColor)
    {
        string[] materialGuids = AssetDatabase.FindAssets(materialName + " t:Material", new[] { "Assets" });
        List<string> exactMatches = new List<string>();

        foreach (string guid in materialGuids)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (existing != null && string.Equals(existing.name, materialName, StringComparison.Ordinal))
            {
                exactMatches.Add(assetPath);
            }
        }

        if (exactMatches.Count > 0)
        {
            exactMatches.Sort(StringComparer.OrdinalIgnoreCase);
            if (exactMatches.Count > 1)
            {
                Debug.LogWarning(
                    "Multiple materials named " + materialName + " were found. Using: " + exactMatches[0]);
            }

            return AssetDatabase.LoadAssetAtPath<Material>(exactMatches[0]);
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            shader = Shader.Find("Universal Render Pipeline/Simple Lit");
        }

        if (shader == null)
        {
            Debug.LogError(
                "Cannot create " + materialName +
                ": no URP Lit or URP Simple Lit shader was found. Confirm that URP is installed and active.");
            return null;
        }

        EnsureAssetFolderExists(MaterialFolder);

        Material material = new Material(shader)
        {
            name = materialName,
            enableInstancing = true
        };

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", baseColor);
        }
        else if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", baseColor);
        }

        if (material.HasProperty("_Metallic"))
        {
            material.SetFloat("_Metallic", 0f);
        }

        if (material.HasProperty("_Smoothness"))
        {
            material.SetFloat("_Smoothness", 0.15f);
        }

        string desiredPath = MaterialFolder + "/" + materialName + ".mat";
        string assetPathToCreate = AssetDatabase.GenerateUniqueAssetPath(desiredPath);
        AssetDatabase.CreateAsset(material, assetPathToCreate);
        return material;
    }

    private static void SetLayerIfExists(GameObject target, string layerName)
    {
        int layer = LayerMask.NameToLayer(layerName);
        if (layer >= 0)
        {
            target.layer = layer;
            return;
        }

        target.layer = 0;
        if (MissingLayerWarnings.Add(layerName))
        {
            Debug.LogWarning(
                "Layer '" + layerName +
                "' does not exist. DustLike walls and cover were left on the Default layer. " +
                "Create the layer and run the builder again to assign it automatically.");
        }
    }

    private static void ClearExistingMap()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        GameObject[] roots = activeScene.GetRootGameObjects();

        foreach (GameObject root in roots)
        {
            if (root != null && string.Equals(root.name, MapRootName, StringComparison.Ordinal))
            {
                // Only this generated root and its own children are ever deleted.
                Undo.DestroyObjectImmediate(root);
            }
        }
    }

    private static void UpdatePathfindingGridIfExists()
    {
        GameObject gridObject = FindSceneObjectByName("PathfindingGrid3D");
        if (gridObject == null)
        {
            return;
        }

        Component pathfinder = null;
        Component[] components = gridObject.GetComponents<Component>();
        foreach (Component component in components)
        {
            if (component != null &&
                string.Equals(component.GetType().Name, "AStarPathfinder3D", StringComparison.Ordinal))
            {
                pathfinder = component;
                break;
            }
        }

        if (pathfinder == null)
        {
            Debug.LogWarning(
                "PathfindingGrid3D was found, but it has no AStarPathfinder3D component. Grid settings were not changed.");
            return;
        }

        try
        {
            Undo.RecordObject(pathfinder, "Update DustLike Pathfinding Grid");

            SerializedObject serializedPathfinder = new SerializedObject(pathfinder);
            serializedPathfinder.Update();

            List<KeyValuePair<string, double>> values = new List<KeyValuePair<string, double>>
            {
                new KeyValuePair<string, double>("gridWidth", 80d),
                new KeyValuePair<string, double>("gridDepth", 60d),
                new KeyValuePair<string, double>("cellSize", 1d)
            };

            List<KeyValuePair<string, double>> reflectionFallbacks = new List<KeyValuePair<string, double>>();
            foreach (KeyValuePair<string, double> value in values)
            {
                if (!TrySetSerializedNumber(serializedPathfinder, value.Key, value.Value))
                {
                    reflectionFallbacks.Add(value);
                }
            }

            serializedPathfinder.ApplyModifiedProperties();

            foreach (KeyValuePair<string, double> fallback in reflectionFallbacks)
            {
                if (!TrySetNumericMember(pathfinder, fallback.Key, fallback.Value))
                {
                    Debug.LogWarning(
                        "AStarPathfinder3D." + fallback.Key +
                        " was not found as a compatible serialized field, field, or writable property.");
                }
            }

            EditorUtility.SetDirty(pathfinder);
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                "AStarPathfinder3D was found, but its grid settings could not be updated: " + exception.Message);
        }
    }

    private static void MoveAgentsIfTheyExist()
    {
        string[] blueAgentNames = { "BlueAgent3D_1", "BlueAgent3D_2", "BlueAgent3D_3" };
        Vector3[] bluePositions =
        {
            new Vector3(5f, 1f, 23f),
            new Vector3(10f, 1f, 23f),
            new Vector3(15f, 1f, 23f)
        };

        string[] redAgentNames = { "RedAgent3D_1", "RedAgent3D_2", "RedAgent3D_3" };
        Vector3[] redPositions =
        {
            new Vector3(-10f, 1f, -24f),
            new Vector3(-5f, 1f, -24f),
            new Vector3(0f, 1f, -24f)
        };

        MoveNamedAgents(blueAgentNames, bluePositions);
        MoveNamedAgents(redAgentNames, redPositions);
    }

    private static void MoveNamedAgents(string[] agentNames, Vector3[] positions)
    {
        for (int i = 0; i < agentNames.Length; i++)
        {
            GameObject agent = FindSceneObjectByName(agentNames[i]);
            if (agent == null)
            {
                continue;
            }

            Undo.RecordObject(agent.transform, "Move " + agentNames[i]);
            agent.transform.position = positions[i];
            EditorUtility.SetDirty(agent.transform);
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

    private static bool TrySetSerializedNumber(
        SerializedObject serializedObject,
        string propertyName,
        double value)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property == null)
        {
            return false;
        }

        if (property.propertyType == SerializedPropertyType.Integer)
        {
            property.intValue = Convert.ToInt32(value);
            return true;
        }

        if (property.propertyType == SerializedPropertyType.Float)
        {
            property.floatValue = Convert.ToSingle(value);
            return true;
        }

        return false;
    }

    private static bool TrySetNumericMember(object target, string memberName, double value)
    {
        const BindingFlags flags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        Type targetType = target.GetType();
        FieldInfo field = targetType.GetField(memberName, flags);
        if (field != null && !field.IsInitOnly && TryConvertNumber(value, field.FieldType, out object fieldValue))
        {
            field.SetValue(target, fieldValue);
            return true;
        }

        PropertyInfo property = targetType.GetProperty(memberName, flags);
        if (property != null && property.CanWrite &&
            TryConvertNumber(value, property.PropertyType, out object propertyValue))
        {
            property.SetValue(target, propertyValue, null);
            return true;
        }

        return false;
    }

    private static bool TryConvertNumber(double value, Type requestedType, out object convertedValue)
    {
        Type targetType = Nullable.GetUnderlyingType(requestedType) ?? requestedType;

        try
        {
            if (targetType == typeof(float))
            {
                convertedValue = Convert.ToSingle(value);
                return true;
            }

            if (targetType == typeof(double))
            {
                convertedValue = value;
                return true;
            }

            if (targetType == typeof(int))
            {
                convertedValue = Convert.ToInt32(value);
                return true;
            }

            if (targetType == typeof(uint))
            {
                convertedValue = Convert.ToUInt32(value);
                return true;
            }

            if (targetType == typeof(long))
            {
                convertedValue = Convert.ToInt64(value);
                return true;
            }

            if (targetType == typeof(short))
            {
                convertedValue = Convert.ToInt16(value);
                return true;
            }
        }
        catch (Exception)
        {
            // The caller will treat this member as incompatible.
        }

        convertedValue = null;
        return false;
    }

    private static void EnsureAssetFolderExists(string folderPath)
    {
        string[] parts = folderPath.Split('/');
        string currentPath = parts[0];

        for (int i = 1; i < parts.Length; i++)
        {
            string nextPath = currentPath + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(nextPath))
            {
                AssetDatabase.CreateFolder(currentPath, parts[i]);
            }

            currentPath = nextPath;
        }
    }
}
#endif
