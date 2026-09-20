using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using FruitFlyJoust;

[InitializeOnLoad]
public sealed class SimulationControlWindow : EditorWindow
{
    private const string Pending = "FruitFlyJoust.PendingMode";
    private const string Launch = "FruitFlyJoust.LaunchMode";
    private const string ReferencePreference = "FruitFlyJoust.BiologicalReference";
    private int biologicalReference;
    [SerializeField] private bool walkingCalibration = true, walkingPerch;
    [SerializeField] private bool walkingOptimized = true;
    [SerializeField] private bool walkingHead;
    [SerializeField] private bool walkingUpstream;
    [SerializeField] private int walkingSurface;
    private Vector2 scroll;
    [SerializeField] private bool physical = true, neural = true, contact;
    [SerializeField] private int surface;
    [SerializeField] private bool transitions;
    [SerializeField] private bool continuous;
    [SerializeField] private int fastScene;
    private float influence = 1, grip = 1;
    private string message;
    static SimulationControlWindow()
    {
        EditorApplication.playModeStateChanged += Changed;
    }
    [MenuItem("Fruit Fly/Simulation Controls")]
    public static void Open() { GetWindow<SimulationControlWindow>("Fly Systems").Show(); }
    [MenuItem("Fruit Fly/Run Brain and Live Body")]
    public static void RunBrainAndLiveBody()
    {
        if(!EditorApplication.isPlaying && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
        EditorPrefs.SetInt(ReferencePreference,1);
        EditorPrefs.SetFloat("FruitFlyJoust.NeuralInfluence",1);
        EditorPrefs.SetFloat("FruitFlyJoust.GripStrength",1);
        SimulationControls.NeuralInfluence=SimulationControls.GripStrength=1;
        SessionState.SetString(Pending,"neuralwalking");
        SessionState.SetBool("FruitFlyJoust.WalkingPerch",true);
        SessionState.SetBool("FruitFlyJoust.WalkingOptimized",true);
        SessionState.SetBool("FruitFlyJoust.WalkingHead",true);
        SessionState.SetBool("FruitFlyJoust.WalkingUpstream",true);
        SessionState.SetBool("FruitFlyJoust.WalkingCalibration",true);
        SessionState.SetString("FruitFlyJoust.WalkingSurface","floor");
        foreach(var window in Resources.FindObjectsOfTypeAll<SimulationControlWindow>())
        {
            window.biologicalReference=1;window.neural=true;window.walkingPerch=true;
            window.walkingOptimized=window.walkingHead=window.walkingCalibration=true;
            window.walkingUpstream=true;window.walkingSurface=0;window.influence=window.grip=1;window.Repaint();
        }
        if(EditorApplication.isPlaying)EditorApplication.isPlaying=false;
        else Prepare();
    }
    void OnEnable()
    {
        minSize = new Vector2(560, 480);
        biologicalReference = Mathf.Clamp(EditorPrefs.GetInt(ReferencePreference, 1), 0, 1);
        influence = EditorPrefs.GetFloat("FruitFlyJoust.NeuralInfluence", 1);
        grip = EditorPrefs.GetFloat("FruitFlyJoust.GripStrength", 1);
        SyncSliders();
    }
    void SyncSliders()
    {
        SimulationControls.NeuralInfluence = influence;
        SimulationControls.GripStrength = grip;
        EditorPrefs.SetFloat("FruitFlyJoust.NeuralInfluence", influence);
        EditorPrefs.SetFloat("FruitFlyJoust.GripStrength", grip);
    }
    void OnGUI()
    {
        float previousLabelWidth = EditorGUIUtility.labelWidth;
        EditorGUIUtility.labelWidth = 245;
        scroll = EditorGUILayout.BeginScrollView(scroll);
        EditorGUILayout.LabelField("Fly simulation systems", EditorStyles.boldLabel);
        if(GUILayout.Button("Run brain and live body",GUILayout.Height(32)))RunBrainAndLiveBody();
        EditorGUILayout.LabelField("Biological reference");
        EditorGUI.BeginChangeCheck();
        biologicalReference = GUILayout.Toolbar(biologicalReference, new[] { "FlyWire + FlyBody", "Eon / NeuroMechFly" });
        if (EditorGUI.EndChangeCheck()) EditorPrefs.SetInt(ReferencePreference, biologicalReference);
        if (biologicalReference == 1)
        {
            EditorGUILayout.HelpBox("NEUROMECHFLY WALKING LAB: upstream detailed body and hybrid gait controller. Experimental FlyWire DNa01/DNa02 spike interface uses synthetic cues and body-speed feedback. This is our reconstruction of public components, not Eon's released brain/body controller.", MessageType.Warning);
            neural = EditorGUILayout.Toggle("Full connectome (experimental)", neural);
            EditorGUILayout.HelpBox("Brain spikes influence steering. Legs use the hybrid gait controller; head uses trained stabilization; assisted launch and landing use engineering control. The renderer displays actual body geometry without decorative animation.",MessageType.Info);
            walkingPerch = EditorGUILayout.Toggle("Walking / perch / assisted launch", walkingPerch);
            walkingOptimized = EditorGUILayout.Toggle("Cache repeated body observations", walkingOptimized);
            if (walkingPerch) walkingSurface = EditorGUILayout.Popup("Contact surface", walkingSurface, new[] { "Floor", "Wall", "Ceiling" });
            using (new EditorGUI.DisabledScope(walkingPerch && walkingSurface != 0))
                walkingHead = EditorGUILayout.Toggle("Trained head stabilization (floor)", walkingHead);
            using (new EditorGUI.DisabledScope(walkingPerch && walkingSurface != 0))
                walkingCalibration = EditorGUILayout.Toggle("Calibrated floor steering", walkingCalibration);
            using (new EditorGUI.DisabledScope(!neural || !walkingHead || !walkingCalibration || (walkingPerch && walkingSurface != 0)))
                walkingUpstream = EditorGUILayout.Toggle("Upstream PFL3 steering (experimental)", walkingUpstream);
            if(walkingUpstream) EditorGUILayout.HelpBox("Network-dependent PFL3 to descending-neuron steering. Synthetic input with smoothed measured yaw; hybrid legs. Nine short loaded-body cases and live pause/perch/remount passed. Not biological validation or neural wing landing.",MessageType.Info);
            EditorGUILayout.HelpBox("Floor steering is qualified for gait drive 0.5–0.9; spur/brake outside that range uses raw engineering drive. NeuroMechFly contact handover keeps the same body. Launch uses external forces, not a wing policy. Apply restarts the lab.", MessageType.Info);
            EditorGUI.BeginChangeCheck();
            using (new EditorGUI.DisabledScope(!neural))
                influence = EditorGUILayout.Slider("Neural steering influence", influence, 0, 1);
            if (EditorGUI.EndChangeCheck()) SyncSliders();
            using (new EditorGUI.DisabledScope(!walkingPerch))
            {
                EditorGUI.BeginChangeCheck();
                grip = EditorGUILayout.Slider("Claw grip strength", grip, 0, 1);
                if (EditorGUI.EndChangeCheck()) SyncSliders();
            }
            EditorGUILayout.LabelField("Rider load: one third of this body model's mass");
            EditorGUILayout.LabelField("Wing-driven flight remains in FlyBody.");
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox("QUALIFIED HIGHER LANDING DEMONSTRATION: automated .25 cm launch, wing landing, six-claw hold and native walking. Engineering stance bridge; no live reins, combat input or connectome-derived limb controller.", MessageType.Info);
            if (GUILayout.Button("Run qualified higher landing demonstration", GUILayout.Height(32)))
            {
                if (!EditorApplication.isPlaying && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                { EditorGUILayout.EndScrollView(); EditorGUIUtility.labelWidth = previousLabelWidth; return; }
                SessionState.SetString(Pending, "higherlanding");
                if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
                else Prepare();
            }
            if (GUILayout.Button("Apply walking mode and run", GUILayout.Height(32)))
            {
                if (!EditorApplication.isPlaying && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                { EditorGUILayout.EndScrollView(); EditorGUIUtility.labelWidth = previousLabelWidth; return; }
                SessionState.SetString(Pending, neural ? "neuralwalking" : "walking");
                SessionState.SetBool("FruitFlyJoust.WalkingPerch", walkingPerch);
                SessionState.SetBool("FruitFlyJoust.WalkingOptimized", walkingOptimized);
                SessionState.SetBool("FruitFlyJoust.WalkingUpstream", walkingUpstream && neural && walkingHead && walkingCalibration && (!walkingPerch || walkingSurface == 0));
                SessionState.SetBool("FruitFlyJoust.WalkingHead", walkingHead && (!walkingPerch || walkingSurface == 0));
                SessionState.SetBool("FruitFlyJoust.WalkingCalibration", walkingCalibration && (!walkingPerch || walkingSurface == 0));
                SessionState.SetString("FruitFlyJoust.WalkingSurface", !walkingPerch || walkingSurface == 0 ? "floor" : walkingSurface == 1 ? "wall" : "ceiling");
                if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
                else Prepare();
            }
            EditorGUILayout.LabelField(EditorApplication.isPlaying ? "Running: " + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name : "Stopped");
            if (GUILayout.Button("Stop simulation")) { ResearchTools.StopBackend(); EditorApplication.isPlaying = false; }
            EditorGUILayout.EndScrollView(); EditorGUIUtility.labelWidth = previousLabelWidth; return;
        }
        EditorGUILayout.HelpBox("AVAILABLE: our FlyWire v783 brain and FlyBody body integration. Neural flight and turning remain experimental.", MessageType.Info);
        EditorGUILayout.HelpBox("Choose a mode, then apply. Applying restarts the scene and resets the encounter.", MessageType.Info);
        physical = EditorGUILayout.Toggle("Detailed MuJoCo body", physical);
        if (!physical) fastScene = EditorGUILayout.Popup("Fast mode",fastScene,new[] { "Combat prototype", "Empirical translation lab" });
        using (new EditorGUI.DisabledScope(!physical))
        {
            contact = EditorGUILayout.Toggle("Six-claw contact experiment", contact);
            if (contact)
            {
                surface = EditorGUILayout.Popup("Surface", surface, new[] { "Wall", "Ceiling" });
                transitions = EditorGUILayout.Toggle("Experimental approach / launch", transitions);
                if (transitions) continuous = EditorGUILayout.Toggle("Continuous surface-relative cruise", continuous);
            }
        }
        using (new EditorGUI.DisabledScope(!physical || contact))
            neural = EditorGUILayout.Toggle("Full connectome (experimental)", neural);
        EditorGUILayout.Space();
        EditorGUI.BeginChangeCheck();
        using (new EditorGUI.DisabledScope(!physical || contact || !neural))
            influence = EditorGUILayout.Slider("Neural wing influence", influence, 0, 1);
        using (new EditorGUI.DisabledScope(!physical || !contact))
            grip = EditorGUILayout.Slider("Claw grip strength", grip, 0, 1);
        if (EditorGUI.EndChangeCheck()) SyncSliders();
        EditorGUILayout.HelpBox(!physical ? fastScene == 1 ? "FAST TRANSLATION LAB: fitted to loaded flight references. Held-out RMS velocity error 1.9 cm/s. Yaw/reins remain uncalibrated; no collision, landing or brain simulation." : "FAST COMBAT PROTOTYPE: gameplay controller and geometric grip. No connectome. Not calibrated against the reference model." : contact ? transitions ? "EXPERIMENTAL PERCH TRANSITIONS: averaged flight forces hand over to real claws. Fixed leg targets; not wing-resolved or neural landing control." : "CONTACT LAB: full legs and contact forces, fixed leg targets. Separate from flight and combat." : neural ? "FULL BRAIN + FLIGHT: actual connectome, synthetic excitation and uncalibrated wing residuals over the trained stabilizer. Runs slowly." : "PHYSICAL FLIGHT: trained controller and detailed body. Brain simulation is off. Runs slowly.", MessageType.Warning);
        EditorGUILayout.LabelField("Rider load: one third of fly mass");
        if (physical && contact && transitions && continuous) EditorGUILayout.HelpBox("Continuous cruise uses engineering yaw and flight forces near one infinite flat surface. Landing aligns the fixed leg stance before real claw handover. Excursion limit: 5 cm. No trained wing control, neural control or combat in this lab.", MessageType.Warning);
        EditorGUILayout.LabelField("Sliders take effect live in the matching lab.");
        EditorGUILayout.LabelField("Zero neural influence removes wing residuals;");
        EditorGUILayout.LabelField("the full brain still runs. Toggle it off to save work.");
        if (GUILayout.Button("Apply mode and run", GUILayout.Height(32)))
        {
            string mode = !physical ? "fast" : contact ? surface == 0 ? "wall" : "ceiling" : neural ? "neural" : "flight";
            if (physical && contact && transitions) mode = "perch" + mode;
            if (physical && contact && transitions && continuous) mode = "continuous" + mode;
            if (!physical && fastScene == 1) mode = "averaged";
            if (!EditorApplication.isPlaying && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) { EditorGUIUtility.labelWidth = previousLabelWidth; return; }
            SessionState.SetString(Pending, mode);
            message = "Switching to " + mode + "…";
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
            else Prepare();
        }
        if (EditorApplication.isPlaying) EditorGUILayout.LabelField("Running: " + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
        else EditorGUILayout.LabelField(EditorApplication.isPlayingOrWillChangePlaymode ? message ?? "Starting…" : "Stopped");
        if (GUILayout.Button("Stop simulation")) { ResearchTools.StopBackend(); EditorApplication.isPlaying = false; }
        EditorGUILayout.EndScrollView();
        EditorGUIUtility.labelWidth = previousLabelWidth;
    }
    private static void Prepare()
    {
        string mode = SessionState.GetString(Pending, "");
        if (string.IsNullOrEmpty(mode)) return;
        SessionState.EraseString(Pending);
        ResearchTools.StopBackend();
        string scene = mode == "higherlanding" ? "UnifiedFloorLab" : mode.EndsWith("walking") ? "WalkingLab" : mode == "fast" ? "CombatEncounter" : mode == "wall" || mode == "ceiling" || mode.StartsWith("perch") || mode.StartsWith("continuous") ? "ResearchLab" : "FlightLab";
        try
        {
            if (scene == "WalkingLab" && !System.IO.File.Exists("Assets/WalkingLab.unity")) ResearchTools.CreateWalkingScene();
            else EditorSceneManager.OpenScene("Assets/" + scene + ".unity");
            if (scene == "WalkingLab") ResearchTools.ConfigureWalkingSurface(SessionState.GetString("FruitFlyJoust.WalkingSurface", "floor"));
            SessionState.SetBool("FruitFlyJoust.HigherLandingLaunch", mode == "higherlanding");
            SessionState.SetBool("FruitFlyJoust.HigherLandingViewerCheck", mode == "higherlanding");
            SessionState.SetString(Launch, mode);
            EditorApplication.isPlaying = true;
        }
        catch (System.Exception error) { SessionState.EraseString(Launch); Debug.LogException(error); }
    }
    private static void Changed(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += Prepare;
        if (state != PlayModeStateChange.EnteredPlayMode) return;
        SimulationControls.NeuralInfluence = EditorPrefs.GetFloat("FruitFlyJoust.NeuralInfluence", 1);
        SimulationControls.GripStrength = EditorPrefs.GetFloat("FruitFlyJoust.GripStrength", 1);
        string mode = SessionState.GetString(Launch, ""); SessionState.EraseString(Launch);
        if (mode.EndsWith("walking")) ResearchTools.StartWalkingBackend(mode == "neuralwalking",
            SessionState.GetBool("FruitFlyJoust.WalkingCalibration", false), SessionState.GetBool("FruitFlyJoust.WalkingPerch", false),
            SessionState.GetString("FruitFlyJoust.WalkingSurface", "floor"), SessionState.GetBool("FruitFlyJoust.WalkingOptimized", true), SessionState.GetBool("FruitFlyJoust.WalkingHead", false), SessionState.GetBool("FruitFlyJoust.WalkingUpstream", false));
        else if (mode == "neural") ResearchTools.StartNeuralFlightBackend();
        else if (mode == "averaged") ResearchTools.StartFastFlightBackend();
        else if (mode == "flight") ResearchTools.StartFlightBackend();
        else if (mode == "wall") ResearchTools.StartWallBackend();
        else if (mode == "ceiling") ResearchTools.StartCeilingBackend();
        else if (mode == "perchwall") ResearchTools.StartWallTransitions();
        else if (mode == "perchceiling") ResearchTools.StartCeilingTransitions();
        else if (mode == "continuousperchwall") ResearchTools.StartContinuousWall();
        else if (mode == "continuousperchceiling") ResearchTools.StartContinuousCeiling();
    }
}
