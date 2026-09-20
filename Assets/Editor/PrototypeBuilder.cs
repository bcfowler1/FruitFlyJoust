using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using FruitFlyJoust;

public static class PrototypeBuilder
{
    [InitializeOnLoadMethod]
    static void FirstOpen()
    {
        if (!System.IO.File.Exists("Assets/Practice.unity"))
            EditorApplication.delayCall += Generate;
    }
    static Material Material(string name, Color color)
    {
        string path = "Assets/Generated/" + name + ".mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing) { existing.color = color; return existing; }
        var material = new Material(Shader.Find("Standard"));
        material.color = color;
        AssetDatabase.CreateAsset(material, path);
        return material;
    }
    static GameObject Shape(string name, PrimitiveType type, Vector3 pos, Vector3 scale,
        Material material, Transform parent = null, bool collider = true)
    {
        var obj = GameObject.CreatePrimitive(type);
        obj.name = name;
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = pos;
        obj.transform.localScale = scale;
        obj.GetComponent<Renderer>().sharedMaterial = material;
        if (!collider) Object.DestroyImmediate(obj.GetComponent<Collider>());
        return obj;
    }
    [MenuItem("Fruit Fly/Generate Practice Scene")]
    public static void Generate()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Generated")) AssetDatabase.CreateFolder("Assets", "Generated");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var floor = Material("Floor", new Color(.26f, .32f, .36f));
        var wall = Material("Wall", new Color(.63f, .70f, .74f));
        var amber = Material("Fly", new Color(.68f, .35f, .12f));
        var red = Material("Eyes", new Color(.65f, .045f, .04f));
        var wing = Material("Wings", new Color(.70f, .88f, .96f));
        var dark = Material("Rider", new Color(.1f, .14f, .2f));
        var green = Material("Landing", new Color(.17f, .65f, .40f));
        var gold = Material("Hoops", new Color(1f, .68f, .12f));
        Shape("Floor", PrimitiveType.Cube, new Vector3(0, -.5f, 0), new Vector3(70, 1, 70), floor);
        Shape("North wall", PrimitiveType.Cube, new Vector3(0, 12, 35), new Vector3(70, 25, 1), wall);
        Shape("South wall", PrimitiveType.Cube, new Vector3(0, 12, -35), new Vector3(70, 25, 1), wall);
        Shape("East wall", PrimitiveType.Cube, new Vector3(35, 12, 0), new Vector3(1, 25, 70), wall);
        Shape("West wall", PrimitiveType.Cube, new Vector3(-35, 12, 0), new Vector3(1, 25, 70), wall);
        Shape("Ceiling", PrimitiveType.Cube, new Vector3(0, 25, 0), new Vector3(70, 1, 70), wall);
        var pillar = Shape("Orbit pillar", PrimitiveType.Cylinder, new Vector3(12, 7, 10), new Vector3(3, 7, 3), amber);
        Shape("Landing table", PrimitiveType.Cube, new Vector3(-12, 3, 12), new Vector3(8, 1, 8), green);
        for (int i = 0; i < 4; i++)
            Shape("Table leg", PrimitiveType.Cube, new Vector3(-12 + (i % 2 == 0 ? -3 : 3), 1.25f, 12 + (i < 2 ? -3 : 3)), new Vector3(.5f, 2.5f, .5f), dark);

