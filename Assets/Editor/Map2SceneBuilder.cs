#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Creates Map1 as an untouched checkpoint of the original battlefield and
/// authors Map2 as a larger three-route map with a traversable raised platform.
/// </summary>
[InitializeOnLoad]
public static class Map2SceneBuilder
{
    private const string OriginalScenePath = "Assets/Scenes/BattleScene_2_5D.unity";
    private const string Map1ScenePath = "Assets/Scenes/Map1.unity";
    private const string Map2ScenePath = "Assets/Scenes/Map2.unity";
    private const string Map2RootName = "Map2_ThreeRoute_Foundry";
    private const string MaterialFolder = "Assets/Materials/Map2";
    private const string ObstacleLayerName = "Obstacle3D";
    private const string WalkableLayerName = "Walkable3D";
    private const string MenuPath = "Tools/CS Auto Battler/Build Map2 Three-Route Foundry";
    private const float SecondLevelRampWidth = 8f;
    private const float SecondLevelRampLength = 13f;
    private const float SecondLevelRampAngle = 13.5f;
    private const float SecondLevelRampCenterY = 1.294f;
    private const float SecondLevelSouthRampZ = -11.75f;
    private const float SecondLevelNorthRampZ = 15.75f;

    private static bool autoBuildQueued;

    static Map2SceneBuilder()
    {
        QueueAutomaticBuild();
    }

    private static void QueueAutomaticBuild()
    {
        if (autoBuildQueued || IsMap2Complete())
        {
            return;
        }

        autoBuildQueued = true;
        EditorApplication.delayCall += TryAutomaticBuild;
    }

    private static void TryAutomaticBuild()
    {
        autoBuildQueued = false;
        if (IsMap2Complete())
        {
            return;
        }

        if (EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode)
        {
            QueueAutomaticBuild();
            return;
        }

        BuildMap2();
    }

