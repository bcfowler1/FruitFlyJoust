using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using FruitFlyJoust;

[CustomEditor(typeof(ProceduralPlant))]
public sealed class ProceduralPlantEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.Space();
        if (GUILayout.Button("Regenerate and save plant meshes"))
        {
            var plant = (ProceduralPlant)target;
            PlantLabTools.Regenerate(plant);
            PlantLabTools.SavePreset(plant);
        }
        EditorGUILayout.HelpBox("Turn off Include Pot for ground planting. The growth geometry and contact surfaces are independent of the pot.", MessageType.Info);
    }
}

public static class PlantLabTools
{
    const string GeneratedFolder = "Assets/Generated/Plants";
    const string ScenePath = "Assets/PlantLab.unity";

    [MenuItem("Fruit Fly/Plants/Build Indoor Plant Lab")]
    public static void BuildIndoorLab()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play before building PlantLab.");
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        EnsureFolder();
        if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) &&
            !AssetDatabase.CopyAsset("Assets/Practice.unity", ScenePath))
            throw new IOException("Could not copy the practice room to PlantLab.");
        var scene = EditorSceneManager.OpenScene(ScenePath);
        foreach (var oldPlant in UnityEngine.Object.FindObjectsOfType<ProceduralPlant>())
            UnityEngine.Object.DestroyImmediate(oldPlant.gameObject);

        var group = GameObject.Find("Generated indoor plants");
        if (group) UnityEngine.Object.DestroyImmediate(group);
        group = new GameObject("Generated indoor plants");
        CreatePreset(group.transform, "Amber spiral", new Vector3(-10, 0, -7), 17,
            5, ProceduralPlant.LeafPattern.Spiral, 6, 1.05f, 4.8f, 3.6f,
            .35f, .26f, new Color(.18f, .39f, .26f), new Color(.31f, .52f, .30f));
        CreatePreset(group.transform, "Silver opposite", new Vector3(9, 0, -6), 39,
            6, ProceduralPlant.LeafPattern.Opposite, 5, 1.12f, 4.3f, 3.9f,
            .53f, .12f, new Color(.22f, .40f, .38f), new Color(.39f, .57f, .49f));
        CreatePreset(group.transform, "Copper whorl", new Vector3(10, 0, 12), 83,
            7, ProceduralPlant.LeafPattern.Whorled, 5, .96f, 3.8f, 3.2f,
            .41f, .07f, new Color(.37f, .34f, .23f), new Color(.57f, .47f, .30f));
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        Debug.Log("PLANT_LAB_READY: three editable plant prefabs, broad closed leaves, faceted stems, removable indoor pots");
        ValidateLab();
    }

    static void CreatePreset(Transform parent, string name, Vector3 position, int seed,
        int faces, ProceduralPlant.LeafPattern pattern, int nodes, float nodeLength,
        float leafLength, float leafWidth, float widest, float branchChance,
        Color upper, Color lower)
    {
        var root = new GameObject(name);
        root.transform.SetParent(parent, false);
        root.transform.localPosition = position;
        var plant = root.AddComponent<ProceduralPlant>();
        plant.seed = seed;
        plant.stemFaces = faces;
        plant.leafPattern = pattern;
        plant.internodes = nodes;
        plant.internodeLength = nodeLength;
        plant.leafLength = leafLength;
        plant.leafWidth = leafWidth;
        plant.widestPoint = widest;
        plant.branchChance = branchChance;
        plant.stemMaterial = Material("Stem", new Color(.24f, .32f, .19f));
        plant.leafUpperMaterial = Material(name + " upper", upper);
        plant.leafLowerMaterial = Material(name + " lower", lower);
        plant.veinMaterial = Material("Leaf veins", new Color(.54f, .63f, .37f));
        plant.potMaterial = Material("Faceted pot", new Color(.28f, .36f, .44f));
        plant.potRimMaterial = Material("Pot rim", new Color(.58f, .49f, .40f));
        plant.soilMaterial = Material("Soil", new Color(.20f, .16f, .13f));
        Regenerate(plant);
        SavePreset(plant);
    }

    public static void SavePreset(ProceduralPlant plant)
    {
        EnsureFolder();
        PrefabUtility.SaveAsPrefabAsset(plant.gameObject, GeneratedFolder + "/" + plant.name + ".prefab");
    }

    static void EnsureFolder()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Generated")) AssetDatabase.CreateFolder("Assets", "Generated");
        if (!AssetDatabase.IsValidFolder(GeneratedFolder)) AssetDatabase.CreateFolder("Assets/Generated", "Plants");
    }

    static Material Material(string name, Color color)
    {
        EnsureFolder();
        string path = GeneratedFolder + "/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!material)
        {
            material = new Material(Shader.Find("Standard"));
            AssetDatabase.CreateAsset(material, path);
        }
        material.color = color;
        EditorUtility.SetDirty(material);
        return material;
    }

    public static void Regenerate(ProceduralPlant plant)
    {
        EnsureFolder();
        plant.Rebuild();
        string safeName = plant.name.Replace("/", "_").Replace("\\", "_");
        string path = GeneratedFolder + "/" + safeName + " meshes.asset";
        bool exists = AssetDatabase.LoadMainAssetAtPath(path);
        var oldMeshes = new System.Collections.Generic.Dictionary<string, Mesh>();
        if (exists)
            foreach (var oldMesh in AssetDatabase.LoadAllAssetsAtPath(path))
                if (oldMesh is Mesh mesh) oldMeshes[mesh.name] = mesh;
        int index = 0;
        foreach (var filter in plant.GetComponentsInChildren<MeshFilter>(true))
        {
            var mesh = filter.sharedMesh;
            if (!mesh) continue;
            mesh.name = safeName + " " + index++ + " " + filter.gameObject.name;
            if (oldMeshes.TryGetValue(mesh.name, out var savedMesh))
            {
                EditorUtility.CopySerialized(mesh, savedMesh);
                UnityEngine.Object.DestroyImmediate(mesh);
                filter.sharedMesh = savedMesh;
                EditorUtility.SetDirty(savedMesh);
            }
            else if (!exists)
            {
                AssetDatabase.CreateAsset(mesh, path);
                exists = true;
            }
            else AssetDatabase.AddObjectToAsset(mesh, path);
            EditorUtility.SetDirty(filter);
            var collider = filter.GetComponent<MeshCollider>();
            if (collider)
            {
                collider.sharedMesh = filter.sharedMesh;
                EditorUtility.SetDirty(collider);
            }
        }
        if (!exists) throw new InvalidOperationException("Plant generated no mesh.");
        EditorUtility.SetDirty(plant);
        if (plant.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(plant.gameObject.scene);
    }

    [MenuItem("Fruit Fly/Plants/Validate Plant Lab")]
    public static void ValidateLab()
    {
        var plants = UnityEngine.Object.FindObjectsOfType<ProceduralPlant>();
        Array.Sort(plants, (a, b) => string.CompareOrdinal(a.name, b.name));
        var fly = UnityEngine.Object.FindObjectOfType<FlyMotor>();
        var footprint = typeof(FlyMotor).GetMethod("HasFootprint", BindingFlags.Instance | BindingFlags.NonPublic,
            null, new[] { typeof(RaycastHit), typeof(Vector3) }, null);
        bool passed = plants.Length == 3 && fly && footprint != null;
        string detail = "plants=" + plants.Length;
        Physics.SyncTransforms();
        foreach (var plant in plants)
        {
            var stems = plant.GetComponentsInChildren<FlyGripSurface>(true);
            MeshCollider leaf = null, stem = null;
            int leafCount = 0;
            foreach (var surface in stems)
            {
                var collider = surface.GetComponent<MeshCollider>();
                if (!collider) continue;
                if (surface.allowFacetWrap && !stem) stem = collider;
                if (!surface.allowFacetWrap) { leafCount++; if (!leaf) leaf = collider; }
            }
            bool geometry = plant.stemFaces >= 4 && plant.stemFaces <= 7 &&
                plant.leafWidth > 1.5f * RiderCombat.FlyAssemblyScale &&
                stem && leaf && leafCount >= plant.internodes;
            bool pot = plant.transform.Find("Generated plant/Removable rotated-facet pot") != null;
            bool leafContact = ContactFromBothFaces(leaf);
            bool stemContact = StemSideContact(stem);
            bool topGrip = false, underGrip = false, stemGrip = false;
            if (fly && footprint != null)
            {
                foreach (var surface in stems)
                {
                    var candidate = surface.GetComponent<MeshCollider>();
                    if (!candidate) continue;
                    if (surface.allowFacetWrap)
                    {
                        if (TryStemSideHit(candidate, out var sideHit))
                            stemGrip |= AcceptsFootprint(fly, footprint, sideHit);
                    }
                    else if (TryLeafHits(candidate, out var topHit, out var bottomHit))
                    {
                        topGrip |= AcceptsFootprint(fly, footprint, topHit);
                        underGrip |= AcceptsFootprint(fly, footprint, bottomHit);
                    }
                }
            }
            bool savedMeshes = leaf && AssetDatabase.Contains(leaf.sharedMesh) &&
                stem && AssetDatabase.Contains(stem.sharedMesh);
            passed &= geometry && pot && leafContact && stemContact && savedMeshes &&
                topGrip && underGrip && stemGrip;
            detail += " | " + plant.name + ": leaves=" + leafCount + ", width=" +
                plant.leafWidth.ToString("F2") + ", faces=" + plant.stemFaces +
                ", bothLeafFaces=" + leafContact + ", stemSide=" + stemContact +
                ", flyGrip=" + topGrip + "/" + underGrip + "/" + stemGrip +
                ", pot=" + pot + ", savedMeshes=" + savedMeshes;
        }
        var ground = new GameObject("Temporary ground-grown plant").AddComponent<ProceduralPlant>();
        ground.includePot = false;
        ground.Rebuild();
        bool groundReady = ground.BaseHeight == 0 &&
            !ground.transform.Find("Generated plant/Removable rotated-facet pot") &&
            ground.transform.Find("Generated plant/Growth").localPosition == Vector3.zero;
        UnityEngine.Object.DestroyImmediate(ground.gameObject);
        passed &= groundReady;
        detail += " | groundMode=" + groundReady;
        string report = "{\"status\":\"" + (passed ? "passed" : "failed") +
            "\",\"details\":\"" + detail.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"}";
        string reportPath = Path.GetFullPath(Path.Combine(Application.dataPath, "../Research/plant-lab-evaluation.json"));
        File.WriteAllText(reportPath, report);
        if (passed) Debug.Log("PLANT_LAB_CHECK_PASSED " + detail);
        else Debug.LogError("PLANT_LAB_CHECK_FAILED " + detail);
    }

    static bool ContactFromBothFaces(MeshCollider collider)
    {
        return TryLeafHits(collider, out var topHit, out var bottomHit) &&
            Vector3.Dot(topHit.normal, collider.transform.up) > .3f &&
            Vector3.Dot(bottomHit.normal, collider.transform.up) < -.3f;
    }

    static bool TryLeafHits(MeshCollider collider, out RaycastHit topHit, out RaycastHit bottomHit)
    {
        topHit = default(RaycastHit);
        bottomHit = default(RaycastHit);
        if (!collider) return false;
        Vector3 center = collider.transform.TransformPoint(collider.sharedMesh.bounds.center);
        Vector3 up = collider.transform.up;
        return collider.Raycast(new Ray(center + up * 3, -up), out topHit, 6) &&
            collider.Raycast(new Ray(center - up * 3, up), out bottomHit, 6);
    }

    static bool StemSideContact(MeshCollider collider)
    {
        return TryStemSideHit(collider, out var hit) &&
            Vector3.Dot(hit.normal, collider.transform.right) > .2f;
    }

    static bool TryStemSideHit(MeshCollider collider, out RaycastHit hit)
    {
        hit = default(RaycastHit);
        if (!collider) return false;
        Vector3 center = collider.transform.TransformPoint(collider.sharedMesh.bounds.center);
        Vector3 right = collider.transform.right;
        return collider.Raycast(new Ray(center + right * 3, -right), out hit, 6);
    }

    static bool AcceptsFootprint(FlyMotor fly, MethodInfo footprint, RaycastHit hit)
    {
        Vector3 position = hit.point + hit.normal * (.5f * RiderCombat.FlyAssemblyScale + .025f);
        return (bool)footprint.Invoke(fly, new object[] { hit, position });
    }
}
