using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using FruitFlyJoust;

public sealed class RiderPoseLabWindow : EditorWindow
{
    RiderPoseAuthoring authoring;

    [MenuItem("Fruit Fly/Rider Pose Lab/Open Pose Lab")]
    public static void OpenLab(){RiderPoseLabTools.CreateOrOpen();GetWindow<RiderPoseLabWindow>("Rider Pose Lab");}

    void OnEnable(){FindAuthoring();}
    void FindAuthoring(){authoring=Object.FindObjectOfType<RiderPoseAuthoring>();}
    void OnGUI()
    {
        if(!authoring)FindAuthoring();
        EditorGUILayout.LabelField("Mounted Rider Pose",EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Select a bone below, press E for the Rotate tool, and pose it in Scene view. Move Mounted rider root to adjust the whole torso/seat. Save writes Research/rider-authored-pose.json so Codex can read the exact pose.",MessageType.Info);
        if(!authoring){if(GUILayout.Button("Create / open RiderPoseLab"))RiderPoseLabTools.CreateOrOpen();return;}
        if(GUILayout.Button("Select mounted rider root"))Selection.activeTransform=authoring.riderRoot;
        EditorGUILayout.Space();
        foreach(var bone in authoring.editableBones)
        {
            Transform t=authoring.Bone(bone);
            using(new EditorGUI.DisabledScope(!t))if(GUILayout.Button(bone.ToString()))Selection.activeTransform=t;
        }
        EditorGUILayout.Space();
        if(GUILayout.Button("Save pose for Codex",GUILayout.Height(30)))
        { string path=authoring.SavePose();AssetDatabase.Refresh();Debug.Log("RIDER_POSE_SAVED: "+path);ShowNotification(new GUIContent("Pose saved")); }
        if(GUILayout.Button("Reload last saved pose")){Undo.RecordObject(authoring.riderRoot,"Reload rider pose");authoring.LoadPose();SceneView.RepaintAll();}
        EditorGUILayout.SelectableLabel(authoring.AbsoluteOutputPath(),GUILayout.Height(38));
    }
}

public static class RiderPoseLabTools
{
    const string ScenePath="Assets/RiderPoseLab.unity";

    public static void CreateOrOpen()
    {
        if(EditorApplication.isPlaying)EditorApplication.isPlaying=false;
        if(File.Exists(Path.Combine(Path.GetDirectoryName(Application.dataPath),ScenePath)))
        {EditorSceneManager.OpenScene(ScenePath);UpgradeExisting();Frame();return;}
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var root=new GameObject("Rider Pose Lab");
        root.AddComponent<RiderPoseFlyPreview>().Rebuild();
        var prefab=Resources.Load<GameObject>("BorrowedRider");
        var rider=(GameObject)PrefabUtility.InstantiatePrefab(prefab,root.transform);rider.name="Mounted rider root";rider.transform.localPosition=new Vector3(0,-.33f,-.16f);rider.transform.localScale=Vector3.one*.78f;
        var animator=rider.GetComponentInChildren<Animator>();animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;animator.fireEvents=false;animator.Play("Ride",0,0);animator.Update(.1f);animator.enabled=false;
        var authoring=root.AddComponent<RiderPoseAuthoring>();authoring.riderRoot=rider.transform;authoring.animator=animator;
        var floor=GameObject.CreatePrimitive(PrimitiveType.Plane);floor.name="Reference floor";floor.transform.SetParent(root.transform);floor.transform.position=Vector3.down*.7f;floor.transform.localScale=Vector3.one*.35f;
        var light=new GameObject("Pose light").AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.2f;light.transform.rotation=Quaternion.Euler(45,-30,0);
        EditorSceneManager.SaveScene(scene,ScenePath);authoring.SavePose();AssetDatabase.Refresh();Frame();
    }

    static void UpgradeExisting()
    {
        var root=GameObject.Find("Rider Pose Lab");if(!root)return;
        var proxy=root.transform.Find("Thorax reference (do not pose)");if(proxy)Object.DestroyImmediate(proxy.gameObject);
        var seat=root.transform.Find("Mounted rider anchor");
        var authoring=root.GetComponent<RiderPoseAuthoring>();
        if(authoring && authoring.riderRoot)
        {
            authoring.riderRoot.SetParent(root.transform,false);
            authoring.riderRoot.localPosition=new Vector3(0,-.33f,-.16f);
            authoring.riderRoot.localRotation=Quaternion.identity;
            authoring.riderRoot.localScale=Vector3.one*.78f;
        }
        if(seat)Object.DestroyImmediate(seat.gameObject);
        var preview=root.GetComponent<RiderPoseFlyPreview>();if(!preview)preview=root.AddComponent<RiderPoseFlyPreview>();preview.Rebuild();
        EditorSceneManager.MarkSceneDirty(root.scene);EditorSceneManager.SaveScene(root.scene,ScenePath);
        if(authoring)authoring.SavePose();AssetDatabase.Refresh();
    }

    static void Frame()
    {
        EditorApplication.delayCall+=()=>{var a=Object.FindObjectOfType<RiderPoseAuthoring>();if(!a)return;Selection.activeGameObject=a.riderRoot.gameObject;SceneView.lastActiveSceneView?.FrameSelected();};
    }
}