        var fly = new GameObject("Fruit fly"); fly.layer = 2;
        fly.transform.position = new Vector3(0, 1.2f, -18);
        var capsule = fly.AddComponent<CapsuleCollider>(); capsule.radius = .5f; capsule.height = 1.5f; capsule.direction = 2;
        fly.AddComponent<Rigidbody>();
        var input = fly.AddComponent<RiderInput>();
        var brain = fly.AddComponent<PlaceholderBrain>();
        var motor = fly.AddComponent<FlyMotor>(); motor.rider = input; motor.brain = brain;
        var visual = new GameObject("Body and saddle").transform; visual.SetParent(fly.transform, false);
        motor.bodyVisual = visual;
        Shape("Thorax", PrimitiveType.Sphere, Vector3.zero, new Vector3(.9f, .7f, 1.4f), amber, visual, false);
        Shape("Abdomen", PrimitiveType.Sphere, new Vector3(0, -.05f, -.85f), new Vector3(.7f, .6f, 1.2f), amber, visual, false);
        Shape("Head", PrimitiveType.Sphere, new Vector3(0, .08f, .85f), new Vector3(.75f, .65f, .65f), amber, visual, false);
        for (int side = -1; side <= 1; side += 2)
        {
            Shape("Eye", PrimitiveType.Sphere, new Vector3(side * .28f, .16f, 1), new Vector3(.38f, .4f, .35f), red, visual, false);
            var w = Shape("Wing", PrimitiveType.Sphere, new Vector3(side * 1.05f, .15f, -.25f), new Vector3(1.7f, .045f, .7f), wing, visual, false);
            w.AddComponent<WingBeat>().side = side;
            for (int leg = 0; leg < 3; leg++)
                Shape("Leg", PrimitiveType.Cube, new Vector3(side * .55f, -.35f, .45f - leg * .45f), new Vector3(.7f, .07f, .07f), dark, visual, false);
            var rein = new GameObject("Rein").AddComponent<LineRenderer>();
            rein.transform.SetParent(visual, false); rein.useWorldSpace = false;
            rein.sharedMaterial = dark; rein.startWidth = rein.endWidth = .025f; rein.positionCount = 2;
            rein.SetPosition(0, new Vector3(side * .2f, .8f, .15f)); rein.SetPosition(1, new Vector3(side * .3f, .25f, .9f));
        }
        Shape("Saddle", PrimitiveType.Cube, new Vector3(0, .42f, 0), new Vector3(.5f, .18f, .6f), dark, visual, false);
        Shape("Rider", PrimitiveType.Capsule, new Vector3(0, .9f, 0), new Vector3(.28f, .4f, .28f), dark, visual, false);
        Shape("Rider head", PrimitiveType.Sphere, new Vector3(0, 1.37f, .05f), Vector3.one * .28f, gold, visual, false);
        var camera = new GameObject("Rider camera"); camera.tag = "MainCamera";
        camera.transform.position = fly.transform.position + new Vector3(0, 2, -4);
        camera.AddComponent<Camera>().farClipPlane = 120; camera.AddComponent<AudioListener>();
        var rideCamera = camera.AddComponent<RiderCamera>(); rideCamera.fly = fly.transform; rideCamera.rider = input;
        var light = new GameObject("Sun").AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.2f;
        light.transform.rotation = Quaternion.Euler(45, -35, 0);
        RenderSettings.ambientLight = new Color(.5f, .55f, .65f);
        var course = new GameObject("Practice course").AddComponent<PracticeCourse>();
        course.fly = motor; course.pillar = pillar.transform; course.landingCenter = new Vector3(-12, 3.5f, 12);
        course.hoops = new Transform[3];
        Vector3[] centers = { new Vector3(0, 4, -8), new Vector3(0, 6, 3), new Vector3(0, 8, 15) };
        for (int h = 0; h < 3; h++)
        {
            var hoop = new GameObject("Hoop " + (h + 1)).transform; hoop.position = centers[h]; course.hoops[h] = hoop;
            for (int i = 0; i < 32; i++)
            {
                float angle = i * Mathf.PI * 2 / 32;
                var segment = Shape("Ring segment", PrimitiveType.Cube,
                    new Vector3(Mathf.Sin(angle) * 3, Mathf.Cos(angle) * 3, 0), new Vector3(.62f, .18f, .18f), gold, hoop);
                segment.transform.localRotation = Quaternion.Euler(0, 0, -angle * Mathf.Rad2Deg);
            }
        }
        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), "Assets/Practice.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Practice.unity", true) };
        PlayerSettings.productName = "Fruit Fly Joust — The Ride";
        PlayerSettings.companyName = "FruitFlyJoust";
        AssetDatabase.SaveAssets();
        Debug.Log("FRUIT_FLY_SCENE_READY");
    }
    public static void Build()
    {
        var report = BuildPipeline.BuildPlayer(new[] { "Assets/Practice.unity" }, "Build/FruitFlyJoust.exe",
            BuildTarget.StandaloneWindows64, BuildOptions.None);
        if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            throw new System.Exception("Player build failed: " + report.summary.result);
    }
}
