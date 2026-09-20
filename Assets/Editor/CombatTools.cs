using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using FruitFlyJoust;

public static class CombatTools
{
    public static void RunBatchEnemyChecks()
    {
        if(!Application.isBatchMode)throw new InvalidOperationException("Use this entry point only for batch verification.");
        DateTime checkStarted=DateTime.UtcNow;
        EditorSceneManager.OpenScene("Assets/CombatEncounter.unity");
        EditorApplication.playModeStateChanged += state => {
            if(state==PlayModeStateChange.EnteredPlayMode)RunEnemyChecks();
            if(state==PlayModeStateChange.EnteredEditMode)
            {
                string report=System.IO.Path.Combine(Application.dataPath,"../Research/enemy-play-evaluation.json");
                bool fresh=System.IO.File.Exists(report) && System.IO.File.GetLastWriteTimeUtc(report)>=checkStarted;
                EditorApplication.Exit(fresh && System.IO.File.ReadAllText(report).Contains("\"status\":\"passed\"") ? 0 : 1);
            }
        };
        EditorApplication.isPlaying=true;
    }
    public static void RunBatchChecks()
    {
        if(!Application.isBatchMode)throw new InvalidOperationException("Use this entry point only for batch verification.");
        DateTime checkStarted=DateTime.UtcNow;
        EditorSceneManager.OpenScene("Assets/CombatLab.unity");
        EditorApplication.playModeStateChanged += state => {
            if(state==PlayModeStateChange.EnteredPlayMode)RunChecks();
            if(state==PlayModeStateChange.EnteredEditMode)
            {
                string report=System.IO.Path.Combine(Application.dataPath,"../Research/combat-play-evaluation.json");
                bool fresh=System.IO.File.Exists(report) && System.IO.File.GetLastWriteTimeUtc(report)>=checkStarted;
                EditorApplication.Exit(fresh && System.IO.File.ReadAllText(report).Contains("\"status\":\"passed\"") ? 0 : 1);
            }
        };
        EditorApplication.isPlaying=true;
    }
    [MenuItem("Fruit Fly/Combat/Run Rider Pose Checks")]
    public static void RunPoseChecks()
    {
        if(!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play first.");
        new GameObject("Rider pose checks").AddComponent<RiderPoseCheck>();
    }
    [MenuItem("Fruit Fly/Combat/Run Scientific Clock Checks")]
    public static void RunClockChecks()
    {
        var viewer=UnityEngine.Object.FindObjectOfType<ResearchViewer>();
        if (!EditorApplication.isPlaying || !viewer || !viewer.walkingLab) throw new InvalidOperationException("Enter WalkingLab first.");
        new GameObject("Scientific clock checks").AddComponent<ScientificClockCheck>();
    }
    [MenuItem("Fruit Fly/Combat/Run Scientific Body Checks")]
    public static void RunScientificChecks()
    {
        var viewer=UnityEngine.Object.FindObjectOfType<ResearchViewer>();
        if (!EditorApplication.isPlaying || !viewer || !viewer.walkingLab)
            throw new InvalidOperationException("Enter floor WalkingLab with perch transitions enabled first.");
        new GameObject("Scientific combat checks").AddComponent<ScientificCombatCheck>();
    }
    [MenuItem("Fruit Fly/Combat/Run Enemy Play Checks")]
    public static void RunEnemyChecks()
    {
        var viewer=UnityEngine.Object.FindObjectOfType<ResearchViewer>();
        if (!EditorApplication.isPlaying || (!UnityEngine.Object.FindObjectOfType<CombatOpponent>() && (!viewer || !viewer.walkingLab)))
            throw new InvalidOperationException("Enter CombatEncounter Play first.");
        new GameObject("Bounded enemy play checks").AddComponent<EnemyPlayCheck>();
    }
    [MenuItem("Fruit Fly/Combat/Create Enemy Encounter Scene")]
    public static void CreateEncounter()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play first.");
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var scene = EditorSceneManager.OpenScene("Assets/CombatLab.unity");
        int index = 0;
        foreach (var target in UnityEngine.Object.FindObjectsOfType<CombatTarget>())
        {
            target.gameObject.name = index % 2 == 0 ? "Enemy swordsman" : "Enemy archer";
            var feet = target.gameObject.AddComponent<CharacterController>();
            feet.height = 2; feet.radius = .35f;
            var opponent = target.gameObject.AddComponent<CombatOpponent>();
            opponent.style = index++ % 2 == 0 ? CombatOpponent.Style.Swordsman : CombatOpponent.Style.Archer;
        }
        EditorSceneManager.SaveScene(scene, "Assets/CombatEncounter.unity");
    }
    [MenuItem("Fruit Fly/Combat/Run Play Checks")]
    public static void RunChecks()
    {
        if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter CombatLab Play first.");
        if (!UnityEngine.Object.FindObjectOfType<RiderCombat>()) throw new InvalidOperationException("Open CombatLab first.");
        new GameObject("Bounded combat play check").AddComponent<CombatPlayCheck>();
    }
    [MenuItem("Fruit Fly/Combat/Create Combat Practice Scene")]
    public static void CreateScene()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play before creating combat practice.");
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var scene = EditorSceneManager.OpenScene("Assets/Practice.unity");
        var fly = UnityEngine.Object.FindObjectOfType<FlyMotor>();
        var camera = UnityEngine.Object.FindObjectOfType<RiderCamera>();
        var combat = fly.gameObject.AddComponent<RiderCombat>();
        combat.fly = fly; combat.view = camera; combat.course = UnityEngine.Object.FindObjectOfType<PracticeCourse>();
        combat.mountedVisual = fly.bodyVisual.Find("Rider");
        combat.riderMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Generated/Rider.mat");
        combat.weaponMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Generated/Hoops.mat");
        for (int i = 0; i < 5; i++)
        {
            var target = GameObject.CreatePrimitive(PrimitiveType.Capsule); target.name = "Combat dummy " + (i+1);
            target.transform.position = new Vector3((i-2)*3, 1, -12);
            target.transform.localScale = new Vector3(.75f, 1, .75f);
            target.GetComponent<Renderer>().sharedMaterial = combat.weaponMaterial;
            target.AddComponent<CombatTarget>();
        }
        EditorSceneManager.SaveScene(scene, "Assets/CombatLab.unity");
        Debug.Log("COMBAT_SCENE_READY: separate gameplay scaffold, no scientific neural combat coupling");
    }
}
