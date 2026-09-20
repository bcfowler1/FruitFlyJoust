using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using FruitFlyJoust;

[InitializeOnLoad]
public static class ResearchTools
{
    private static Process backend;
    private const string HigherLandingCheck = "FruitFlyJoust.HigherLandingViewerCheck";
    static ResearchTools()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        EditorApplication.delayCall += RunRequestedHigherLandingCheck;
    }

    private static void RunRequestedHigherLandingCheck()
    {
        string request = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Research", "run-higher-landing-unity-check.request");
        if (!File.Exists(request)) return;
        // A request can be present while Unity is still restoring its startup scene or recompiling the final rider fit. Entering
        // Play during that window is immediately cancelled by the remaining initialization.
        if (EditorApplication.timeSinceStartup < 12.0)
        {
            EditorApplication.delayCall += RunRequestedHigherLandingCheck;
            return;
        }
        File.Delete(request);
        if (EditorApplication.isPlaying || UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty)
        {
            UnityEngine.Debug.LogError("HIGHER_LANDING_VIEWER_CHECK_BLOCKED: stop Play and save the current scene, then create the request again.");
            return;
        }
        EditorSceneManager.OpenScene("Assets/UnifiedFloorLab.unity");
        SessionState.SetBool("FruitFlyJoust.HigherLandingLaunch", true);
        SessionState.SetBool(HigherLandingCheck, true);
        EditorApplication.isPlaying = true;
    }

    [MenuItem("Fruit Fly/Research/Create Research Scene")]
    public static void CreateScene()
    { CreateScene(false); }
    [MenuItem("Fruit Fly/Research/Create Trained Flight Scene")]
    public static void CreateFlightScene()
    { CreateScene(true); }
    [MenuItem("Fruit Fly/Research/Create Unified Floor Qualification Scene")]
    public static void CreateUnifiedScene()
    { CreateScene(true, unified: true); }
    [MenuItem("Fruit Fly/Research/Run Unified Floor Viewer Checks")]
    public static void CheckUnifiedViewer()
    {
        var viewer = UnityEngine.Object.FindObjectOfType<ResearchViewer>();
        if (!EditorApplication.isPlaying || !viewer || !viewer.unifiedLab) throw new InvalidOperationException("Run checks in Play in UnifiedFloorLab.");
        if (!viewer.GetComponent<UnifiedFloorViewerCheck>()) viewer.gameObject.AddComponent<UnifiedFloorViewerCheck>();
    }
    public static void CreateWalkingScene()
    {
        CreateScene(false, true);
        var viewer = UnityEngine.Object.FindObjectOfType<ResearchViewer>();
        viewer.geometryResource = "FlyWalkingGeometry";
        viewer.walkingLab = true;
        var floor = GameObject.Find("Display floor");
        if (floor) floor.transform.position = Vector3.zero;
        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), "Assets/WalkingLab.unity");
    }
    private static void CreateScene(bool flight, bool walking = false, bool unified = false)
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play before creating the research scene.");
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var viewer = new GameObject("Research body stream").AddComponent<ResearchViewer>();
        viewer.trainedFlight = flight;
        viewer.unifiedLab = unified;
        viewer.geometryResource = unified ? "FlyUnifiedGeometry" : flight ? "FlyFlightGeometry" : "FlyResearchGeometry";
        var anchor = new GameObject("Mounted rider anchor").transform;
        viewer.riderAnchor = anchor;
        var input = anchor.gameObject.AddComponent<RiderInput>();
        var rider = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        rider.name = "Mounted rider"; rider.transform.SetParent(anchor, false);
        rider.transform.localPosition = new Vector3(0, .55f, 0); rider.transform.localScale = new Vector3(.22f, .3f, .22f);
        UnityEngine.Object.DestroyImmediate(rider.GetComponent<Collider>());
        for (int side = -1; side <= 1; side += 2)
        {
            var leg = GameObject.CreatePrimitive(PrimitiveType.Cube); leg.name = "Rider gripping leg";
            leg.transform.SetParent(anchor, false); leg.transform.localPosition = new Vector3(side * .23f, .2f, 0);
            leg.transform.localScale = new Vector3(.1f, .45f, .12f);
            UnityEngine.Object.DestroyImmediate(leg.GetComponent<Collider>());
        }
        var camera = new GameObject("Research rider camera"); camera.tag = "MainCamera";
        camera.AddComponent<Camera>(); camera.AddComponent<AudioListener>();
        var look = camera.AddComponent<RiderCamera>(); look.fly = anchor; look.rider = input;
        look.anchorHeight = .25f;
        look.cameraRise = 0;
        var light = new GameObject("Research light").AddComponent<Light>(); light.type = LightType.Directional;
        light.transform.rotation = Quaternion.Euler(45, -30, 0); light.intensity = 1.2f;
        RenderSettings.ambientLight = new Color(.5f, .55f, .6f);
        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane); floor.name = "Display floor";
        floor.transform.position = new Vector3(0, unified ? 0 : -.66f, 0);
        if (unified) floor.transform.localScale = Vector3.one * 100;
        UnityEngine.Object.DestroyImmediate(floor.GetComponent<Collider>());
        EditorSceneManager.SaveScene(scene, unified ? "Assets/UnifiedFloorLab.unity" : walking ? "Assets/WalkingLab.unity" : flight ? "Assets/FlightLab.unity" : "Assets/ResearchLab.unity");
    }
    [MenuItem("Fruit Fly/Research/Start GPU Backend")]
    public static void StartBackend()
    { StartBackend(false); }
    [MenuItem("Fruit Fly/Research/Start Trained Flight Backend")]
    public static void StartFlightBackend()
    { StartBackend(true); }
    [Serializable] private class QualificationStatus { public bool qualified = false; }
    [MenuItem("Fruit Fly/Research/Start Unified Floor Qualification Backend")]
    public static void StartUnifiedBackend()
    {
        string directory = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Research");
        string selected = null;
        foreach (string file in Directory.GetFiles(directory, "unified-contact-qualification-*-summary.json"))
        {
            var status = JsonUtility.FromJson<QualificationStatus>(File.ReadAllText(file));
            if (status != null && status.qualified && (selected == null || File.GetLastWriteTimeUtc(file) > File.GetLastWriteTimeUtc(selected))) selected = file;
        }
        if (selected == null) throw new InvalidOperationException("The unified floor loop has not passed its qualification suite.");
        StartBackend(true, unifiedQualification: selected);
    }
    [MenuItem("Fruit Fly/Research/Start Qualified Higher Landing Backend")]
    public static void StartHigherLandingBackend()
    {
        string directory = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Research");
        string selected = null;
        foreach (string file in Directory.GetFiles(directory, "higher-landing-qualification-*-summary.json"))
        {
            var status = JsonUtility.FromJson<QualificationStatus>(File.ReadAllText(file));
            if (status != null && status.qualified && (selected == null || File.GetLastWriteTimeUtc(file) > File.GetLastWriteTimeUtc(selected))) selected = file;
        }
        if (selected == null) throw new InvalidOperationException("The higher landing extension has not passed its qualification suite.");
        StartBackend(true, higherLandingQualification: selected);
    }
    [MenuItem("Fruit Fly/Research/Start Experimental Neural Flight Backend")]
    public static void StartNeuralFlightBackend()
    { StartBackend(true, true); }
    [MenuItem("Fruit Fly/Research/Start Six-Claw Wall Backend")]
    public static void StartWallBackend() { StartBackend(false,false,"wall"); }
    [MenuItem("Fruit Fly/Research/Start Six-Claw Ceiling Backend")]
    public static void StartCeilingBackend() { StartBackend(false,false,"ceiling"); }
    [MenuItem("Fruit Fly/Research/Start Experimental Wall Perch Transitions")]
    public static void StartWallTransitions() { StartBackend(false,false,"wall",true); }
    [MenuItem("Fruit Fly/Research/Start Experimental Ceiling Perch Transitions")]
    public static void StartCeilingTransitions() { StartBackend(false,false,"ceiling",true); }
    [MenuItem("Fruit Fly/Research/Start Continuous Wall Flight Perch Lab")]
    public static void StartContinuousWall() { StartBackend(false,false,"wall",true,false,true); }
    [MenuItem("Fruit Fly/Research/Start Continuous Ceiling Flight Perch Lab")]
    public static void StartContinuousCeiling() { StartBackend(false,false,"ceiling",true,false,true); }
    [MenuItem("Fruit Fly/Research/Start Calibrated Translation Backend")]
    public static void StartFastFlightBackend() { StartBackend(true,false,null,false,true); }
    public static void ConfigureWalkingSurface(string surface)
    {
        var viewer = UnityEngine.Object.FindObjectOfType<ResearchViewer>();
        Quaternion rotation = Quaternion.Euler(0, 0, surface == "wall" ? 90 : surface == "ceiling" ? 180 : 0);
        if (viewer) viewer.transform.rotation = rotation;
        var floor = GameObject.Find("Display floor");
        if (floor) floor.transform.rotation = rotation;
    }
    public static void StartWalkingBackend(bool neural, bool calibrated = false, bool perch = false, string surface = "floor", bool optimized = true, bool headStabilization = false, bool upstreamSteering = false)
    { StartBackend(false, neural, walking: true, walkingCalibration: calibrated, walkingPerch: perch, walkingSurface: surface, walkingOptimized: optimized, walkingHead: headStabilization, walkingUpstream: upstreamSteering); }
    private static void StartBackend(bool flight, bool experimental = false, string contactSurface = null, bool transitions = false, bool averaged = false, bool continuous = false, bool walking = false, bool walkingCalibration = false, bool walkingPerch = false, string walkingSurface = "floor", bool walkingOptimized = false, bool walkingHead = false, bool walkingUpstream = false, string unifiedQualification = null, string higherLandingQualification = null)
    {
        StopBackend();
        string project = Path.GetDirectoryName(Application.dataPath);
        string workspace = Directory.GetParent(Directory.GetParent(project).FullName).FullName;
        string python = Path.Combine(workspace, "work", walking ? "neuromechfly-env" : flight ? "flight-env" : "research-env", "Scripts", "python.exe");
        string script = Path.Combine(project, "Research", higherLandingQualification != null ? "serve_higher_landing.py" : unifiedQualification != null ? "serve_unified.py" : walking ? "serve_walking.py" : averaged ? "serve_fast_flight.py" : contactSurface != null ? "serve_adhesion.py" : flight ? "serve_flight.py" : "serve_research.py");
        if (!File.Exists(python)) throw new FileNotFoundException("Workspace research environment not found", python);
        string qualification = higherLandingQualification ?? unifiedQualification;
        backend = Process.Start(new ProcessStartInfo(python, "\"" + script + "\"" + (qualification != null ? " --qualification \"" + qualification + "\"" : "") + (experimental ? " --experimental-neural" : "") +
            (contactSurface != null ? " --surface " + contactSurface : "") + (transitions ? " --transitions" : "") + (continuous ? " --continuous" : "") +
            (walkingCalibration ? " --calibrated-steering" : "") + (walkingPerch ? " --perch-transitions --surface " + walkingSurface : "") + (walkingOptimized ? " --optimized" : "") + (walkingHead ? " --head-stabilization" : "") + (walkingUpstream ? " --upstream-steering" : ""))
        {
            WorkingDirectory = project, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        });
        backend.OutputDataReceived += (_, e) => { if (e.Data != null) UnityEngine.Debug.Log(e.Data); };
        backend.ErrorDataReceived += (_, e) => { if (e.Data != null) UnityEngine.Debug.LogWarning(e.Data); };
        backend.BeginOutputReadLine(); backend.BeginErrorReadLine();
        EditorApplication.quitting -= StopBackend;
        EditorApplication.quitting += StopBackend;
        AssemblyReloadEvents.beforeAssemblyReload -= StopBackend;
        AssemblyReloadEvents.beforeAssemblyReload += StopBackend;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }
    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingPlayMode) StopBackend();
        if (state == PlayModeStateChange.EnteredPlayMode && backend == null)
        {
            var viewer = UnityEngine.Object.FindObjectOfType<ResearchViewer>();
            if (viewer && viewer.unifiedLab)
            {
                bool higherLanding = SessionState.GetBool("FruitFlyJoust.HigherLandingLaunch", false);
                SessionState.EraseBool("FruitFlyJoust.HigherLandingLaunch");
                if (higherLanding) StartHigherLandingBackend(); else StartUnifiedBackend();
                if (SessionState.GetBool(HigherLandingCheck, false))
                {
                    SessionState.EraseBool(HigherLandingCheck);
                    if (!viewer.GetComponent<UnifiedFloorViewerCheck>()) viewer.gameObject.AddComponent<UnifiedFloorViewerCheck>();
                }
            }
        }
    }
    [MenuItem("Fruit Fly/Research/Stop GPU Backend")]
    [MenuItem("Fruit Fly/Research/Stop Trained Flight Backend")]
    public static void StopBackend()
    {
        if (backend == null) return;
        // The owned neural worker exits when its parent's stdin pipe closes.
        if (!backend.HasExited) backend.Kill();
        backend.Dispose(); backend = null;
    }
}
