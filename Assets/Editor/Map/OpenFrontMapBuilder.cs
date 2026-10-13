using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Replaces Map1's corridor blockout with the multi-level "Open Front"
/// battlefield (see OpenFrontLayout): heightfield terrain with plateau, quarry
/// pit, hill and trenches, CC0 props as cover, and the naming conventions the
/// tactical AI relies on (Cover_*, Wall_*, RoutePoint_*, ChokePoint_*, TSpawn,
/// CTSpawn, BombSite_A/B). Gameplay objects (agents, managers, UI) are kept.
/// Menu: CsAutobattler/Map/Build Open Front
/// </summary>
public static class OpenFrontMapBuilder
{
    private const string ScenePath = "Assets/Scenes/Map1.unity";
    private const string OutputFolder = "Assets/Art/Map";
    private const string RootName = "Map_OpenFront";
    private const int ObstacleLayer = 6;  // Obstacle3D
    private const int WalkableLayer = 7;  // Walkable3D
    private const float TerrainResolution = 0.5f;

    private static Transform root;
    private static int propIndex;

    [MenuItem("CsAutobattler/Map/Build Open Front")]
    public static void Build()
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Directory.CreateDirectory(OutputFolder);
        propIndex = 0;

        Material terrainMaterial = CreateTerrainMaterial();
        // Zones sit on raised/sunken ground: never apply the wall-top tint.
        foreach (string zoneMaterial in new[] { "DustLikeMap/A_Site_Mat", "DustLikeMap/B_Site_Mat", "DustLikeMap/Spawn_Mat", "Floor_Mat" })
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>($"Assets/Materials/{zoneMaterial}.mat");
            if (material != null && material.HasProperty("_CapTint"))
            {
                material.SetColor("_CapTint", new Color(1f, 1f, 1f, 0f));
                EditorUtility.SetDirty(material);
            }
        }
        Transform preserved = PreserveGameplayObjects();
        RemoveOldGeometry();

        root = new GameObject(RootName).transform;
        preserved.SetParent(root, true);

        BuildTerrain(terrainMaterial);
        BuildSkirt(terrainMaterial);
        PlaceGameplayZones();
        BuildCompound();
        PlaceProps();
        PlaceRouteMarkers();
        BuildBoundary();
        ConfigureNavigation();
        SnapAgents();
        Object.DestroyImmediate(preserved.gameObject);
        VisualOverhaulBuilder.FrameMainCamera();

        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();
        AssetDatabase.SaveAssets();
        Debug.Log($"[OpenFront] Built map with {propIndex} props.");
    }

    // --- Scene cleanup ----------------------------------------------------

    private static Transform PreserveGameplayObjects()
    {
        Transform holder = new GameObject("__Preserved").transform;
        foreach (string name in new[] { "TSpawn", "CTSpawn", "BombSite_A", "BombSite_B" })
        {
            GameObject zone = GameObject.Find(name);
            if (zone != null)
            {
                zone.transform.SetParent(holder, true);
            }
        }
        return holder;
    }

    private static void RemoveOldGeometry()
    {
        foreach (string name in new[] { "Map_DustLike", RootName, "Wall3D_1", "Wall3D_2", "Wall3D_3" })
        {
            GameObject old;
            while ((old = GameObject.Find(name)) != null)
            {
                Object.DestroyImmediate(old);
            }
        }
    }

    // --- Terrain ------------------------------------------------------------

    private static Material CreateTerrainMaterial()
    {
        string path = "Assets/Materials/Terrain_Mat.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("CsAutobattler/TerrainLit"));
            AssetDatabase.CreateAsset(material, path);
        }
        material.shader = Shader.Find("CsAutobattler/TerrainLit");
        SetTextureSet(material, "_Grass", "aerial_grass_rock");
        SetTextureSet(material, "_Dirt", "brown_mud_dry");
        material.SetTexture("_RockMap", LoadTexture("rock_face_03_diff_1k"));
        material.SetTexture("_RockNormal", LoadTexture("rock_face_03_nor_gl_1k"));
        material.SetColor("_GrassTint", new Color(0.95f, 1.02f, 0.86f));
        material.SetColor("_DirtTint", new Color(1.05f, 0.95f, 0.82f));
        material.SetColor("_RockTint", new Color(0.92f, 0.86f, 0.78f));
        material.SetFloat("_GrassTile", 7f);
        material.SetFloat("_DirtTile", 3.5f);
        material.SetFloat("_RockTile", 4f);
        material.SetFloat("_Smoothness", 0.4f);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static void SetTextureSet(Material material, string prefix, string name)
    {
        material.SetTexture(prefix + "Map", LoadTexture(name + "_diff_1k"));
        material.SetTexture(prefix + "Normal", LoadTexture(name + "_nor_gl_1k"));
        material.SetTexture(prefix + "ARM", LoadTexture(name + "_arm_1k"));
    }

    private static Texture2D LoadTexture(string name)
    {
        string path = $"Assets/Art/PolyHaven/{name}.jpg";
        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
        if (importer != null)
        {
            bool normal = name.Contains("_nor_gl_");
            bool data = name.Contains("_arm_");
            if (importer.textureType != (normal ? TextureImporterType.NormalMap : TextureImporterType.Default) ||
                importer.sRGBTexture != (!normal && !data) || importer.anisoLevel != 8)
            {
                importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                importer.sRGBTexture = !normal && !data;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.anisoLevel = 8;
                importer.SaveAndReimport();
            }
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    private static void BuildTerrain(Material material)
    {
        Mesh mesh = BuildHeightMesh(
            "OpenFrontTerrain",
            -OpenFrontLayout.HalfX, OpenFrontLayout.HalfX,
            -OpenFrontLayout.HalfZ, OpenFrontLayout.HalfZ,
            TerrainResolution, false);
        mesh = SaveMesh(mesh, "OpenFrontTerrain");

        // Reuse Floor3D: the camera clamp and minimap reference its renderer.
        GameObject floor = GameObject.Find("Floor3D") ?? new GameObject("Floor3D");
        floor.transform.SetParent(null);
        floor.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        floor.transform.localScale = Vector3.one;
        floor.layer = WalkableLayer;
        foreach (Collider collider in floor.GetComponents<Collider>())
        {
            Object.DestroyImmediate(collider);
        }
        MeshFilter filter = floor.GetComponent<MeshFilter>() ?? floor.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;
        MeshRenderer renderer = floor.GetComponent<MeshRenderer>() ?? floor.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.On;
        renderer.receiveShadows = true;
        floor.AddComponent<MeshCollider>().sharedMesh = mesh;
    }

    private static void BuildSkirt(Material material)
    {
        Mesh mesh = BuildHeightMesh("OpenFrontSkirt", -102f, 102f, -82f, 82f, 1.5f, true);
        mesh = SaveMesh(mesh, "OpenFrontSkirt");
        GameObject skirt = new GameObject("TerrainSkirt");
        skirt.transform.SetParent(root, false);
        skirt.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer renderer = skirt.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;

        // Tree line on the hills outside the playable area.
        System.Random random = new System.Random(17);
        for (int i = 0; i < 70; i++)
        {
            float x = (float)(random.NextDouble() * 120 - 60);
            float z = (float)(random.NextDouble() * 92 - 46);
            if (Mathf.Abs(x) < OpenFrontLayout.HalfX + 1.5f && Mathf.Abs(z) < OpenFrontLayout.HalfZ + 1.5f)
            {
                continue;
            }
            PlaceTree(random.Next(0, 2) == 0 ? "Trees" : "DeadTrees", random.Next(0, 5),
                x, z, (float)random.NextDouble() * 360f, 0.9f + (float)random.NextDouble() * 0.5f, false);
        }
    }

    private static Mesh BuildHeightMesh(string name, float minX, float maxX, float minZ, float maxZ, float step, bool skirt)
    {
        int countX = Mathf.RoundToInt((maxX - minX) / step) + 1;
        int countZ = Mathf.RoundToInt((maxZ - minZ) / step) + 1;
        Vector3[] vertices = new Vector3[countX * countZ];
        Color[] colors = new Color[vertices.Length];
        for (int iz = 0; iz < countZ; iz++)
        {
            for (int ix = 0; ix < countX; ix++)
            {
                float x = minX + ix * step;
                float z = minZ + iz * step;
                float y = OpenFrontLayout.Height(x, z);
                // The skirt runs under the terrain's gentle edge ring slightly
                // lowered (no seam, no z-fighting) and drops well below the
                // interior so cliffs never poke through.
                bool interior = Mathf.Abs(x) < OpenFrontLayout.HalfX - 2f &&
                                Mathf.Abs(z) < OpenFrontLayout.HalfZ - 2f;
                // Exact heights on the terrain's last row so the edge is seamless.
                bool underTerrain = Mathf.Abs(x) < OpenFrontLayout.HalfX - 0.75f &&
                                    Mathf.Abs(z) < OpenFrontLayout.HalfZ - 0.75f;
                float drop = !skirt ? 0f : interior ? 4f : underTerrain ? 0.12f : 0f;
                vertices[iz * countX + ix] = new Vector3(x, y - drop, z);
                colors[iz * countX + ix] = new Color(OpenFrontLayout.Dirt(x, z), 0f, 0f, 1f);
            }
        }

        List<int> triangles = new List<int>((countX - 1) * (countZ - 1) * 6);
        for (int iz = 0; iz < countZ - 1; iz++)
        {
            for (int ix = 0; ix < countX - 1; ix++)
            {
                int a = iz * countX + ix;
                int b = a + 1;
                int c = a + countX;
                int d = c + 1;
                triangles.Add(a); triangles.Add(c); triangles.Add(b);
                triangles.Add(b); triangles.Add(c); triangles.Add(d);
            }
        }

        Mesh mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
        mesh.vertices = vertices;
        mesh.colors = colors;
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>
    /// Stores the mesh as a fresh asset (CopySerialized into an existing mesh of
    /// a different size proved unreliable). The builder reassigns every scene
    /// reference afterwards.
    /// </summary>
    private static Mesh SaveMesh(Mesh mesh, string name)
    {
        string path = $"{OutputFolder}/{name}.asset";
        if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null)
        {
            AssetDatabase.DeleteAsset(path);
        }
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    public static float GroundY(float x, float z) => OpenFrontLayout.Height(x, z);

    // --- Sites, spawns, compound -----------------------------------------

    private static void PlaceGameplayZones()
    {
        PlaceZone("BombSite_A", OpenFrontLayout.SiteA, OpenFrontLayout.SiteSize);
        PlaceZone("BombSite_B", OpenFrontLayout.SiteB, OpenFrontLayout.SiteSize);
        PlaceZone("TSpawn", OpenFrontLayout.TSpawn, OpenFrontLayout.SpawnSize);
        PlaceZone("CTSpawn", OpenFrontLayout.CTSpawn, OpenFrontLayout.SpawnSize);
    }

    private static void PlaceZone(string name, Vector2 center, Vector2 size)
    {
        GameObject zone = GameObject.Find(name);
        if (zone == null)
        {
            Debug.LogError("[OpenFront] Missing gameplay zone " + name);
            return;
        }
        zone.transform.SetParent(root, true);
        float top = GroundY(center.x, center.y);
        zone.transform.SetPositionAndRotation(new Vector3(center.x, top + 0.03f, center.y), Quaternion.identity);
        zone.transform.localScale = new Vector3(size.x, 0.05f, size.y);
    }

    private static void BuildCompound()
    {
        Material wall = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Wall_Mat.mat");
        Vector2 c = OpenFrontLayout.CompoundCenter;
        Vector2 h = OpenFrontLayout.CompoundHalf;
        float y = GroundY(c.x, c.y);
        const float height = 2.6f;
        const float thickness = 0.6f;
        // Each side has a doorway; segments are [start, end] along the side.
        AddWallSide("Wall_Compound_North", c, h, y, height, thickness, true, h.y, new[] { (-h.x, -1f), (1f, h.x) }, wall);
        AddWallSide("Wall_Compound_South", c, h, y, height, thickness, true, -h.y, new[] { (-h.x, -2.5f), (0.5f, h.x) }, wall);
        AddWallSide("Wall_Compound_West", c, h, y, height, thickness, false, -h.x, new[] { (-h.y, -1f), (1f, h.y) }, wall);
        AddWallSide("Wall_Compound_East", c, h, y, height, thickness, false, h.x, new[] { (-h.y, -1.5f), (0.5f, h.y) }, wall);

        Place("Crate", c.x + 1.5f, c.y - 1.5f, 10f, 1f, "Cover_CT_Crate_1");
        Place("Crate", c.x + 2.4f, c.y - 1.4f, -6f, 1f, "Cover_CT_Crate_2");
        Place("CardboardBoxes", c.x + 4f, c.y + 2.6f, 90f, 1f, "Cover_CT_Boxes");
        Place("ExplodingBarrel", c.x - 4.6f, c.y + 3.2f, 0f, 1f, "Cover_CT_Barrel");
        Place("Pallet", c.x - 3f, c.y - 2.5f, 20f, 1f, null);
    }

    private static void AddWallSide(
        string name, Vector2 c, Vector2 h, float y, float height, float thickness,
        bool horizontal, float offset, (float start, float end)[] segments, Material material)
    {
        int index = 1;
        foreach ((float start, float end) in segments)
        {
            float length = end - start;
            float middle = (start + end) * 0.5f;
            Vector3 position = horizontal
                ? new Vector3(c.x + middle, y + height * 0.5f, c.y + offset)
                : new Vector3(c.x + offset, y + height * 0.5f, c.y + middle);
            Vector3 scale = horizontal
                ? new Vector3(length + thickness, height, thickness)
                : new Vector3(thickness, height, length + thickness);
            GameObject segment = GameObject.CreatePrimitive(PrimitiveType.Cube);
            segment.name = $"{name}_{index++}";
            segment.layer = ObstacleLayer;
            segment.transform.SetParent(root, false);
            segment.transform.position = position;
            segment.transform.localScale = scale;
            segment.GetComponent<MeshRenderer>().sharedMaterial = material;
        }
    }

    // --- Props ----------------------------------------------------------------

    private static void PlaceProps()
    {
        float a = OpenFrontLayout.PlateauHeight;

        // A: Hilltop Fort - sandbag ring with gaps toward both ramps.
        Place("SackTrench", 21.2f, 13.5f, 90f, 1f, "Cover_A_Sandbag_1");
        Place("SackTrench", 21.2f, 21f, 90f, 1f, "Cover_A_Sandbag_2");
        Place("SackTrench", 27.5f, 11f, 0f, 1f, "Cover_A_Sandbag_3");
        Place("SackTrenchSmall", 31.5f, 11.2f, 0f, 1f, "Cover_A_Sandbag_4");
        Place("BarrierFixed", 33.5f, 20f, 90f, 1f, "Cover_A_Barrier_1");
        Place("Crate", 28f, 19.5f, 15f, 1f, "Cover_A_Crate_1");
        Place("Crate", 28.9f, 19.3f, -10f, 1f, "Cover_A_Crate_2");
        Place("ContainerSmall", 31f, 15f, 0f, 1f, "Cover_A_Container");
        Place("ExplodingBarrel", 24f, 21.5f, 0f, 1f, "Cover_A_Barrel");
        Place("StreetLight", 22.5f, 23f, 200f, 1f, null);
        Place("Rocks", 18f, 10f, 30f, 3f, null);

        // B: Quarry - containers, crates and debris on the pit floor.
        Place("ShippingContainer", -30.5f, 19.5f, 90f, 1f, "Cover_B_Container_1");
        Place("ShippingContainer", -21f, 12.5f, 0f, 1f, "Cover_B_Container_2");
        Place("Crate", -26f, 20.5f, 10f, 1f, "Cover_B_Crate_1");
        Place("Crate", -25.1f, 20.3f, -15f, 1f, "Cover_B_Crate_2");
        Place("Tires", -28.5f, 12.5f, 40f, 1f, "Cover_B_Tires");
        Place("Pipes", -20.5f, 20.5f, 0f, 1f, "Cover_B_Pipes");
        Place("CardboardBoxes", -32.5f, 13f, 0f, 1f, "Cover_B_Boxes");
        Place("ExplodingBarrel", -22.5f, 22f, 0f, 1f, "Cover_B_Barrel");
        Place("TrashContainerOpen", -33.5f, 22f, 90f, 0.8f, "Cover_B_Dumpster");
        Place("Pallet", -23.5f, 17f, 30f, 1f, null);
        Place("RockLarge", -37f, 9f, 20f, 0.3f, null);
        Place("RockLarge", -18f, 26f, 120f, 0.28f, null);
        Place("RockLarge", -35f, 25f, 260f, 0.25f, null);

        // Mid: Overlook hill and the trench line.
        Place("SackTrenchSmall", -2f, 2.4f, 0f, 1f, "Cover_Mid_Sandbag_1");
        Place("SackTrenchSmall", -0.2f, 4.2f, 90f, 1f, "Cover_Mid_Sandbag_2");
        PlaceTree("DeadTrees", 1, -4.5f, 3.5f, 30f, 1.1f, true);
        PlaceTree("DeadTrees", 3, 0.5f, -1.5f, 200f, 1f, true);
        Place("Rocks", -5f, -0.5f, 0f, 3f, null);
        Place("SackTrenchSmall", -12f, -3.2f, 20f, 1f, "Cover_Trench_W1");
        Place("SackTrenchSmall", -6.5f, -6.2f, -30f, 1f, "Cover_Trench_W2");
        Place("SackTrenchSmall", 6.5f, -6.2f, 30f, 1f, "Cover_Trench_E1");
        Place("SackTrenchSmall", 12f, -3.2f, -20f, 1f, "Cover_Trench_E2");

        // Woods on both flanks.
        float[,] westWoods = { { -16f, -1f }, { -19f, 2.5f }, { -21.5f, -2f }, { -15f, 4f }, { -23f, 4.5f }, { -18f, -4.5f } };
        float[,] eastWoods = { { 15.5f, -4f }, { 18.5f, 0.5f }, { 21f, -3.5f }, { 16f, 3f }, { 20.5f, 4f }, { 13.5f, 1f } };
        for (int i = 0; i < westWoods.GetLength(0); i++)
        {
            PlaceTree("Trees", i % 5, westWoods[i, 0], westWoods[i, 1], i * 57f, 1f + (i % 3) * 0.12f, true);
            PlaceTree("Trees", (i + 2) % 5, eastWoods[i, 0], eastWoods[i, 1], i * 83f, 1f + (i % 2) * 0.15f, true);
        }

        // T approach: wrecked armour and roadblocks.
        Place("Tank", -16f, -15f, 35f, 0.42f, "Cover_T_TankWreck");
        Place("Tires", -11.5f, -17.5f, 0f, 1f, "Cover_T_Tires");
        Place("DebrisPile", -13f, -12f, 60f, 1.2f, null);
        Place("BarrierLarge", 8f, -15.5f, 10f, 1f, "Cover_T_Barrier_1");
        Place("BarrierSingle", 3f, -13f, -15f, 1f, "Cover_T_Barrier_2");
        Place("FenceLong", 15f, -20f, 0f, 1f, null);
        Place("FenceLong", 17.6f, -20f, 0f, 1f, null);
        Place("MetalFence", -25f, -14f, 75f, 1f, "Wall_T_MetalFence");
        Place("ContainerSmall", 12f, -11.5f, 25f, 1f, "Cover_T_Container");
        Place("Crate", 26f, -8f, 0f, 1f, "Cover_T_Crate_1");
        Place("ShippingContainer", -28f, -8f, 80f, 1f, "Cover_T_Container_2");

        // CT approach: fences and a checkpoint.
        Place("Fence", -6f, 25.5f, 0f, 1f, null);
        Place("Fence", 21f, 25.5f, 0f, 1f, null);
        Place("BarrierFixed", -6f, 15f, 70f, 1f, "Cover_CT_Barrier_1");
        Place("SackTrench", 13f, 22f, 90f, 1f, "Cover_CT_Sandbag_1");
        Place("StreetLight", 0f, 21.5f, 160f, 1f, null);
        Place("StreetLight", 14f, 15f, 250f, 1f, null);
        Place("Rocks", 10f, 5f, 0f, 3f, null);
        Place("Rocks", -9f, 10f, 0f, 3f, null);
    }

    private static GameObject Place(string prefabName, float x, float z, float yaw, float scale, string coverName)
    {
        GameObject prefab = Resources.Load<GameObject>("Props/" + prefabName);
        if (prefab == null)
        {
            Debug.LogWarning("[OpenFront] Missing prop " + prefabName);
            return null;
        }
        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        instance.name = coverName ?? $"Prop_{prefabName}_{propIndex}";
        propIndex++;
        instance.transform.SetParent(root, false);
        instance.transform.localScale = Vector3.one * scale;
        instance.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        instance.transform.position = new Vector3(x, LowestGround(instance, x, z), z);
        if (coverName != null)
        {
            AddBoxCollider(instance);
        }
        return instance;
    }

    /// <summary>Sit props on the lowest ground under their footprint so they never float.</summary>
    private static float LowestGround(GameObject instance, float x, float z)
    {
        Bounds bounds = RendererBounds(instance);
        Vector3 offset = bounds.center - instance.transform.position;
        float y = GroundY(x, z);
        for (int i = -1; i <= 1; i += 2)
        {
            for (int j = -1; j <= 1; j += 2)
            {
                y = Mathf.Min(y, GroundY(x + offset.x + bounds.extents.x * i * 0.8f, z + offset.z + bounds.extents.z * j * 0.8f));
            }
        }
        return y - 0.03f;
    }

    private static void PlaceTree(string pack, int index, float x, float z, float yaw, float scale, bool cover)
    {
        GameObject prefab = Resources.Load<GameObject>("Props/" + pack);
        if (prefab == null)
        {
            return;
        }

        // Each pack holds five trees side by side; keep one and recentre it.
        GameObject instance = Object.Instantiate(prefab);
        Renderer[] trees = instance.GetComponentsInChildren<Renderer>();
        System.Array.Sort(trees, (left, right) => left.bounds.center.x.CompareTo(right.bounds.center.x));
        Renderer keep = trees[Mathf.Clamp(index, 0, trees.Length - 1)];
        Transform tree = keep.transform;
        tree.SetParent(null, true);
        Object.DestroyImmediate(instance);

        GameObject holder = new GameObject(cover ? $"Cover_Tree_{propIndex}" : $"Prop_Tree_{propIndex}");
        propIndex++;
        holder.transform.SetParent(root, false);
        Bounds bounds = keep.bounds;
        Vector3 basePoint = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        tree.SetParent(holder.transform, true);
        tree.position -= basePoint;
        holder.transform.localScale = Vector3.one * scale;
        holder.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        holder.transform.position = new Vector3(x, GroundY(x, z) - 0.05f, z);
        if (cover)
        {
            holder.layer = ObstacleLayer;
            CapsuleCollider trunk = holder.AddComponent<CapsuleCollider>();
            trunk.radius = 0.35f;
            trunk.height = 3f;
            trunk.center = new Vector3(0f, 1.5f, 0f);
        }
    }

    private static void AddBoxCollider(GameObject instance)
    {
        foreach (Collider existing in instance.GetComponentsInChildren<Collider>())
        {
            Object.DestroyImmediate(existing);
        }
        BoxCollider box = instance.AddComponent<BoxCollider>();
        // Fit in local space using the rotated prefab's unrotated bounds.
        Quaternion rotation = instance.transform.rotation;
        instance.transform.rotation = Quaternion.identity;
        Bounds unrotated = RendererBounds(instance);
        instance.transform.rotation = rotation;
        Vector3 lossy = instance.transform.lossyScale;
        box.center = new Vector3(
            (unrotated.center.x - instance.transform.position.x) / lossy.x,
            (unrotated.center.y - instance.transform.position.y) / lossy.y,
            (unrotated.center.z - instance.transform.position.z) / lossy.z);
        box.size = new Vector3(unrotated.size.x / lossy.x, unrotated.size.y / lossy.y, unrotated.size.z / lossy.z);
        foreach (Transform child in instance.GetComponentsInChildren<Transform>(true))
        {
            child.gameObject.layer = ObstacleLayer;
        }
    }

    private static Bounds RendererBounds(GameObject instance)
    {
        Renderer[] renderers = instance.GetComponentsInChildren<Renderer>();
        Bounds bounds = new Bounds(instance.transform.position, Vector3.zero);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (i == 0) bounds = renderers[i].bounds; else bounds.Encapsulate(renderers[i].bounds);
        }
        return bounds;
    }

    // --- AI markers, boundary, navigation --------------------------------

    private static void PlaceRouteMarkers()
    {
        AddMarker("ChokePoint_A_SouthRamp", OpenFrontLayout.PlateauRamps[0].Bottom);
        AddMarker("ChokePoint_A_WestRamp", OpenFrontLayout.PlateauRamps[1].Bottom);
        AddMarker("ChokePoint_B_SouthRamp", OpenFrontLayout.PitRamps[0].Bottom);
        AddMarker("ChokePoint_B_EastRamp", OpenFrontLayout.PitRamps[1].Bottom);
        AddMarker("ChokePoint_Mid_TrenchGap", new Vector2(0f, -7.5f));
        AddMarker("RoutePoint_Mid_Overlook", OpenFrontLayout.HillCenter);
        AddMarker("RoutePoint_West_Woods", new Vector2(-18f, 0f));
        AddMarker("RoutePoint_East_Woods", new Vector2(18f, 0f));
        AddMarker("RoutePoint_CT_Compound", OpenFrontLayout.CompoundCenter);
    }

    private static void AddMarker(string name, Vector2 position)
    {
        GameObject marker = new GameObject(name);
        marker.transform.SetParent(root, false);
        marker.transform.position = new Vector3(position.x, GroundY(position.x, position.y), position.y);
    }

    private static void BuildBoundary()
    {
        float x = OpenFrontLayout.PlayHalfX;
        float z = OpenFrontLayout.PlayHalfZ;
        AddInvisibleWall("Wall_Boundary_North", new Vector3(0f, 3f, z + 0.5f), new Vector3(x * 2f + 2f, 8f, 1f));
        AddInvisibleWall("Wall_Boundary_South", new Vector3(0f, 3f, -z - 0.5f), new Vector3(x * 2f + 2f, 8f, 1f));
        AddInvisibleWall("Wall_Boundary_East", new Vector3(x + 0.5f, 3f, 0f), new Vector3(1f, 8f, z * 2f + 2f));
        AddInvisibleWall("Wall_Boundary_West", new Vector3(-x - 0.5f, 3f, 0f), new Vector3(1f, 8f, z * 2f + 2f));
    }

    private static void AddInvisibleWall(string name, Vector3 position, Vector3 size)
    {
        GameObject wall = new GameObject(name) { layer = ObstacleLayer };
        wall.transform.SetParent(root, false);
        wall.transform.position = position;
        wall.AddComponent<BoxCollider>().size = size;
    }

    private static void ConfigureNavigation()
    {
        LayerMask walkable = 1 << WalkableLayer;
        foreach (AStarPathfinder3D grid in Object.FindObjectsByType<AStarPathfinder3D>(FindObjectsInactive.Include))
        {
            grid.walkableSurfaceMask = walkable;
            grid.surfaceProbeHeight = 12f;
            grid.maximumNeighborHeightDelta = 0.75f;
            EditorUtility.SetDirty(grid);
        }

        foreach (AgentMotor motor in Object.FindObjectsByType<AgentMotor>(FindObjectsInactive.Include))
        {
            SetSurfaceMask(motor, walkable);
        }
        foreach (string prefabPath in new[] { "Assets/Prefabs/RedAgent3D_1.prefab", "Assets/Prefabs/BlueAgent3D_1.prefab" })
        {
            GameObject prefab = PrefabUtility.LoadPrefabContents(prefabPath);
            foreach (AgentMotor motor in prefab.GetComponentsInChildren<AgentMotor>(true))
            {
                SetSurfaceMask(motor, walkable);
            }
            PrefabUtility.SaveAsPrefabAsset(prefab, prefabPath);
            PrefabUtility.UnloadPrefabContents(prefab);
        }
    }

    private static void SetSurfaceMask(AgentMotor motor, LayerMask mask)
    {
        SerializedObject serialized = new SerializedObject(motor);
        serialized.FindProperty("walkableSurfaceMask").intValue = mask.value;
        serialized.FindProperty("surfaceProbeHeight").floatValue = 6f;
        serialized.FindProperty("surfaceProbeDistance").floatValue = 14f;
        serialized.FindProperty("maximumSurfaceStep").floatValue = 0.6f;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SnapAgents()
    {
        foreach (AgentStats agent in Object.FindObjectsByType<AgentStats>(FindObjectsInactive.Include))
        {
            Vector3 position = agent.transform.position;
            if (agent.team == TeamType.Blue && position.z > OpenFrontLayout.CTSpawn.y + 3f)
            {
                position.z = OpenFrontLayout.CTSpawn.y + Mathf.Repeat(position.x, 2f) - 1f;
            }
            position.y = GroundY(position.x, position.z) + 1f;
            agent.transform.position = position;
            EditorUtility.SetDirty(agent.transform);
        }
    }
}
