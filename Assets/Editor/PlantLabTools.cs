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
        // Start with the current combat encounter so the detailed rider, fly,
        // and opponents remain exactly as they are in the playable scene.
        var scene = EditorSceneManager.OpenScene("Assets/CombatEncounter.unity");
        var group = new GameObject("Generated indoor plants");
        CreatePreset(group.transform, "Amber spiral", FindClearPosition(4.8f,
                new Vector3(-17, 0, -10), new Vector3(-20, 0, -18), new Vector3(-23, 0, 4)), 17,
            5, ProceduralPlant.LeafPattern.Spiral, 6, 1.05f, 4.8f, 3.6f,
            .35f, .26f, new Color(.18f, .39f, .26f), new Color(.31f, .52f, .30f),
            new Color(.95f, .77f, .52f), new Color(.78f, .18f, .20f));
        CreatePreset(group.transform, "Silver opposite", FindClearPosition(4.3f,
                new Vector3(18, 0, -11), new Vector3(22, 0, -18), new Vector3(23, 0, 4)), 39,
            6, ProceduralPlant.LeafPattern.Opposite, 5, 1.12f, 4.3f, 3.9f,
            .53f, .12f, new Color(.22f, .40f, .38f), new Color(.39f, .57f, .49f),
            new Color(.73f, .66f, .91f), new Color(.31f, .27f, .66f));
        CreatePreset(group.transform, "Copper whorl", FindClearPosition(3.8f,
                new Vector3(22, 0, 21), new Vector3(-21, 0, 21), new Vector3(24, 0, 3)), 83,
            7, ProceduralPlant.LeafPattern.Whorled, 5, .96f, 3.8f, 3.2f,
            .41f, .07f, new Color(.37f, .34f, .23f), new Color(.57f, .47f, .30f),
            new Color(.94f, .50f, .38f), new Color(.87f, .38f, .12f));
        CreatePreset(group.transform, "Hanging jade vines", FindHangingPosition(2.8f,
                new Vector3(-25, 0, 24), new Vector3(-24, 0, -25),
                new Vector3(25, 0, -24), new Vector3(25, 0, 24)), 109,
            5, ProceduralPlant.LeafPattern.Spiral, 4, 1.55f, 2.8f, 2.48f,
            .42f, 0, new Color(.16f, .47f, .38f), new Color(.33f, .65f, .56f),
            new Color(.71f, .88f, .73f), new Color(.42f, .73f, .63f), true);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        Debug.Log("PLANT_LAB_READY: combat models preserved; three floor plants and one corner hanging plant clear of room obstacles");
        ValidateLab();
    }

    static void CreatePreset(Transform parent, string name, Vector3 position, int seed,
        int faces, ProceduralPlant.LeafPattern pattern, int nodes, float nodeLength,
        float leafLength, float leafWidth, float widest, float branchChance,
        Color upper, Color lower, Color petalColor, Color fruitColor, bool hanging = false)
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
        plant.hangingPlant = hanging;
        if (hanging)
        {
            plant.stemRadius = .17f;
            plant.stemBend = .28f;
            plant.potRadius = 1.4f;
            plant.potHeight = 1.65f;
            plant.leafRiseDegrees = 8f;
        }
        plant.stemMaterial = Material("Stem", new Color(.24f, .32f, .19f));
        plant.leafUpperMaterial = Material(name + " upper", upper);
        plant.leafLowerMaterial = Material(name + " lower", lower);
        plant.veinMaterial = Material("Leaf veins", new Color(.54f, .63f, .37f));
        plant.potMaterial = Material("Faceted pot", new Color(.28f, .36f, .44f));
        plant.potRimMaterial = Material("Pot rim", new Color(.58f, .49f, .40f));
        plant.soilMaterial = Material("Soil", new Color(.20f, .16f, .13f));
        if (!hanging)
        {
            plant.budMaterial = Material("Flower buds and calyx", new Color(.32f, .53f, .22f));
            plant.petalMaterial = Material(name + " petals", petalColor);
            plant.flowerCenterMaterial = Material("Flower pollen", new Color(.98f, .72f, .19f));
            plant.youngFruitMaterial = Material("Young fruit", new Color(.48f, .68f, .27f));
            plant.ripeFruitMaterial = Material(name + " ripe fruit", fruitColor);
        }
        Regenerate(plant);
        SavePreset(plant);
    }

    static Vector3 FindClearPosition(float leafLength, params Vector3[] candidates)
    {
        float radius = leafLength + 2f;
        foreach (var candidate in candidates)
            if (ClearOfSceneObjects(candidate, radius)) return candidate;
        throw new InvalidOperationException("No collision-free position for a plant of radius " + radius);
    }

    static Vector3 FindHangingPosition(float leafLength, params Vector3[] candidates)
    {
        Vector3 position = FindClearPosition(leafLength + 1f, candidates);
        var ceiling = GameObject.Find("Ceiling");
        var collider = ceiling ? ceiling.GetComponent<Collider>() : null;
        if (!collider) throw new InvalidOperationException("Hanging plant needs a ceiling collider.");
        position.y = collider.bounds.min.y - 1.65f - 3.8f;
        return position;
    }

    static bool ClearOfSceneObjects(Vector3 center, float radius, Transform ignore = null)
    {
        if (Mathf.Abs(center.x) + radius > 33 || Mathf.Abs(center.z) + radius > 33)
            return false;
        foreach (var collider in UnityEngine.Object.FindObjectsOfType<Collider>())
        {
            if (!collider.enabled || collider.isTrigger ||
                (ignore && collider.transform.IsChildOf(ignore))) continue;
            if (collider.GetComponentInParent<ProceduralPlant>() && ignore) continue;
            string name = collider.gameObject.name;
            if (name == "Floor" || name == "Ceiling" || name.EndsWith(" wall")) continue;
            Bounds bounds = collider.bounds;
            float dx = Mathf.Max(bounds.min.x - center.x, 0, center.x - bounds.max.x);
            float dz = Mathf.Max(bounds.min.z - center.z, 0, center.z - bounds.max.z);
            if (dx * dx + dz * dz < (radius + .5f) * (radius + .5f)) return false;
        }
        return true;
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
        int opponents = UnityEngine.Object.FindObjectsOfType<CombatOpponent>().Length;
        bool combatPreserved = UnityEngine.Object.FindObjectOfType<RiderCombat>() && opponents >= 2;
        int hangingCount = 0;
        foreach (var plant in plants) if (plant.hangingPlant) hangingCount++;
        bool passed = plants.Length == 4 && hangingCount == 1 && fly &&
            footprint != null && combatPreserved;
        string detail = "plants=" + plants.Length + ", combatPreserved=" + combatPreserved +
            ", opponents=" + opponents + ", hanging=" + hangingCount;
        Physics.SyncTransforms();
        foreach (var plant in plants)
        {
            var stems = plant.GetComponentsInChildren<FlyGripSurface>(true);
            MeshCollider leaf = null, stem = null;
            int leafCount = 0;
            float lowestGripSurface = float.PositiveInfinity;
            foreach (var surface in stems)
            {
                var collider = surface.GetComponent<MeshCollider>();
                if (!collider) continue;
                lowestGripSurface = Mathf.Min(lowestGripSurface, collider.bounds.min.y);
                if (surface.allowFacetWrap && !stem) stem = collider;
                if (!surface.allowFacetWrap) { leafCount++; if (!leaf) leaf = collider; }
            }
            bool geometry = plant.stemFaces >= 4 && plant.stemFaces <= 7 &&
                plant.leafWidth > 1.5f * RiderCombat.FlyAssemblyScale &&
                stem && leaf && leafCount >= plant.internodes;
            bool pot = plant.transform.Find("Generated plant/Removable rotated-facet pot") != null;
            var growth = plant.transform.Find("Generated plant/Growth");
            var mainStem = growth ? growth.Find("Main faceted stem") : null;
            bool floralStages = mainStem && mainStem.Find("Bud stalk") &&
                mainStem.Find("Open flower stalk") && mainStem.Find("Young fruit stalk") &&
                mainStem.Find("Ripe fruit stalk");
            var sampledStem = plant.hangingPlant && growth ?
                growth.Find("Trailing vine 1/Branch faceted stem") : mainStem;
            float largestLeaf = 0, smallestLeaf = float.PositiveInfinity;
            if (sampledStem)
                foreach (Transform child in sampledStem)
                {
                    var blade = child.Find("Closed leaf blade");
                    if (!blade) continue;
                    float length = blade.GetComponent<MeshFilter>().sharedMesh.bounds.size.z;
                    largestLeaf = Mathf.Max(largestLeaf, length);
                    smallestLeaf = Mathf.Min(smallestLeaf, length);
                }
            bool smallerTips = largestLeaf > 0 && smallestLeaf < largestLeaf * .8f;
            int trailingVines = 0;
            if (growth)
                foreach (Transform child in growth)
                    if (child.name.StartsWith("Trailing vine ")) trailingVines++;
            var ceiling = GameObject.Find("Ceiling");
            var ceilingCollider = ceiling ? ceiling.GetComponent<Collider>() : null;
            bool hangingShape = !plant.hangingPlant ||
                (trailingVines == plant.vineCount &&
                 plant.transform.Find("Generated plant/Ceiling hanger/Ceiling hook") &&
                 ceilingCollider &&
                 lowestGripSurface < plant.transform.position.y - 2f &&
                 Mathf.Abs(plant.transform.position.y + plant.potHeight +
                     plant.hangerLength - ceilingCollider.bounds.min.y) < .2f &&
                 Mathf.Abs(plant.transform.position.x) >= 20f &&
                 Mathf.Abs(plant.transform.position.z) >= 20f);
            bool placementClear = ClearOfSceneObjects(plant.transform.position,
                plant.leafLength + (plant.hangingPlant ? 3f : 2f), plant.transform);
            foreach (var other in plants)
            {
                if (other == plant) continue;
                float separation = Vector2.Distance(
                    new Vector2(plant.transform.position.x, plant.transform.position.z),
                    new Vector2(other.transform.position.x, other.transform.position.z));
                placementClear &= separation >= plant.leafLength + other.leafLength + 4f;
            }
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
            passed &= geometry && pot && (plant.hangingPlant || floralStages) &&
                smallerTips && hangingShape && placementClear &&
                leafContact && stemContact && savedMeshes && topGrip && underGrip && stemGrip;
            detail += " | " + plant.name + ": leaves=" + leafCount + ", width=" +
                plant.leafWidth.ToString("F2") + ", faces=" + plant.stemFaces +
                ", bothLeafFaces=" + leafContact + ", stemSide=" + stemContact +
                ", flyGrip=" + topGrip + "/" + underGrip + "/" + stemGrip +
                ", flowers=" + floralStages + ", smallTips=" + smallerTips +
                ", hangingShape=" + hangingShape + ", vines=" + trailingVines +
                ", placementClear=" + placementClear + ", pot=" + pot +
                ", savedMeshes=" + savedMeshes;
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
