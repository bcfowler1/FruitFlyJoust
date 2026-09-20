using System.IO;
using UnityEditor;
using UnityEngine;
using FruitFlyJoust;

public sealed class SwordPoseLabWindow : EditorWindow
{
    [MenuItem("Fruit Fly/Sword Pose Lab")]
    static void Open(){GetWindow<SwordPoseLabWindow>("Sword Pose Lab");}
    void OnGUI()
    {
        var combat=FindObjectOfType<RiderCombat>();
        EditorGUILayout.HelpBox("Enter Play mode, dismount, and equip the sword. Adjust the hand-local pose here; Save writes the exact pose used on subsequent runs. Attack buttons preview the three separate motions.",MessageType.Info);
        if(!combat){EditorGUILayout.LabelField("No running RiderCombat scene found.");return;}
        var pose=combat.swordHandPose;
        EditorGUI.BeginChangeCheck();
        pose.position=EditorGUILayout.Vector3Field("Local position",pose.position);
        pose.euler=EditorGUILayout.Vector3Field("Local rotation",pose.euler);
        pose.scale=EditorGUILayout.Slider("Uniform scale",pose.scale,.25f,2f);
        if(EditorGUI.EndChangeCheck())combat.ApplySwordHandPose();
        EditorGUILayout.Space();
        using(new EditorGUILayout.HorizontalScope())
        {
            if(GUILayout.Button("Left → Right"))combat.SwordStrike(RiderCombat.SwordAttack.LeftToRight);
            if(GUILayout.Button("Right → Left"))combat.SwordStrike(RiderCombat.SwordAttack.RightToLeft);
            if(GUILayout.Button("Thrust + Lunge"))combat.SwordStrike(RiderCombat.SwordAttack.Thrust);
        }
        if(GUILayout.Button("Save sword hand pose",GUILayout.Height(28)))Save(combat);
        if(GUILayout.Button("Capture six inspection angles"))Capture(combat);
        Repaint();
    }
    static void Save(RiderCombat combat)
    {
        string json=JsonUtility.ToJson(combat.swordHandPose,true);
        File.WriteAllText("Assets/Resources/SwordHandPose.json",json);
        Directory.CreateDirectory("Research");File.WriteAllText("Research/sword-hand-pose.json",json);
        AssetDatabase.Refresh();Debug.Log("SWORD_HAND_POSE_SAVED");
    }
    static void Capture(RiderCombat combat)
    {
        var renderers=combat.FootAvatar.GetComponentsInChildren<Renderer>(true);
        if(renderers.Length==0)return;
        Bounds bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);
        var go=new GameObject("Sword pose capture camera");var camera=go.AddComponent<Camera>();
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.16f,.19f,.23f);camera.fieldOfView=32;
        var rt=new RenderTexture(700,700,24);camera.targetTexture=rt;
        Vector3[] directions={Vector3.right,Vector3.left,Vector3.forward,Vector3.back,Vector3.up,Vector3.down};
        string[] names={"right","left","front","rear","top","bottom"};Directory.CreateDirectory("Research/SwordPoseViews");
        for(int i=0;i<directions.Length;i++)
        {
            camera.transform.position=bounds.center+directions[i]*Mathf.Max(2,bounds.extents.magnitude*3);
            Vector3 up=Mathf.Abs(Vector3.Dot(directions[i],Vector3.up))>.9f ? Vector3.forward : Vector3.up;
            camera.transform.LookAt(bounds.center,up);camera.Render();RenderTexture.active=rt;
            var image=new Texture2D(700,700,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,700,700),0,0);image.Apply();
            File.WriteAllBytes("Research/SwordPoseViews/"+names[i]+".png",image.EncodeToPNG());DestroyImmediate(image);
        }
        RenderTexture.active=null;rt.Release();DestroyImmediate(rt);DestroyImmediate(go);Debug.Log("SWORD_POSE_SIX_VIEWS_CAPTURED");
    }
}