    [MenuItem(MenuPath, false, 2001)]
    public static void BuildMap2()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("Map2 build skipped: exit Play Mode first.");
            return;
        }

        try
        {
            if (!SaveLoadedAssetScenes())
            {
                Debug.LogError("Map2 build stopped because the currently open scene could not be saved.");
                return;
            }

            EnsureMap1Checkpoint();
            EnsureMap2Copy();

            Scene map2Scene = EditorSceneManager.OpenScene(Map2ScenePath, OpenSceneMode.Single);
            if (!map2Scene.IsValid() || !map2Scene.isLoaded)
            {
                throw new InvalidOperationException("Map2 could not be opened after it was copied from Map1.");
            }

            int walkableLayer = EnsureLayer(WalkableLayerName);
            int obstacleLayer = LayerMask.NameToLayer(ObstacleLayerName);
            if (walkableLayer < 0 || obstacleLayer < 0)
            {
                throw new InvalidOperationException(
                    "Map2 requires the Walkable3D and Obstacle3D layers.");
            }

            ClearGeneratedMapRoots(map2Scene);
            GameObject mapRoot = BuildGeometry(map2Scene, walkableLayer, obstacleLayer);
            ConfigureGameplay(mapRoot, walkableLayer);

            EditorSceneManager.MarkSceneDirty(map2Scene);
            if (!EditorSceneManager.SaveScene(map2Scene, Map2ScenePath))
            {
                throw new InvalidOperationException("Unity failed to save Map2.unity.");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeGameObject = mapRoot;
            EditorGUIUtility.PingObject(mapRoot);
            Debug.Log(
                "Map creation complete: the original scene is saved as Map1, and Map2 is a 120x90 " +
                "octagonal battlefield with three routes, A/B/C sites, and a solid C-site bridge.");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            Debug.LogError("Map2 build failed. Map1 remains preserved and can be used to retry safely.");
        }
    }

    [MenuItem("Tools/CS Auto Battler/Validate Map2", false, 2002)]
    public static void ValidateMap2()
    {
        Scene scene = EditorSceneManager.OpenScene(Map2ScenePath, OpenSceneMode.Single);
        GameObject root = scene.GetRootGameObjects().FirstOrDefault(
            candidate => candidate != null && candidate.name == Map2RootName);
        if (root == null)
        {
            throw new InvalidOperationException("Map2 root is missing.");
        }

        Transform floor = FindChild(root.transform, "Floor3D");
        Transform platform = FindChild(root.transform, "SecondLevel_Platform");
        Transform southRamp = FindChild(root.transform, "SecondLevel_SouthRamp");
        Transform northRamp = FindChild(root.transform, "SecondLevel_NorthRamp");
        Transform solidBase = FindChild(root.transform, "SecondLevel_SolidBase");
        Transform southUnderfill = FindChild(root.transform, "SecondLevel_SouthRampUnderfill_1");
        Transform northUnderfill = FindChild(root.transform, "SecondLevel_NorthRampUnderfill_1");
        Renderer floorRenderer = floor != null ? floor.GetComponent<Renderer>() : null;
        if (floorRenderer == null || floorRenderer.bounds.size.x < 120f ||
            floorRenderer.bounds.size.z < 90f)
        {
            throw new InvalidOperationException("Map2 does not meet the 120x90 octagonal footprint requirement.");
        }
        if (platform == null || southRamp == null || northRamp == null || solidBase == null ||
            southUnderfill == null || northUnderfill == null ||
            platform.position.y < 2.5f)
        {
            throw new InvalidOperationException("Map2's solid, traversable second level is incomplete.");
        }

        BombSite[] bombSites = UnityEngine.Object.FindObjectsByType<BombSite>(
            FindObjectsInactive.Include);
        if (!bombSites.Any(site => site.siteId == BombSiteId.A) ||
            !bombSites.Any(site => site.siteId == BombSiteId.B) ||
            !bombSites.Any(site => site.siteId == BombSiteId.C))
        {
            throw new InvalidOperationException("Map2 must contain functional A, B, and C bomb sites.");
        }

        int walkableLayer = LayerMask.NameToLayer(WalkableLayerName);
        if (walkableLayer < 0 || floor.gameObject.layer != walkableLayer ||
            platform.gameObject.layer != walkableLayer ||
            southRamp.gameObject.layer != walkableLayer ||
            northRamp.gameObject.layer != walkableLayer)
        {
            throw new InvalidOperationException("Map2 walkable surfaces are not on Walkable3D.");
        }

        AgentMotor[] motors = UnityEngine.Object.FindObjectsByType<AgentMotor>(
            FindObjectsInactive.Include);
        int expectedMask = 1 << walkableLayer;
        foreach (AgentMotor motor in motors)
        {
            SerializedObject serializedMotor = new SerializedObject(motor);
            SerializedProperty mask = serializedMotor.FindProperty("walkableSurfaceMask");
            if (mask == null || mask.intValue != expectedMask)
            {
                throw new InvalidOperationException(
                    "Every Map2 agent must have multi-level surface following enabled.");
            }
        }

        AStarPathfinder3D pathfinder = UnityEngine.Object.FindAnyObjectByType<AStarPathfinder3D>();
        if (pathfinder == null || pathfinder.gridWidth < 120f || pathfinder.gridDepth < 90f ||
            pathfinder.walkableSurfaceMask.value != expectedMask)
        {
            throw new InvalidOperationException("Map2's pathfinding grid is not large enough.");
        }

        Physics.SyncTransforms();
        ValidateSecondLevelRampEntrances(expectedMask);
        Vector3 attackerSpawn = new Vector3(0f, 1f, -40f);
        Vector3[] requiredDestinations =
        {
            bombSites.First(site => site.siteId == BombSiteId.A).PlantPosition,
            bombSites.First(site => site.siteId == BombSiteId.B).PlantPosition,
            bombSites.First(site => site.siteId == BombSiteId.C).PlantPosition,
            new Vector3(0f, 1f, 40f)
        };
        foreach (Vector3 destination in requiredDestinations)
        {
            List<Vector3> path = pathfinder.FindPath(attackerSpawn, destination);
            if (path == null)
            {
                throw new InvalidOperationException(
                    "Map2 path validation failed for destination " + destination + ".");
            }
        }

        Debug.Log(
            "Map2 validation passed: 120x90 octagonal footprint, three connected routes, " +
            "functional A/B/C sites, and capsule-clear solid ramps onto the C-site bridge.");
    }

    private static void ValidateSecondLevelRampEntrances(int walkableMask)
    {
        (Vector3 outside, Vector3 inside)[] seams =
        {
            (new Vector3(-3f, 0f, -18.25f), new Vector3(-3f, 0f, -17.9f)),
            (new Vector3(3f, 0f, 22.25f), new Vector3(3f, 0f, 21.9f)),
            (new Vector3(-3f, 0f, -5.6f), new Vector3(-3f, 0f, -5.4f)),
            (new Vector3(3f, 0f, 9.6f), new Vector3(3f, 0f, 9.4f))
        };
        foreach ((Vector3 outside, Vector3 inside) seam in seams)
        {
            float outsideY = GetWalkableSurfaceHeight(seam.outside, walkableMask);
            float insideY = GetWalkableSurfaceHeight(seam.inside, walkableMask);
            if (Mathf.Abs(outsideY - insideY) > 0.08f)
            {
                throw new InvalidOperationException(
                    $"Map2 ramp seam is too tall for agents: {outsideY:0.000} -> {insideY:0.000}.");
            }
        }

        int obstacleMask = LayerMask.GetMask(ObstacleLayerName);
        Vector3[] entranceCenters =
        {
            new Vector3(-3f, 1f, -17.9f),
            new Vector3(3f, 1f, 21.9f)
        };
        foreach (Vector3 center in entranceCenters)
        {
            if (Physics.CheckCapsule(
                    center + Vector3.down * 0.4f,
                    center + Vector3.up * 0.4f,
                    0.48f,
                    obstacleMask,
                    QueryTriggerInteraction.Ignore))
            {
                throw new InvalidOperationException(
                    "Map2 ramp entrance overlaps an obstacle at " + center + ".");
            }
        }
    }

    private static float GetWalkableSurfaceHeight(Vector3 position, int walkableMask)
    {
        if (!Physics.Raycast(
                position + Vector3.up * 10f,
                Vector3.down,
                out RaycastHit hit,
                20f,
                walkableMask,
                QueryTriggerInteraction.Ignore))
        {
            throw new InvalidOperationException(
                "Map2 has no walkable surface at ramp seam " + position + ".");
        }

        return hit.point.y;
    }

    private static bool IsMap2Complete()
    {
        if (!File.Exists(Map2ScenePath))
        {
            return false;
        }

        try
        {
            return File.ReadAllText(Map2ScenePath).Contains("m_Name: " + Map2RootName);
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static bool SaveLoadedAssetScenes()
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (!scene.IsValid() || !scene.isLoaded || !scene.isDirty ||
                string.IsNullOrEmpty(scene.path))
            {
                continue;
            }

            if (!EditorSceneManager.SaveScene(scene))
            {
                return false;
            }
        }

        return true;
    }

    private static void EnsureMap1Checkpoint()
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(Map1ScenePath) != null)
        {
            return;
        }

        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(OriginalScenePath) == null)
        {
            throw new FileNotFoundException(
                "Neither the original BattleScene_2_5D scene nor Map1 could be found.");
        }

        string moveError = AssetDatabase.MoveAsset(OriginalScenePath, Map1ScenePath);
        if (!string.IsNullOrEmpty(moveError))
        {
            throw new InvalidOperationException("Could not rename the original scene to Map1: " + moveError);
        }

        AssetDatabase.SaveAssets();
    }

    private static void EnsureMap2Copy()
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(Map2ScenePath) != null)
        {
            return;
        }

        if (!AssetDatabase.CopyAsset(Map1ScenePath, Map2ScenePath))
        {
            throw new InvalidOperationException("Could not copy Map1 to create Map2.");
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private static GameObject BuildGeometry(
        Scene scene,
        int walkableLayer,
        int obstacleLayer)
    {
        Material floorMaterial = GetOrCreateMaterial(
            "Map2_Floor", new Color(0.34f, 0.38f, 0.40f));
        Material wallMaterial = GetOrCreateMaterial(
            "Map2_Wall", new Color(0.25f, 0.29f, 0.31f));
        Material coverMaterial = GetOrCreateMaterial(
            "Map2_Cover", new Color(0.48f, 0.31f, 0.16f));
        Material platformMaterial = GetOrCreateMaterial(
            "Map2_Platform", new Color(0.22f, 0.48f, 0.50f));
        Material aSiteMaterial = GetOrCreateMaterial(
            "Map2_SiteA", new Color(0.94f, 0.30f, 0.07f));
        Material bSiteMaterial = GetOrCreateMaterial(
            "Map2_SiteB", new Color(0.08f, 0.40f, 0.92f));
        Material cSiteMaterial = GetOrCreateMaterial(
            "Map2_SiteC", new Color(0.78f, 0.18f, 0.72f));
        Material spawnMaterial = GetOrCreateMaterial(
            "Map2_Spawn", new Color(0.13f, 0.72f, 0.32f));
        Material routeMaterial = GetOrCreateMaterial(
            "Map2_RouteAccent", new Color(0.72f, 0.60f, 0.18f));

        GameObject root = new GameObject(Map2RootName);
        SceneManager.MoveGameObjectToScene(root, scene);

        Transform ground = CreateGroup("01_Ground", root.transform);
        Transform boundaries = CreateGroup("02_Boundaries", root.transform);
        Transform west = CreateGroup("03_West_Route", root.transform);
        Transform middle = CreateGroup("04_Central_Route", root.transform);
        Transform east = CreateGroup("05_East_Route", root.transform);
        Transform elevation = CreateGroup("06_Second_Level", root.transform);
        Transform sites = CreateGroup("07_Bomb_Sites", root.transform);
        Transform tactical = CreateGroup("08_Tactical_Markers", root.transform);

        CreateOctagonalFloor(ground, floorMaterial, walkableLayer);

        // The 9,669-square-unit octagon is just over twice Map1's 80x60 area.
        // Its clipped corners and asymmetric internal diagonals create a distinct
        // silhouette rather than another rectangular arena.
        Vector3[] outline =
        {
            new Vector3(-35f, 0f, -45f), new Vector3(35f, 0f, -45f),
            new Vector3(60f, 0f, -20f), new Vector3(60f, 0f, 22f),
            new Vector3(38f, 0f, 45f), new Vector3(-38f, 0f, 45f),
            new Vector3(-60f, 0f, 22f), new Vector3(-60f, 0f, -20f)
        };
        for (int i = 0; i < outline.Length; i++)
        {
            CreateBoundarySegment(
                "Boundary_Octagon_" + (i + 1),
                boundaries,
                outline[i],
                outline[(i + 1) % outline.Length],
                wallMaterial,
                obstacleLayer);
        }

        // Broken diagonal spines form three lanes with six cross-route rotation
        // windows. The two sides deliberately use different angles and pacing.
        CreateCube("West_Spine_South", west, new Vector3(-18f, 1.5f, -31f), new Vector3(2f, 3f, 17f), wallMaterial, obstacleLayer, true, new Vector3(0f, -18f, 0f));
        CreateCube("West_Spine_Middle", west, new Vector3(-24f, 1.5f, -8f), new Vector3(2f, 3f, 13f), wallMaterial, obstacleLayer, true, new Vector3(0f, 14f, 0f));
        CreateCube("West_Spine_North", west, new Vector3(-18f, 1.5f, 19f), new Vector3(2f, 3f, 17f), wallMaterial, obstacleLayer, true, new Vector3(0f, -27f, 0f));
        CreateCube("East_Spine_South", east, new Vector3(18f, 1.5f, -30f), new Vector3(2f, 3f, 18f), wallMaterial, obstacleLayer, true, new Vector3(0f, 22f, 0f));
        CreateCube("East_Spine_Middle", east, new Vector3(25f, 1.5f, -7f), new Vector3(2f, 3f, 12f), wallMaterial, obstacleLayer, true, new Vector3(0f, -16f, 0f));
        CreateCube("East_Spine_North", east, new Vector3(18f, 1.5f, 18f), new Vector3(2f, 3f, 16f), wallMaterial, obstacleLayer, true, new Vector3(0f, 30f, 0f));

        // West route: tight corners and short-range ambush pockets.
        CreateCube("West_Crook_South", west, new Vector3(-46f, 1.5f, -19f), new Vector3(22f, 3f, 2f), wallMaterial, obstacleLayer, true);
        CreateCube("West_Crook_Middle", west, new Vector3(-36f, 1.5f, 2f), new Vector3(28f, 3f, 2f), wallMaterial, obstacleLayer, true);
        CreateCube("West_Crook_North", west, new Vector3(-49f, 1.5f, 17f), new Vector3(20f, 3f, 2f), wallMaterial, obstacleLayer, true);
        CreateCover("Cover_West_South_1", west, new Vector3(-31f, 0.8f, -29f), new Vector3(4f, 1.6f, 3f), coverMaterial, obstacleLayer);
        CreateCover("Cover_West_South_2", west, new Vector3(-51f, 0.8f, -9f), new Vector3(3f, 1.6f, 5f), coverMaterial, obstacleLayer);
        CreateCover("Cover_West_Middle_1", west, new Vector3(-29f, 0.8f, 10f), new Vector3(5f, 1.6f, 3f), coverMaterial, obstacleLayer);
        CreateCover("Cover_West_North_1", west, new Vector3(-52f, 0.8f, 25f), new Vector3(4f, 1.6f, 4f), coverMaterial, obstacleLayer);

        // East route: longer sightlines broken by offset cover and two entry forks.
        CreateCube("East_Long_South", east, new Vector3(39f, 1.5f, -16f), new Vector3(2f, 3f, 26f), wallMaterial, obstacleLayer, true);
        CreateCube("East_Long_Middle", east, new Vector3(48f, 1.5f, 2f), new Vector3(24f, 3f, 2f), wallMaterial, obstacleLayer, true);
        CreateCube("East_Long_North", east, new Vector3(33f, 1.5f, 17f), new Vector3(24f, 3f, 2f), wallMaterial, obstacleLayer, true);
        CreateCover("Cover_East_South_1", east, new Vector3(29f, 0.8f, -30f), new Vector3(4f, 1.6f, 3f), coverMaterial, obstacleLayer);
        CreateCover("Cover_East_South_2", east, new Vector3(51f, 0.8f, -18f), new Vector3(3f, 1.6f, 5f), coverMaterial, obstacleLayer);
        CreateCover("Cover_East_Middle_1", east, new Vector3(30f, 0.8f, -1f), new Vector3(5f, 1.6f, 3f), coverMaterial, obstacleLayer);
        CreateCover("Cover_East_North_1", east, new Vector3(51f, 0.8f, 12f), new Vector3(4f, 1.6f, 4f), coverMaterial, obstacleLayer);

        // Central route: exposed approach, low cover, and the raised control platform.
        CreateCube("Mid_South_Screen", middle, new Vector3(-2f, 1.5f, -26f), new Vector3(14f, 3f, 2f), wallMaterial, obstacleLayer, true, new Vector3(0f, 18f, 0f));
        CreateCube("Mid_North_Screen", middle, new Vector3(3f, 1.5f, 27f), new Vector3(16f, 3f, 2f), wallMaterial, obstacleLayer, true, new Vector3(0f, -15f, 0f));
        CreateCover("Cover_Mid_South_Left", middle, new Vector3(-10f, 0.8f, -33f), new Vector3(3f, 1.6f, 4f), coverMaterial, obstacleLayer);
        CreateCover("Cover_Mid_South_Right", middle, new Vector3(10f, 0.8f, -20f), new Vector3(3f, 1.6f, 4f), coverMaterial, obstacleLayer);
        CreateCover("Cover_Mid_North_Left", middle, new Vector3(-9f, 0.8f, 20f), new Vector3(4f, 1.6f, 3f), coverMaterial, obstacleLayer);
        CreateCover("Cover_Mid_North_Right", middle, new Vector3(10f, 0.8f, 34f), new Vector3(4f, 1.6f, 3f), coverMaterial, obstacleLayer);

        BuildSecondLevel(elevation, walkableLayer, obstacleLayer, platformMaterial, wallMaterial, coverMaterial);

        BombSite siteA = CreateBombSite(
            "BombSite_A", sites, BombSiteId.A, new Vector3(43f, 0.04f, 29f),
            new Vector3(15f, 0.08f, 12f), aSiteMaterial);
        BombSite siteB = CreateBombSite(
            "BombSite_B", sites, BombSiteId.B, new Vector3(-43f, 0.04f, 29f),
            new Vector3(15f, 0.08f, 12f), bSiteMaterial);
        BombSite siteC = CreateBombSite(
            "BombSite_C_Bridge", sites, BombSiteId.C, new Vector3(0f, 3.04f, 2f),
            new Vector3(13f, 0.08f, 7f), cSiteMaterial);

        CreateCube("Wall_A_Back", sites, new Vector3(44f, 1.5f, 38f), new Vector3(26f, 3f, 2f), wallMaterial, obstacleLayer, true);
        CreateCube("Wall_A_Outer", sites, new Vector3(56f, 1.5f, 29f), new Vector3(2f, 3f, 16f), wallMaterial, obstacleLayer, true);
        CreateCover("Cover_A_Default", sites, new Vector3(43f, 0.9f, 28f), new Vector3(4f, 1.8f, 4f), coverMaterial, obstacleLayer);
        CreateCover("Cover_A_Entry", sites, new Vector3(35f, 0.8f, 24f), new Vector3(3f, 1.6f, 5f), coverMaterial, obstacleLayer);
        CreateCover("DefenderHold_A_Back", sites, new Vector3(50f, 0.8f, 34f), new Vector3(4f, 1.6f, 3f), coverMaterial, obstacleLayer);

        CreateCube("Wall_B_Back", sites, new Vector3(-44f, 1.5f, 38f), new Vector3(26f, 3f, 2f), wallMaterial, obstacleLayer, true);
        CreateCube("Wall_B_Outer", sites, new Vector3(-56f, 1.5f, 29f), new Vector3(2f, 3f, 16f), wallMaterial, obstacleLayer, true);
        CreateCover("Cover_B_Default", sites, new Vector3(-43f, 0.9f, 28f), new Vector3(4f, 1.8f, 4f), coverMaterial, obstacleLayer);
        CreateCover("Cover_B_Entry", sites, new Vector3(-35f, 0.8f, 24f), new Vector3(3f, 1.6f, 5f), coverMaterial, obstacleLayer);
        CreateCover("DefenderHold_B_Back", sites, new Vector3(-50f, 0.8f, 34f), new Vector3(4f, 1.6f, 3f), coverMaterial, obstacleLayer);

        CreateCover("DefenderHold_C_North", sites, new Vector3(0f, 3.75f, 6f), new Vector3(3f, 1.4f, 2f), coverMaterial, obstacleLayer);

        CreateCube("TSpawn", tactical, new Vector3(0f, 0.04f, -40f), new Vector3(20f, 0.08f, 8f), spawnMaterial, 0, false);
        CreateCube("CTSpawn", tactical, new Vector3(0f, 0.04f, 40f), new Vector3(20f, 0.08f, 8f), spawnMaterial, 0, false);

        CreateRouteMarker("Route_West", tactical, new Vector3(-30f, 0.025f, -35f), new Vector3(14f, 0.05f, 3f), routeMaterial);
        CreateRouteMarker("Route_Center", tactical, new Vector3(0f, 0.025f, -35f), new Vector3(14f, 0.05f, 3f), routeMaterial);
        CreateRouteMarker("Route_East", tactical, new Vector3(30f, 0.025f, -35f), new Vector3(14f, 0.05f, 3f), routeMaterial);

        CreateStagingPoint("A_Feint_Staging_East", tactical, BombSiteId.A, new Vector3(30f, 0f, -10f));
        CreateStagingPoint("B_Feint_Staging_West", tactical, BombSiteId.B, new Vector3(-29f, 0f, -12f));
        CreateStagingPoint("C_Feint_Staging_Center", tactical, BombSiteId.C, new Vector3(-8f, 0f, -19f));

        ObjectiveManager objective = UnityEngine.Object.FindAnyObjectByType<ObjectiveManager>();
        if (objective != null)
        {
            objective.siteA = siteA;
            objective.siteB = siteB;
            objective.siteC = siteC;
            EditorUtility.SetDirty(objective);
        }

        return root;
    }

    private static void CreateOctagonalFloor(
        Transform parent,
        Material material,
        int walkableLayer)
    {
        const string meshPath = MaterialFolder + "/Map2_OctagonalFloor.asset";
        Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
        if (mesh == null)
        {
            mesh = new Mesh { name = "Map2_OctagonalFloor" };
            AssetDatabase.CreateAsset(mesh, meshPath);
        }
        else
        {
            mesh.Clear();
        }

        Vector3[] vertices =
        {
            new Vector3(-35f, 0f, -45f), new Vector3(35f, 0f, -45f),
            new Vector3(60f, 0f, -20f), new Vector3(60f, 0f, 22f),
            new Vector3(38f, 0f, 45f), new Vector3(-38f, 0f, 45f),
            new Vector3(-60f, 0f, 22f), new Vector3(-60f, 0f, -20f)
        };
        int[] triangles =
        {
            0, 2, 1,
            0, 3, 2,
            0, 4, 3,
            0, 5, 4,
            0, 6, 5,
            0, 7, 6
        };
        Vector2[] uv = vertices
            .Select(vertex => new Vector2(
                (vertex.x + 60f) / 120f,
                (vertex.z + 45f) / 90f))
            .ToArray();
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.uv = uv;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        EditorUtility.SetDirty(mesh);

        GameObject floor = new GameObject("Floor3D");
        floor.transform.SetParent(parent, false);
        floor.layer = walkableLayer;
        MeshFilter filter = floor.AddComponent<MeshFilter>();
        MeshRenderer renderer = floor.AddComponent<MeshRenderer>();
        MeshCollider collider = floor.AddComponent<MeshCollider>();
        filter.sharedMesh = mesh;
        renderer.sharedMaterial = material;
        collider.sharedMesh = mesh;
    }

    private static void CreateBoundarySegment(
        string name,
        Transform parent,
        Vector3 from,
        Vector3 to,
        Material material,
        int obstacleLayer)
    {
        Vector3 direction = to - from;
        float length = direction.magnitude;
        float angle = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        CreateCube(
            name,
            parent,
            (from + to) * 0.5f + Vector3.up * 2f,
            new Vector3(1f, 4f, length + 0.5f),
            material,
            obstacleLayer,
            true,
            new Vector3(0f, angle, 0f));
    }

    private static void CreateDivider(
        Transform parent,
        float x,
        Material material,
        int obstacleLayer,
        string prefix)
    {
        CreateCube(prefix + "_Divider_South", parent, new Vector3(x, 1.5f, -34f), new Vector3(2f, 3f, 18f), material, obstacleLayer, true);
        CreateCube(prefix + "_Divider_LowerMid", parent, new Vector3(x, 1.5f, -9f), new Vector3(2f, 3f, 14f), material, obstacleLayer, true);
        CreateCube(prefix + "_Divider_UpperMid", parent, new Vector3(x, 1.5f, 10f), new Vector3(2f, 3f, 10f), material, obstacleLayer, true);
        CreateCube(prefix + "_Divider_North", parent, new Vector3(x, 1.5f, 33f), new Vector3(2f, 3f, 16f), material, obstacleLayer, true);
    }

    private static void BuildSecondLevel(
        Transform parent,
        int walkableLayer,
        int obstacleLayer,
        Material platformMaterial,
        Material wallMaterial,
        Material coverMaterial)
    {
        CreateCube("SecondLevel_Platform", parent, new Vector3(0f, 2.85f, 2f), new Vector3(20f, 0.3f, 15f), platformMaterial, walkableLayer, true);
        CreateCube("SecondLevel_SouthRamp", parent, new Vector3(-3f, SecondLevelRampCenterY, SecondLevelSouthRampZ), new Vector3(SecondLevelRampWidth, 0.4f, SecondLevelRampLength), platformMaterial, walkableLayer, true, new Vector3(-SecondLevelRampAngle, 0f, 0f));
        CreateCube("SecondLevel_NorthRamp", parent, new Vector3(3f, SecondLevelRampCenterY, SecondLevelNorthRampZ), new Vector3(SecondLevelRampWidth, 0.4f, SecondLevelRampLength), platformMaterial, walkableLayer, true, new Vector3(SecondLevelRampAngle, 0f, 0f));
        CreateRampUnderfillSteps(
            "SecondLevel_SouthRampUnderfill",
            parent,
            new Vector3(-3f, 0f, SecondLevelSouthRampZ),
            true,
            wallMaterial,
            obstacleLayer);
        CreateRampUnderfillSteps(
            "SecondLevel_NorthRampUnderfill",
            parent,
            new Vector3(3f, 0f, SecondLevelNorthRampZ),
            false,
            wallMaterial,
            obstacleLayer);

        // A single solid obstacle fills the complete volume beneath the bridge.
        // Ground-level units cannot enter it from any angle; the offset ramps are
        // the only routes onto the C-site deck.
        CreateCube("SecondLevel_SolidBase", parent, new Vector3(0f, 1.4f, 2f), new Vector3(20f, 2.8f, 15f), wallMaterial, obstacleLayer, true);
        CreateCube("SecondLevel_WestRail", parent, new Vector3(-9.6f, 3.6f, 2f), new Vector3(0.8f, 1.2f, 15f), wallMaterial, obstacleLayer, true);
        CreateCube("SecondLevel_EastRail", parent, new Vector3(9.6f, 3.6f, 2f), new Vector3(0.8f, 1.2f, 15f), wallMaterial, obstacleLayer, true);

        CreateCover("Cover_SecondLevel_Left", parent, new Vector3(-5.5f, 3.75f, 0f), new Vector3(2.5f, 1.5f, 3f), coverMaterial, obstacleLayer);
        CreateCover("Cover_SecondLevel_Right", parent, new Vector3(5.5f, 3.75f, 4f), new Vector3(2.5f, 1.5f, 3f), coverMaterial, obstacleLayer);
    }

    private static void CreateRampUnderfillSteps(
        string prefix,
        Transform parent,
        Vector3 center,
        bool risesTowardPositiveZ,
        Material material,
        int obstacleLayer)
    {
        const int stepCount = 8;
        const float rampBottomRise = 3.06f;
        const float lowEndBottomHeight = -0.44f;
        const float undersideSafetyGap = 0.08f;
        const float supportBottom = -0.15f;
        float segmentLength = SecondLevelRampLength / stepCount;
        float startZ = center.z - SecondLevelRampLength * 0.5f;

        for (int i = 0; i < stepCount; i++)
        {
            int segmentFromLowEnd = risesTowardPositiveZ
                ? i
                : stepCount - i - 1;
            float progressAtSegmentLowEdge = segmentFromLowEnd / (float)stepCount;
            float rampUndersideAtLowEdge =
                lowEndBottomHeight + rampBottomRise * progressAtSegmentLowEdge;
            float top = Mathf.Max(0f, rampUndersideAtLowEdge - undersideSafetyGap);
            float height = top - supportBottom;
            Vector3 position = new Vector3(
                center.x,
                (top + supportBottom) * 0.5f,
                startZ + (i + 0.5f) * segmentLength);
            CreateCube(
                prefix + "_" + (i + 1),
                parent,
                position,
                new Vector3(SecondLevelRampWidth, height, segmentLength + 0.18f),
                material,
                obstacleLayer,
                true);
        }
    }

    private static void ConfigureGameplay(GameObject mapRoot, int walkableLayer)
    {
        AgentStats[] agents = UnityEngine.Object.FindObjectsByType<AgentStats>(
            FindObjectsInactive.Include);
        List<AgentStats> red = agents
            .Where(agent => agent != null && agent.team == TeamType.Red)
            .OrderBy(agent => agent.name, StringComparer.Ordinal)
            .ToList();
        List<AgentStats> blue = agents
            .Where(agent => agent != null && agent.team == TeamType.Blue)
            .OrderBy(agent => agent.name, StringComparer.Ordinal)
            .ToList();

        MoveTeamToSpawn(red, -40f);
        MoveTeamToSpawn(blue, 40f);

        LayerMask walkableMask = 1 << walkableLayer;
        foreach (AgentStats agent in agents)
        {
            AgentMotor motor = agent != null ? agent.GetComponent<AgentMotor>() : null;
            if (motor == null)
            {
                continue;
            }

            motor.ConfigureWalkableSurfaceMask(walkableMask);
            EditorUtility.SetDirty(motor);
        }

        AStarPathfinder3D pathfinder = UnityEngine.Object.FindAnyObjectByType<AStarPathfinder3D>();
        if (pathfinder != null)
        {
            pathfinder.gridWidth = 120f;
            pathfinder.gridDepth = 90f;
            pathfinder.cellSize = 1f;
            pathfinder.walkableSurfaceMask = walkableMask;
            pathfinder.surfaceProbeHeight = 10f;
            pathfinder.maximumNeighborHeightDelta = 0.75f;
            pathfinder.transform.position = Vector3.zero;
            EditorUtility.SetDirty(pathfinder);
            EditorUtility.SetDirty(pathfinder.transform);
        }

        Renderer floorRenderer = FindChild(mapRoot.transform, "Floor3D")?.GetComponent<Renderer>();
        CameraController3DTopDown cameraController =
            UnityEngine.Object.FindAnyObjectByType<CameraController3DTopDown>();
        if (cameraController != null)
        {
            cameraController.floorRenderer = floorRenderer;
            cameraController.maxFieldOfView = 80f;
            EditorUtility.SetDirty(cameraController);
        }

        foreach (MiniMapClickMover3D mover in
                 UnityEngine.Object.FindObjectsByType<MiniMapClickMover3D>(
                     FindObjectsInactive.Include))
        {
            mover.floorRenderer = floorRenderer;
            EditorUtility.SetDirty(mover);
        }

        foreach (MiniMapViewportIndicator3D indicator in
                 UnityEngine.Object.FindObjectsByType<MiniMapViewportIndicator3D>(
                     FindObjectsInactive.Include))
        {
            indicator.floorRenderer = floorRenderer;
            EditorUtility.SetDirty(indicator);
        }

        GameObject mainCameraObject = GameObject.Find("Main Camera");
        if (mainCameraObject != null)
        {
            mainCameraObject.transform.position = new Vector3(0f, 38f, -32f);
            Camera mainCamera = mainCameraObject.GetComponent<Camera>();
            if (mainCamera != null)
            {
                mainCamera.fieldOfView = 72f;
                EditorUtility.SetDirty(mainCamera);
            }
            EditorUtility.SetDirty(mainCameraObject.transform);
        }

        GameObject miniMapObject = GameObject.Find("MiniMapCamera3D");
        if (miniMapObject != null)
        {
            miniMapObject.transform.position = new Vector3(0f, 100f, 0f);
            Camera miniMapCamera = miniMapObject.GetComponent<Camera>();
            if (miniMapCamera != null)
            {
                miniMapCamera.orthographic = true;
                miniMapCamera.orthographicSize = 61f;
                EditorUtility.SetDirty(miniMapCamera);
            }
            EditorUtility.SetDirty(miniMapObject.transform);
        }

        Physics.SyncTransforms();
    }

    private static void MoveTeamToSpawn(List<AgentStats> team, float z)
    {
        for (int i = 0; i < team.Count; i++)
        {
            float x = (i - (team.Count - 1) * 0.5f) * 3f;
            team[i].transform.position = new Vector3(x, 1f, z);
            team[i].transform.rotation = Quaternion.Euler(
                0f,
                z < 0f ? 0f : 180f,
                0f);
            EditorUtility.SetDirty(team[i].transform);
        }
    }

    private static BombSite CreateBombSite(
        string name,
        Transform parent,
        BombSiteId id,
        Vector3 position,
        Vector3 scale,
        Material material)
    {
        GameObject siteObject = CreateCube(name, parent, position, scale, material, 0, true);
        BoxCollider trigger = siteObject.GetComponent<BoxCollider>();
        trigger.isTrigger = true;
        BombSite site = siteObject.AddComponent<BombSite>();
        site.siteId = id;
        site.plantPositionOffset = new Vector3(0f, 0.15f, 0f);
        return site;
    }

    private static void CreateStagingPoint(
        string name,
        Transform parent,
        BombSiteId siteId,
        Vector3 position)
    {
        GameObject marker = new GameObject(name);
        marker.transform.SetParent(parent, false);
        marker.transform.localPosition = position;
        TacticStagingPoint staging = marker.AddComponent<TacticStagingPoint>();
        staging.site = siteId;
        staging.mustBeOutsideSite = true;
        staging.mustBeHiddenFromDefenders = true;
        staging.spaceForAgents = 4f;
    }

    private static void CreateRouteMarker(
        string name,
        Transform parent,
        Vector3 position,
        Vector3 scale,
        Material material)
    {
        CreateCube(name, parent, position, scale, material, 0, false);
    }

    private static void CreateCover(
        string name,
        Transform parent,
        Vector3 position,
        Vector3 scale,
        Material material,
        int obstacleLayer)
    {
        CreateCube(name, parent, position, scale, material, obstacleLayer, true);
    }

    private static GameObject CreateCube(
        string name,
        Transform parent,
        Vector3 position,
        Vector3 scale,
        Material material,
        int layer,
        bool keepCollider,
        Vector3? rotation = null)
    {
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = name;
        cube.transform.SetParent(parent, false);
        cube.transform.localPosition = position;
        cube.transform.localRotation = Quaternion.Euler(rotation ?? Vector3.zero);
        cube.transform.localScale = scale;
        cube.layer = layer;

        MeshRenderer renderer = cube.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = material;
        }

        BoxCollider collider = cube.GetComponent<BoxCollider>();
        if (!keepCollider && collider != null)
        {
            UnityEngine.Object.DestroyImmediate(collider);
        }

        return cube;
    }

    private static Transform CreateGroup(string name, Transform parent)
    {
        GameObject group = new GameObject(name);
        group.transform.SetParent(parent, false);
        return group.transform;
    }

    private static Transform FindChild(Transform root, string name)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (string.Equals(child.name, name, StringComparison.Ordinal))
            {
                return child;
            }
        }

        return null;
    }

    private static void ClearGeneratedMapRoots(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root != null &&
                (string.Equals(root.name, "Map_DustLike", StringComparison.Ordinal) ||
                 string.Equals(root.name, Map2RootName, StringComparison.Ordinal) ||
                 string.Equals(root.name, "Floor3D", StringComparison.Ordinal)))
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }
    }

    private static Material GetOrCreateMaterial(string name, Color color)
    {
        EnsureFolder(MaterialFolder);
        string path = MaterialFolder + "/" + name + ".mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null)
        {
            return material;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ??
                        Shader.Find("Universal Render Pipeline/Simple Lit");
        if (shader == null)
        {
            throw new InvalidOperationException("No compatible URP shader was found for Map2 materials.");
        }

        material = new Material(shader)
        {
            name = name,
            enableInstancing = true
        };
        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }
        else if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }
        if (material.HasProperty("_Smoothness"))
        {
            material.SetFloat("_Smoothness", 0.18f);
        }

        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static void EnsureFolder(string folderPath)
    {
        string[] parts = folderPath.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, parts[i]);
            }
            current = next;
        }
    }

    private static int EnsureLayer(string layerName)
    {
        int existing = LayerMask.NameToLayer(layerName);
        if (existing >= 0)
        {
            return existing;
        }

        UnityEngine.Object[] tagManagerAssets =
            AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
        if (tagManagerAssets.Length == 0)
        {
            return -1;
        }

        SerializedObject tagManager = new SerializedObject(tagManagerAssets[0]);
        SerializedProperty layers = tagManager.FindProperty("layers");
        for (int i = 7; i < layers.arraySize; i++)
        {
            SerializedProperty layer = layers.GetArrayElementAtIndex(i);
            if (string.IsNullOrEmpty(layer.stringValue))
            {
                layer.stringValue = layerName;
                tagManager.ApplyModifiedProperties();
                AssetDatabase.SaveAssets();
                return i;
            }
        }

        return -1;
    }
}
#endif
