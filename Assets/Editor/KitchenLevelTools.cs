using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using FruitFlyJoust;

public static class KitchenLevelTools
{
    const string ScenePath = "Assets/KitchenLevel1.unity";
    const string NextScenePath = "Assets/KitchenLevel2.unity";
    const string Folder = "Assets/Generated/Kitchen";
    static Transform root;
    static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
    static System.Random random;

    [MenuItem("Fruit Fly/Levels/Build Kitchen Level 1 (seed 1049)")]
    public static void BuildDefault() { Build(1049); }

    [MenuItem("Fruit Fly/Levels/Verify three kitchen seeds")]
    public static void VerifySeeds()
    {
        var signatures = new HashSet<string>();
        foreach (int seed in new[] { 1049, 2050, 3621 })
        {
            Build(seed);
            var level = UnityEngine.Object.FindObjectOfType<KitchenLevel>();
            signatures.Add(level ? level.layoutSignature : "missing");
        }
        Build(1049);
        bool passed = signatures.Count == 3;
        string detail = "distinctLayouts=" + signatures.Count + " seeds=1049/2050/3621 restored=1049";
        File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath,
            "../Research/kitchen-seed-evaluation.json")), "{\"status\":\"" +
            (passed ? "passed" : "failed") + "\",\"details\":\"" + detail + "\"}");
        if (passed) Debug.Log("KITCHEN_SEEDS_CHECK_PASSED " + detail);
        else Debug.LogError("KITCHEN_SEEDS_CHECK_FAILED " + detail);
    }

    public static void Build(int seed)
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play before rebuilding the kitchen.");
        if (!AssetDatabase.IsValidFolder("Assets/Generated")) AssetDatabase.CreateFolder("Assets", "Generated");
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Generated", "Kitchen");
        materials.Clear(); random = new System.Random(seed);
        var scene = EditorSceneManager.OpenScene("Assets/CombatEncounter.unity");
        foreach (string name in new[] { "Floor", "Ceiling", "North wall", "South wall", "East wall", "West wall",
                     "Orbit pillar", "Landing table", "Practice course", "Enemy swordsman", "Enemy archer",
                     "Hoop 1", "Hoop 2", "Hoop 3" })
        {
            GameObject found;
            while ((found = GameObject.Find(name))) UnityEngine.Object.DestroyImmediate(found);
        }
        foreach (var target in UnityEngine.Object.FindObjectsOfType<CombatTarget>())
            UnityEngine.Object.DestroyImmediate(target.gameObject);
        foreach (var renderer in UnityEngine.Object.FindObjectsOfType<Renderer>())
            if (renderer.gameObject.name == "Ring segment" || renderer.gameObject.name == "Table leg")
                UnityEngine.Object.DestroyImmediate(renderer.gameObject);

        var fly = UnityEngine.Object.FindObjectOfType<FlyMotor>();
        var combat = UnityEngine.Object.FindObjectOfType<RiderCombat>();
        if (!fly || !combat) throw new InvalidOperationException("The combat source scene lost its player fly or rider.");
        fly.transform.SetPositionAndRotation(new Vector3(0, 2.2f, -5), Quaternion.identity);
        var rigidbody = fly.GetComponent<Rigidbody>();
        if (rigidbody) rigidbody.velocity = Vector3.zero;
        combat.course = null;
        combat.spawnStandaloneJouster = false;
        combat.message = "Kitchen Level 1: reach the lock through the keyhole. Water, disposal and stove are dangerous.";
        var camera = UnityEngine.Object.FindObjectOfType<RiderCamera>();
        if (camera)
        {
            camera.transform.position = fly.transform.position + new Vector3(0, 2, -4);
            camera.GetComponent<Camera>().farClipPlane = 125;
        }
        var existingSun = GameObject.Find("Sun");
        if (existingSun) existingSun.transform.rotation = Quaternion.Euler(37, 155, 0);
        RenderSettings.ambientLight = new Color(.44f, .39f, .32f);

        var level = new GameObject("Kitchen Level 1").AddComponent<KitchenLevel>();
        level.seed = seed;
        root = level.transform;
        Room();
        int side = random.Next(2) == 0 ? -1 : 1;
        int stoolOffset = random.Next(3) - 1;
        float refrigeratorZ = -4 + random.Next(-2, 3);
        float trashZ = -9 + random.Next(-1, 2);
        level.layoutSignature = side + ":" + stoolOffset + ":" + refrigeratorZ + ":" + trashZ;
        SinkAndCounter();
        Refrigerator(-side, refrigeratorZ);
        Stove(side);
        Shelves(side);
        Stools(stoolOffset);
        Trash(side, trashZ);
        DoorAndKeyhole();
        CeilingFan();
        Plants();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        Validate();
        string signature = level.layoutSignature;
        BuildNextRoom();
        EditorSceneManager.OpenScene(ScenePath);
        Debug.Log("KITCHEN_LEVEL_READY seed=" + seed + " layout=" + signature);
    }

    static void BuildNextRoom()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath);
        var kitchen = GameObject.Find("Kitchen Level 1");
        if (kitchen) UnityEngine.Object.DestroyImmediate(kitchen);
        root = new GameObject("Level 2 arrival through keyhole").transform;
        root.gameObject.AddComponent<KitchenNextRoom>();
        var oak = Mat("Smoked oak", new Color(.20f, .105f, .056f), .05f, .35f);
        var paper = Mat("Damask wallpaper", new Color(.43f, .30f, .25f));
        Box("Floor", new Vector3(0, -.5f, 0), new Vector3(18, 1, 18), oak);
        Box("Ceiling", new Vector3(0, 19, 0), new Vector3(18, 1, 18), paper);
        Box("North wall", new Vector3(0, 9.5f, 9), new Vector3(18, 19, .7f), paper);
        Box("South wall", new Vector3(0, 9.5f, -9), new Vector3(18, 19, .7f), paper);
        Box("East wall", new Vector3(9, 9.5f, 0), new Vector3(.7f, 19, 18), paper);
        Box("West wall", new Vector3(-9, 9.5f, 0), new Vector3(.7f, 19, 18), paper);
        var fly = UnityEngine.Object.FindObjectOfType<FlyMotor>();
        fly.transform.SetPositionAndRotation(new Vector3(0, 2, -2), Quaternion.identity);
        var camera = UnityEngine.Object.FindObjectOfType<RiderCamera>();
        if (camera) camera.transform.position = fly.transform.position + new Vector3(0, 2, -4);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, NextScenePath);
        var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        foreach (string path in new[] { ScenePath, NextScenePath })
            if (!scenes.Exists(entry => entry.path == path))
                scenes.Add(new EditorBuildSettingsScene(path, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    static Material Mat(string name, Color color, float metallic = 0, float gloss = .3f, bool transparent = false)
    {
        if (materials.TryGetValue(name, out var material)) return material;
        string path = Folder + "/" + name + ".mat";
        material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!material)
        {
            material = new Material(Shader.Find("Standard"));
            AssetDatabase.CreateAsset(material, path);
        }
        material.color = color;
        material.SetFloat("_Metallic", metallic);
        material.SetFloat("_Glossiness", gloss);
        if (transparent)
        {
            material.SetFloat("_Mode", 3);
            material.SetInt("_SrcBlend", 5);
            material.SetInt("_DstBlend", 10);
            material.SetInt("_ZWrite", 0);
            material.EnableKeyword("_ALPHABLEND_ON");
            material.renderQueue = 3000;
        }
        EditorUtility.SetDirty(material);
        materials[name] = material;
        return material;
    }

    static GameObject Shape(string name, PrimitiveType primitive, Vector3 position, Vector3 scale,
        Material material, Transform parent = null, bool collision = true)
    {
        var obj = GameObject.CreatePrimitive(primitive);
        obj.name = name;
        obj.transform.SetParent(parent ? parent : root, false);
        obj.transform.localPosition = position;
        obj.transform.localScale = scale;
        obj.GetComponent<Renderer>().sharedMaterial = material;
        if (!collision) UnityEngine.Object.DestroyImmediate(obj.GetComponent<Collider>());
        return obj;
    }

    static GameObject Box(string name, Vector3 p, Vector3 size, Material mat, bool collision = true, Transform parent = null)
        => Shape(name, PrimitiveType.Cube, p, size, mat, parent, collision);
    static GameObject Cylinder(string name, Vector3 p, Vector3 size, Material mat, bool collision = true, Transform parent = null)
        => Shape(name, PrimitiveType.Cylinder, p, size, mat, parent, collision);

    static void Room()
    {
        var oak = Mat("Smoked oak", new Color(.20f, .105f, .056f), .05f, .35f);
        var wallpaper = Mat("Damask wallpaper", new Color(.43f, .30f, .25f));
        var panel = Mat("Moss wainscot", new Color(.20f, .29f, .24f));
        var ivory = Mat("Ivory trim", new Color(.74f, .67f, .52f));
        var floorTile = Mat("Kitchen floor tile", new Color(.41f, .39f, .32f));
        Box("Floor", new Vector3(0, -.5f, 0), new Vector3(32, 1, 32), floorTile);
        Box("Ceiling", new Vector3(0, 48.5f, 0), new Vector3(32, 1, 32), wallpaper);
        Box("East wall", new Vector3(16.5f, 24, 0), new Vector3(1, 48, 33), wallpaper);
        Box("West wall", new Vector3(-16.5f, 24, 0), new Vector3(1, 48, 33), wallpaper);
        // The north wall has a genuine fourteen-unit-wide window opening.
        Box("North wall left", new Vector3(-11.5f, 24, 16.5f), new Vector3(9, 48, 1), wallpaper);
        Box("North wall right", new Vector3(11.5f, 24, 16.5f), new Vector3(9, 48, 1), wallpaper);
        Box("North wall sill", new Vector3(0, 5.5f, 16.5f), new Vector3(14, 11, 1), wallpaper);
        Box("North wall header", new Vector3(0, 41, 16.5f), new Vector3(14, 14, 1), wallpaper);
        Box("South wall left", new Vector3(-11, 24, -16.5f), new Vector3(10, 48, 1), wallpaper);
        Box("South wall right", new Vector3(11, 24, -16.5f), new Vector3(10, 48, 1), wallpaper);
        Box("South wall above door", new Vector3(0, 31.5f, -16.5f), new Vector3(12, 33, 1), wallpaper);
        foreach (int sign in new[] { -1, 1 })
        {
            Box("Wainscot side", new Vector3(sign * 16.05f, 3, 0), new Vector3(.15f, 6, 32), panel);
            Box("Baseboard side", new Vector3(sign * 15.9f, .5f, 0), new Vector3(.35f, 1, 32), ivory);
            Box("Crown side", new Vector3(sign * 15.9f, 47.3f, 0), new Vector3(.45f, .8f, 32), ivory);
        }
        Box("North crown", new Vector3(0, 47.3f, 15.9f), new Vector3(32, .8f, .45f), ivory);
        Box("South crown", new Vector3(0, 47.3f, -15.9f), new Vector3(32, .8f, .45f), ivory);
        for (int sign = -1; sign <= 1; sign += 2)
        {
            Box("Picture rail", new Vector3(sign * 15.86f, 9, 0), new Vector3(.2f, .3f, 31), oak, false);
            for (int z = -13; z <= 13; z += 4)
                Box("Raised Victorian wall panel", new Vector3(sign * 15.84f, 3.1f, z),
                    new Vector3(.12f, 4.7f, 3.1f), ivory, false);
        }
        for (int x = -14; x <= 14; x += 4)
            Box("Oak floor plank", new Vector3(x, .012f, 0), new Vector3(.1f, .025f, 31), oak, false);
        for (int x = -6; x <= 6; x += 3)
            Box("Window mullion", new Vector3(x, 22.5f, 16.25f), new Vector3(.2f, 23, .25f), ivory);
        Box("Window transom", new Vector3(0, 23, 16.25f), new Vector3(14, .22f, .25f), ivory);
        Box("Window sill", new Vector3(0, 11.1f, 15.7f), new Vector3(15, .45f, 1.5f), ivory);
        Box("Window upper casing", new Vector3(0, 34.2f, 16.2f), new Vector3(15.3f, .5f, .6f), ivory);
        var glass = Mat("Warm window glass", new Color(.62f, .78f, .79f, .25f), .05f, .9f, true);
        Box("Large sunlit window", new Vector3(0, 22.5f, 16.48f), new Vector3(13.8f, 22.8f, .07f), glass);
        var sun = new GameObject("Window daylight").AddComponent<Light>();
        sun.transform.SetParent(root, false); sun.transform.localPosition = new Vector3(0, 28, 14);
        sun.type = LightType.Point; sun.range = 30; sun.intensity = 2.2f;
        sun.color = new Color(1, .84f, .62f);
    }

    static void SinkAndCounter()
    {
        var ivory = Mat("Ivory trim", new Color(.74f, .67f, .52f));
        var tile = Mat("Blue glazed tile", new Color(.31f, .48f, .50f), .08f, .65f);
        var grout = Mat("Tile grout", new Color(.69f, .65f, .55f));
        var enamel = Mat("Cream enamel", new Color(.84f, .82f, .71f), .12f, .8f);
        var brass = Mat("Aged brass", new Color(.44f, .31f, .12f), .75f, .7f);
        var pipe = Mat("Copper drain pipe", new Color(.38f, .22f, .14f), .65f, .5f);
        var water = Mat("Flowing water", new Color(.35f, .72f, .84f, .48f), .08f, .95f, true);

        var cabinet = Mat("Painted cabinet", new Color(.32f, .34f, .30f));
        // A shell, not a solid block: the drain and U-bend must have an open interior.
        Box("Counter cabinet front left", new Vector3(-6.85f, 2.35f, 10.48f),
            new Vector3(15.3f, 4.7f, .24f), cabinet);
        Box("Counter cabinet front right", new Vector3(9.25f, 2.35f, 10.48f),
            new Vector3(10.5f, 4.7f, .24f), cabinet);
        Box("Counter cabinet above pipe outlet", new Vector3(2.4f, 4.05f, 10.48f),
            new Vector3(3.2f, 1.3f, .24f), cabinet);
        Box("Counter cabinet back", new Vector3(0, 2.35f, 15.52f), new Vector3(29, 4.7f, .24f), cabinet);
        Box("Counter cabinet left end", new Vector3(-14.4f, 2.35f, 13), new Vector3(.24f, 4.7f, 5), cabinet);
        Box("Counter cabinet right end", new Vector3(14.4f, 2.35f, 13), new Vector3(.24f, 4.7f, 5), cabinet);
        // Leave two real openings in the tiled counter for the split basins.
        Box("Counter left edge", new Vector3(-12, 5.1f, 13), new Vector3(5, .6f, 5), tile);
        Box("Counter right edge", new Vector3(12, 5.1f, 13), new Vector3(5, .6f, 5), tile);
        Box("Tiled worktop beside left basin", new Vector3(-7.1f, 5.1f, 13), new Vector3(4.8f, .6f, 5), tile);
        Box("Tiled worktop beside right basin", new Vector3(7.1f, 5.1f, 13), new Vector3(4.8f, .6f, 5), tile);
        Box("Counter front strip", new Vector3(0, 5.1f, 10.75f), new Vector3(19, .6f, .5f), tile);
        Box("Counter rear strip", new Vector3(0, 5.1f, 15.25f), new Vector3(19, .6f, .5f), tile);
        Box("Sink enamel divider", new Vector3(0, 4.9f, 13), new Vector3(.3f, .8f, 4), enamel);
        for (int sign = -1; sign <= 1; sign += 2)
        {
            float x = sign * 2.4f;
            Box("Split enamel basin front", new Vector3(x, 4.72f, 11.05f), new Vector3(4.5f, 1.25f, .25f), enamel);
            Box("Split enamel basin rear", new Vector3(x, 4.72f, 14.95f), new Vector3(4.5f, 1.25f, .25f), enamel);
            Box("Split enamel basin outer side", new Vector3(sign * 4.68f, 4.72f, 13), new Vector3(.25f, 1.25f, 4), enamel);
            if (sign < 0) Box("Left basin floor", new Vector3(x, 4.13f, 13), new Vector3(4.5f, .16f, 4), enamel);
            else
            {
                // Four quadrants surround an open 2.5-unit drain; there is no hidden floor collider across it.
                Box("Drain floor west", new Vector3(.72f, 4.13f, 13), new Vector3(.8f, .16f, 4), enamel);
                Box("Drain floor east", new Vector3(4.08f, 4.13f, 13), new Vector3(.8f, .16f, 4), enamel);
                Box("Drain floor front", new Vector3(2.4f, 4.13f, 11.38f), new Vector3(2.6f, .16f, .75f), enamel);
                Box("Drain floor back", new Vector3(2.4f, 4.13f, 14.62f), new Vector3(2.6f, .16f, .75f), enamel);
            }
        }
        Cylinder("Brass drain rim", new Vector3(2.4f, 4.06f, 13), new Vector3(1.36f, .07f, 1.36f), brass, false);
        Cylinder("Tap base", new Vector3(2.4f, 5.75f, 14.5f), new Vector3(.35f, .35f, .35f), brass);
        Cylinder("Tap neck", new Vector3(2.4f, 6.65f, 14.5f), new Vector3(.15f, .8f, .15f), brass);
        Box("Tap over basin", new Vector3(2.4f, 7.36f, 13.5f), new Vector3(.2f, .2f, 2), brass);
        Cylinder("Tap nozzle", new Vector3(2.4f, 7.13f, 12.5f), new Vector3(.16f, .3f, .16f), brass);
        var stream = Cylinder("Running tap hazard", new Vector3(2.4f, 5.8f, 12.5f),
            new Vector3(.22f, 1.25f, .22f), water, false);
        var volume = stream.AddComponent<CapsuleCollider>(); volume.isTrigger = true;
        volume.radius = .65f; volume.height = 2.6f;
        stream.AddComponent<KitchenHazard>().kind = KitchenHazard.Kind.TapWater;
        for (int i = 0; i < 10; i++)
        {
            var drop = Shape("Animated tap droplet", PrimitiveType.Sphere,
                new Vector3(2.4f, 6.95f - i * .25f, 12.5f), new Vector3(.22f, .34f, .22f), water, null, false);
            var motion = drop.AddComponent<KitchenMotion>(); motion.motion = KitchenMotion.Motion.WaterDrop;
            motion.speed = 1.8f; motion.travel = 2.5f;
        }
        for (int i = 0; i < 5; i++)
        {
            var drop = Shape("Water descending inside drain", PrimitiveType.Sphere,
                new Vector3(2.4f, 3.95f - i * .33f, 13), new Vector3(.18f, .28f, .18f),
                water, null, false);
            var motion = drop.AddComponent<KitchenMotion>(); motion.motion = KitchenMotion.Motion.WaterDrop;
            motion.speed = 1.4f; motion.travel = 1.8f;
        }
        var path = new[] { new Vector3(2.4f, 4.06f, 13), new Vector3(2.4f, 2.75f, 13),
            new Vector3(2.4f, 1.3f, 12.6f), new Vector3(2.4f, 1.3f, 10.5f),
            new Vector3(2.4f, 2.2f, 9.8f) };
        CreatePipe(path, 1.22f, pipe);
        Cylinder("In-sink erator housing", new Vector3(2.4f, 2.85f, 13), new Vector3(1.38f, .55f, 1.38f), pipe, false);
        var rotor = new GameObject("In-sink erator rotor"); rotor.transform.SetParent(root, false);
        rotor.transform.localPosition = new Vector3(2.4f, 3.35f, 13);
        var rotorMotion = rotor.AddComponent<KitchenMotion>(); rotorMotion.motion = KitchenMotion.Motion.Disposal; rotorMotion.speed = 185;
        for (int i = 0; i < 3; i++)
        {
            var blade = Box("Disposal blunt blade", new Vector3(0, 0, .48f), new Vector3(.17f, .08f, .72f), brass, false, rotor.transform);
            blade.transform.localRotation = Quaternion.Euler(0, i * 120, 0);
        }
        var disposal = new GameObject("Disposal cutting hazard"); disposal.transform.SetParent(root, false);
        disposal.transform.localPosition = new Vector3(2.4f, 3.33f, 13);
        var disposalTrigger = disposal.AddComponent<SphereCollider>(); disposalTrigger.radius = .92f; disposalTrigger.isTrigger = true;
        disposal.AddComponent<KitchenHazard>().kind = KitchenHazard.Kind.Disposal;
        var trap = Shape("Water trap surface", PrimitiveType.Sphere, path[path.Length - 1],
            new Vector3(2.2f, .18f, 2.2f), water, null, false);
        var trapTrigger = trap.AddComponent<SphereCollider>(); trapTrigger.radius = .55f; trapTrigger.isTrigger = true;
        trap.AddComponent<KitchenHazard>().kind = KitchenHazard.Kind.WaterTrap;
        var barrier = Cylinder("Water trap stops pipe traversal", path[path.Length - 1] +
            new Vector3(0, .15f, -.12f), new Vector3(1.17f, .11f, 1.17f), water);
        barrier.transform.localRotation = Quaternion.FromToRotation(Vector3.up,
            (path[path.Length - 1] - path[path.Length - 2]).normalized);

        // Counter tiles and cabinet panels give the room a Victorian enamel-and-brass finish.
        for (int x = -14; x <= 14; x++)
            if (Mathf.Abs(x) > 5)
                Box("Tile grout crossline", new Vector3(x, 5.413f, 13),
                    new Vector3(.028f, .022f, 4.7f), grout, false);
        for (int z = 11; z <= 15; z++)
        {
            Box("Left tile grout row", new Vector3(-12, 5.413f, z), new Vector3(4.7f, .022f, .028f), grout, false);
            Box("Right tile grout row", new Vector3(12, 5.413f, z), new Vector3(4.7f, .022f, .028f), grout, false);
        }
        for (int x = -11; x <= 11; x += 5)
        {
            if (x == 4) continue; // the drain exits through this cabinet bay
            Box("Cabinet panel", new Vector3(x, 2.3f, 10.46f), new Vector3(4.6f, 3.8f, .1f), ivory, false);
            Cylinder("Cabinet brass knob", new Vector3(x + 1.7f, 2.5f, 10.33f), new Vector3(.14f, .08f, .14f), brass, false);
        }
        Compost(-8.4f);
    }

    static void CreatePipe(Vector3[] path, float radius, Material material)
    {
        const int sides = 18;
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        for (int station = 0; station < path.Length; station++)
        {
            Vector3 tangent = (path[Mathf.Min(station + 1, path.Length - 1)] -
                               path[Mathf.Max(0, station - 1)]).normalized;
            Vector3 axis = Vector3.Cross(tangent, Vector3.right).normalized;
            if (axis.sqrMagnitude < .1f) axis = Vector3.forward;
            Vector3 across = Vector3.Cross(tangent, axis).normalized;
            for (int side = 0; side < sides; side++)
            {
                float angle = side * Mathf.PI * 2 / sides;
                vertices.Add(path[station] + radius * (axis * Mathf.Cos(angle) + across * Mathf.Sin(angle)));
            }
        }
        for (int station = 0; station < path.Length - 1; station++)
            for (int side = 0; side < sides; side++)
            {
                int a = station * sides + side, b = station * sides + (side + 1) % sides;
                int c = (station + 1) * sides + side, d = (station + 1) * sides + (side + 1) % sides;
                // Both windings: the outside is visible and the inside remains a fly-contact surface.
                triangles.Add(a); triangles.Add(c); triangles.Add(b);
                triangles.Add(b); triangles.Add(c); triangles.Add(d);
                triangles.Add(b); triangles.Add(c); triangles.Add(a);
                triangles.Add(d); triangles.Add(c); triangles.Add(b);
            }
        var mesh = new Mesh { name = "Traversable drain pipe" };
        mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        string pathName = Folder + "/Traversable drain pipe.asset";
        var saved = AssetDatabase.LoadAssetAtPath<Mesh>(pathName);
        if (saved) { EditorUtility.CopySerialized(mesh, saved); UnityEngine.Object.DestroyImmediate(mesh); mesh = saved; }
        else AssetDatabase.CreateAsset(mesh, pathName);
        var obj = new GameObject("Traversable drain pipe to water trap"); obj.transform.SetParent(root, false);
        obj.AddComponent<MeshFilter>().sharedMesh = mesh;
        obj.AddComponent<MeshRenderer>().sharedMaterial = material;
        obj.AddComponent<MeshCollider>().sharedMesh = mesh;
    }

    static void Compost(float x)
    {
        var copper = Mat("Copper compost pail", new Color(.39f, .20f, .11f), .65f, .45f);
        var dark = Mat("Compost", new Color(.16f, .14f, .08f));
        Cylinder("Compost pail", new Vector3(x, 6.42f, 12.4f), new Vector3(1.35f, .95f, 1.35f), copper);
        Cylinder("Compost interior", new Vector3(x, 7.41f, 12.4f), new Vector3(1.18f, .06f, 1.18f), dark, false);
        var lid = Cylinder("Compost lid ajar", new Vector3(x - .65f, 7.65f, 12.45f),
            new Vector3(1.39f, .08f, 1.39f), copper, false);
        lid.transform.localRotation = Quaternion.Euler(0, 0, -35);
        for (int i = 0; i < 3; i++)
        {
            var food = Shape("Compost fly food", PrimitiveType.Sphere,
                new Vector3(x + .3f * (i - 1), 7.5f, 12.2f + .35f * (i % 2)),
                new Vector3(.4f, .3f, .4f), dark, null, false);
            food.AddComponent<SphereCollider>().isTrigger = true;
            food.AddComponent<FlyFood>();
        }
    }

    static void Refrigerator(int side, float z)
    {
        var enamel = Mat("Cream enamel", new Color(.84f, .82f, .71f), .12f, .8f);
        var dark = Mat("Refrigerator interior", new Color(.16f, .19f, .18f));
        var brass = Mat("Aged brass", new Color(.44f, .31f, .12f), .75f, .7f);
        float x = side * 12.1f;
        var body = new GameObject("Refrigerator with damaged seal"); body.transform.SetParent(root, false);
        body.transform.localPosition = new Vector3(x, 0, z);
        Box("Fridge back", new Vector3(0, 5, 1.6f), new Vector3(4.7f, 10, .35f), enamel, true, body.transform);
        Box("Fridge left", new Vector3(-2.2f, 5, 0), new Vector3(.35f, 10, 3.5f), enamel, true, body.transform);
        Box("Fridge right", new Vector3(2.2f, 5, 0), new Vector3(.35f, 10, 3.5f), enamel, true, body.transform);
        Box("Fridge top", new Vector3(0, 9.8f, 0), new Vector3(4.7f, .35f, 3.5f), enamel, true, body.transform);
        Box("Fridge bottom", new Vector3(0, .2f, 0), new Vector3(4.7f, .4f, 3.5f), enamel, true, body.transform);
        Box("Cold interior", new Vector3(0, 5, 1.38f), new Vector3(4.1f, 8.8f, .08f), dark, false, body.transform);
        for (int y = 2; y <= 8; y += 2)
            Box("Fridge shelf", new Vector3(0, y, .2f), new Vector3(4.1f, .12f, 2.5f), brass, true, body.transform);
        // A visible cracked door leaves the front aperture open enough for the fly collider.
        var door = Box("Fridge door open past damaged seal", new Vector3(-1.9f, 5, -2.9f),
            new Vector3(4.4f, 9.7f, .32f), enamel, true, body.transform);
        door.transform.localRotation = Quaternion.Euler(0, -27 * side, 0);
        Box("Fridge handle", new Vector3(.8f, 6.3f, -3.0f), new Vector3(.16f, 1.5f, .25f), brass, false, body.transform);
        Box("Broken lower seal", new Vector3(1.85f, 2.1f, -1.8f), new Vector3(.13f, 3.7f, .13f), dark, false, body.transform);
    }

    static void Stove(int side)
    {
        var enamel = Mat("Cream enamel", new Color(.84f, .82f, .71f), .12f, .8f);
        var iron = Mat("Cast iron", new Color(.10f, .10f, .105f), .55f, .35f);
        var hot = Mat("Hot burner", new Color(.95f, .22f, .04f), .12f, .55f);
        float x = side * 11.5f;
        Box("Victorian stove body", new Vector3(x, 2.4f, 2), new Vector3(7, 4.8f, 5.5f), enamel);
        Box("Cast iron stovetop", new Vector3(x, 4.95f, 2), new Vector3(7.2f, .25f, 5.7f), iron);
        for (int ix = -1; ix <= 1; ix += 2)
            for (int iz = -1; iz <= 1; iz += 2)
            {
                var burner = Cylinder("Glowing stove heat hazard", new Vector3(x + ix * 1.5f, 5.14f, 2 + iz * 1.25f),
                    new Vector3(.95f, .06f, .95f), hot, false);
                burner.AddComponent<SphereCollider>().isTrigger = true;
                var hazard = burner.AddComponent<KitchenHazard>(); hazard.kind = KitchenHazard.Kind.Stove; hazard.damagePerSecond = 18;
                Cylinder("Burner ring", burner.transform.localPosition + new Vector3(0, .1f, 0),
                    new Vector3(1.05f, .035f, 1.05f), iron, false);
            }
        Box("Oven door", new Vector3(x, 2.2f, 2 - 2.85f), new Vector3(5.3f, 3.5f, .12f), iron, false);
        Teapot(x + 1.5f, 2 + 1.25f);
    }

    static void Teapot(float x, float z)
    {
        var copper = Mat("Copper compost pail", new Color(.39f, .20f, .11f), .65f, .45f);
        var ivory = Mat("Cream enamel", new Color(.84f, .82f, .71f), .12f, .8f);
        var steam = Mat("Teapot steam", new Color(.83f, .91f, .89f, .28f), .03f, .3f, true);
        var body = Shape("Victorian teapot on stove", PrimitiveType.Sphere,
            new Vector3(x, 5.89f, z), new Vector3(1.55f, 1.12f, 1.55f), ivory);
        Cylinder("Teapot foot", new Vector3(x, 5.3f, z), new Vector3(.63f, .11f, .63f), copper);
        Cylinder("Teapot lid", new Vector3(x, 6.48f, z), new Vector3(.83f, .12f, .83f), copper);
        Shape("Teapot finial", PrimitiveType.Sphere, new Vector3(x, 6.75f, z),
            new Vector3(.3f, .3f, .3f), ivory);
        var spout = Cylinder("Teapot spout", new Vector3(x - 1.05f, 6.17f, z),
            new Vector3(.25f, .8f, .25f), copper, false);
        spout.transform.localRotation = Quaternion.Euler(0, 0, -58);
        // The open loop handle remains visibly separate from the pot silhouette.
        for (int i = 0; i < 7; i++)
        {
            float angle = Mathf.Lerp(-100, 100, i / 6f) * Mathf.Deg2Rad;
            var segment = Box("Teapot curved handle segment",
                new Vector3(x + .89f + Mathf.Cos(angle) * .52f, 5.92f + Mathf.Sin(angle) * .67f, z),
                new Vector3(.17f, .32f, .19f), copper, false);
            segment.transform.localRotation = Quaternion.Euler(0, 0, -angle * Mathf.Rad2Deg);
        }
        var plume = new GameObject("Scalding teapot steam hazard");
        plume.transform.SetParent(root, false);
        plume.transform.localPosition = new Vector3(x - 1.55f, 7.75f, z);
        var volume = plume.AddComponent<CapsuleCollider>();
        volume.direction = 1; volume.radius = .6f; volume.height = 3.5f; volume.isTrigger = true;
        var hazard = plume.AddComponent<KitchenHazard>();
        hazard.kind = KitchenHazard.Kind.Steam; hazard.damagePerSecond = 12;
        for (int i = 0; i < 7; i++)
        {
            var puff = Shape("Animated teapot steam", PrimitiveType.Sphere,
                new Vector3(x - 1.55f, 6.8f + i * .38f, z),
                new Vector3(.28f + i * .08f, .22f + i * .08f, .28f + i * .08f),
                steam, null, false);
            var motion = puff.AddComponent<KitchenMotion>();
            motion.motion = KitchenMotion.Motion.SteamPuff;
            motion.speed = 1.25f; motion.travel = 2.7f;
        }
    }

    static void Shelves(int side)
    {
        var oak = Mat("Smoked oak", new Color(.20f, .105f, .056f), .05f, .35f);
        var brass = Mat("Aged brass", new Color(.44f, .31f, .12f), .75f, .7f);
        for (int row = 0; row < 3; row++)
        {
            float y = 10 + row * 3.3f;
            Box("Wall shelf", new Vector3(side * 13.4f, y, 8), new Vector3(3.2f, .25f, 7), oak);
            for (int i = 0; i < 2; i++)
                Cylinder("Shelf jar", new Vector3(side * 13.4f, y + .65f, 6.4f + i * 2.8f),
                    new Vector3(.47f, .55f, .47f), brass);
        }
    }

    static void Stools(int offset)
    {
        var oak = Mat("Smoked oak", new Color(.20f, .105f, .056f), .05f, .35f);
        for (int stool = 0; stool < 2; stool++)
        {
            float x = (stool == 0 ? -5 : 5) + offset;
            float z = 4 + (stool == 0 ? -1 : 1);
            Cylinder("Barstool seat", new Vector3(x, 4, z), new Vector3(1.45f, .24f, 1.45f), oak);
            for (int leg = 0; leg < 4; leg++)
                Box("Barstool leg", new Vector3(x + (leg % 2 == 0 ? -1 : 1), 1.9f,
                    z + (leg < 2 ? -1 : 1)), new Vector3(.28f, 3.8f, .28f), oak);
        }
    }

    static void Trash(int side, float z)
    {
        var copper = Mat("Copper compost pail", new Color(.39f, .20f, .11f), .65f, .45f);
        var dark = Mat("Refrigerator interior", new Color(.16f, .19f, .18f));
        float x = side * 10.5f;
        Cylinder("Trash can", new Vector3(x, 1.8f, z), new Vector3(1.2f, 1.8f, 1.2f), copper);
        Cylinder("Trash opening", new Vector3(x, 3.65f, z), new Vector3(1.03f, .04f, 1.03f), dark, false);
        Cylinder("Trash rim", new Vector3(x, 3.57f, z), new Vector3(1.25f, .09f, 1.25f), copper, false);
        var food = Shape("Trash fly food", PrimitiveType.Sphere, new Vector3(x, 3.88f, z),
            new Vector3(.7f, .4f, .7f), dark, null, false);
        food.AddComponent<SphereCollider>().isTrigger = true; food.AddComponent<FlyFood>();
    }

    static void DoorAndKeyhole()
    {
        var oak = Mat("Smoked oak", new Color(.20f, .105f, .056f), .05f, .35f);
        var brass = Mat("Aged brass", new Color(.44f, .31f, .12f), .75f, .7f);
        var iron = Mat("Cast iron", new Color(.10f, .10f, .105f), .55f, .35f);
        Box("Door frame left", new Vector3(-5.9f, 7.5f, -16), new Vector3(.5f, 15.5f, 1), brass);
        Box("Door frame right", new Vector3(5.9f, 7.5f, -16), new Vector3(.5f, 15.5f, 1), brass);
        Box("Door frame head", new Vector3(0, 15.4f, -16), new Vector3(12, .65f, 1), brass);
        Box("Victorian door upper", new Vector3(0, 10.7f, -16.3f), new Vector3(11, 8.5f, .7f), oak);
        Box("Victorian door lower", new Vector3(0, 2.05f, -16.3f), new Vector3(11, 4.1f, .7f), oak);
        Box("Keyhole left stile", new Vector3(-3.15f, 5.35f, -16.3f), new Vector3(4.7f, 2.5f, .7f), oak);
        Box("Keyhole right stile", new Vector3(3.15f, 5.35f, -16.3f), new Vector3(4.7f, 2.5f, .7f), oak);
        Box("Brass keyhole left cheek", new Vector3(-.99f, 5.35f, -16.78f), new Vector3(.17f, 2.6f, .09f), brass, false);
        Box("Brass keyhole right cheek", new Vector3(.99f, 5.35f, -16.78f), new Vector3(.17f, 2.6f, .09f), brass, false);
        Box("Brass keyhole arch", new Vector3(0, 6.7f, -16.78f), new Vector3(2.2f, .18f, .09f), brass, false);
        Cylinder("Door knob", new Vector3(3.8f, 7.4f, -17), new Vector3(.55f, .22f, .55f), brass);
        for (int side = -1; side <= 1; side += 2)
            Box("Lock transit wall", new Vector3(side * 1.2f, 5.4f, -18.2f),
                new Vector3(.22f, 2.7f, 3.4f), iron);
        Box("Lock transit floor", new Vector3(0, 4.05f, -18.2f), new Vector3(2.5f, .16f, 3.4f), brass);
        Box("Lock transit ceiling", new Vector3(0, 6.77f, -18.2f), new Vector3(2.5f, .16f, 3.4f), brass);
        for (int i = 0; i < 4; i++)
        {
            Box("Visible lock tumbler pin", new Vector3(-.76f + i * .51f, 6.43f, -17.5f - i * .48f),
                new Vector3(.2f, .47f + (i % 2) * .16f, .22f), brass, false);
            Cylinder("Lock gear tooth", new Vector3(-.75f + i * .5f, 4.2f, -17.5f - i * .48f),
                new Vector3(.19f, .17f, .19f), brass, false);
        }
        var exit = new GameObject("Travel to next level through keyhole"); exit.transform.SetParent(root, false);
        exit.transform.localPosition = new Vector3(0, 5.4f, -19.65f);
        var collider = exit.AddComponent<BoxCollider>(); collider.size = new Vector3(1.8f, 2.25f, .35f); collider.isTrigger = true;
        exit.AddComponent<KitchenExit>();
    }

    static void CeilingFan()
    {
        var brass = Mat("Aged brass", new Color(.44f, .31f, .12f), .75f, .7f);
        var oak = Mat("Smoked oak", new Color(.20f, .105f, .056f), .05f, .35f);
        Cylinder("Ceiling fan downrod", new Vector3(0, 46.5f, 0), new Vector3(.17f, 1.4f, .17f), brass);
        var hub = new GameObject("Turning ceiling fan"); hub.transform.SetParent(root, false);
        hub.transform.localPosition = new Vector3(0, 44.9f, 0);
        var motion = hub.AddComponent<KitchenMotion>(); motion.motion = KitchenMotion.Motion.CeilingFan; motion.speed = 75;
        Cylinder("Fan motor", Vector3.zero, new Vector3(.75f, .25f, .75f), brass, true, hub.transform);
        for (int i = 0; i < 4; i++)
        {
            var blade = Box("Ceiling fan blade", new Vector3(2.8f, 0, 0), new Vector3(5, .12f, .75f),
                oak, true, hub.transform);
            blade.transform.localRotation = Quaternion.Euler(0, i * 90, 0);
            blade.transform.localPosition = Quaternion.Euler(0, i * 90, 0) * new Vector3(2.8f, 0, 0);
        }
    }

    static void Plants()
    {
        string[] presets = { "Amber spiral", "Silver opposite" };
        for (int i = 0; i < presets.Length; i++)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Generated/Plants/" + presets[i] + ".prefab");
            if (!prefab) throw new InvalidOperationException("Missing plant preset: " + presets[i]);
            var plant = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            plant.name = "Window plant " + (i + 1);
            plant.transform.SetParent(root, false);
            plant.transform.localPosition = new Vector3(i == 0 ? -11 : 11, 0, 6.4f);
            plant.transform.localRotation = Quaternion.Euler(0, random.Next(0, 360), 0);
        }
    }

    [MenuItem("Fruit Fly/Levels/Validate Kitchen Level 1")]
    public static void Validate()
    {
        var level = UnityEngine.Object.FindObjectOfType<KitchenLevel>();
        var fly = UnityEngine.Object.FindObjectOfType<FlyMotor>();
        var combat = UnityEngine.Object.FindObjectOfType<RiderCombat>();
        var hazards = UnityEngine.Object.FindObjectsOfType<KitchenHazard>();
        var plants = UnityEngine.Object.FindObjectsOfType<ProceduralPlant>();
        var drain = GameObject.Find("Traversable drain pipe to water trap");
        var trapBarrier = GameObject.Find("Water trap stops pipe traversal");
        var exit = UnityEngine.Object.FindObjectOfType<KitchenExit>();
        int missingScripts = 0;
        foreach (var go in UnityEngine.Object.FindObjectsOfType<GameObject>())
            missingScripts += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go);
        Physics.SyncTransforms();
        bool drainClear = true;
        foreach (var point in new[] { new Vector3(2.4f, 3.45f, 13),
                     new Vector3(2.4f, 2.3f, 13), new Vector3(2.4f, 1.3f, 12.6f),
                     new Vector3(2.4f, 1.3f, 10.5f), new Vector3(2.4f, 1.6f, 10.3f) })
            drainClear &= !Physics.CheckSphere(point, .31f, ~0, QueryTriggerInteraction.Ignore);
        bool keyholeClear = !Physics.CheckSphere(new Vector3(0, 5.4f, -16.3f), .4f,
            ~0, QueryTriggerInteraction.Ignore);
        bool passed = level && fly && combat && !combat.spawnStandaloneJouster &&
                      Mathf.Approximately(KitchenLevel.RoomWidth, 32) &&
                      hazards.Length >= 8 && plants.Length == 2 && missingScripts == 0 &&
                      drainClear && keyholeClear && drain && trapBarrier && trapBarrier.GetComponent<Collider>() &&
                      drain.GetComponent<MeshCollider>() && exit &&
                      GameObject.Find("Large sunlit window") &&
                      GameObject.Find("In-sink erator rotor") &&
                      GameObject.Find("Fridge door open past damaged seal") &&
                      GameObject.Find("Compost lid ajar") &&
                      GameObject.Find("Water trap stops pipe traversal") &&
                      GameObject.Find("Victorian teapot on stove") &&
                      GameObject.Find("Scalding teapot steam hazard") &&
                      GameObject.Find("Turning ceiling fan");
        string detail = "seed=" + (level ? level.seed.ToString() : "missing") +
                        " layout=" + (level ? level.layoutSignature : "missing") +
                        " hazards=" + hazards.Length + " plants=" + plants.Length +
                        " drain=" + (bool)drain + " exit=" + (bool)exit +
                        " player=" + (bool)fly + "/" + (bool)combat +
                        " clear=" + drainClear + "/" + keyholeClear;
        detail += " missingScripts=" + missingScripts;
        string report = "{\"status\":\"" + (passed ? "passed" : "failed") +
                        "\",\"details\":\"" + detail.Replace("\"", "\\\"") + "\"}";
        File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath,
            "../Research/kitchen-level-evaluation.json")), report);
        if (passed) Debug.Log("KITCHEN_LEVEL_CHECK_PASSED " + detail);
        else Debug.LogError("KITCHEN_LEVEL_CHECK_FAILED " + detail);
    }
}

public sealed class KitchenLevelWindow : EditorWindow
{
    int seed;
    [MenuItem("Fruit Fly/Levels/Kitchen seed and layout")]
    static void Open() { GetWindow<KitchenLevelWindow>("Kitchen seed"); }
    void OnEnable() { seed = EditorPrefs.GetInt("FruitFlyJoust.KitchenSeed", 1049); }
    void OnGUI()
    {
        EditorGUILayout.LabelField("Level 1 · Victorian kitchen", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Eight by eight room feet, enlarged four world units per foot for fly-scale play. Sink and keyhole stay fixed; appliance side, stools, trash and plant orientation vary by seed.", MessageType.Info);
        seed = EditorGUILayout.IntField("Arrangement seed", seed);
        if (GUILayout.Button("Build and validate Kitchen Level 1"))
        {
            EditorPrefs.SetInt("FruitFlyJoust.KitchenSeed", seed);
            KitchenLevelTools.Build(seed);
        }
        if (GUILayout.Button("Validate open kitchen scene")) KitchenLevelTools.Validate();
    }
}
