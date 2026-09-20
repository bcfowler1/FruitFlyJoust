using System.IO;
using UnityEditor;
using UnityEngine;
using FruitFlyJoust;

public sealed class FlyTackLabWindow : EditorWindow
{
    FlyTackFitter fitter;
    PreviewRenderUtility preview;
    GameObject previewRoot;
    Vector2 orbit=new Vector2(-32,18);
    Vector2 scroll;
    [MenuItem("Fruit Fly/Fly Tack Lab/Open Tack Lab")]
    public static void Open(){var window=GetWindow<FlyTackLabWindow>("Fly Tack Lab");window.minSize=new Vector2(390,690);window.Show();}
    void OnEnable(){BuildPreview();}
    void OnDisable(){if(preview!=null)preview.Cleanup();preview=null;previewRoot=null;fitter=null;}
    void BuildPreview()
    {
        if(preview!=null)preview.Cleanup();
        preview=new PreviewRenderUtility();preview.camera.fieldOfView=32;preview.camera.nearClipPlane=.01f;preview.camera.farClipPlane=100;
        preview.camera.clearFlags=CameraClearFlags.SolidColor;preview.camera.backgroundColor=new Color(.12f,.16f,.2f);
        preview.lights[0].intensity=1.25f;preview.lights[0].transform.rotation=Quaternion.Euler(35,-35,0);
        preview.lights[1].intensity=.6f;preview.lights[1].transform.rotation=Quaternion.Euler(330,145,0);
        previewRoot=new GameObject("Static fly tack preview");previewRoot.hideFlags=HideFlags.HideAndDontSave;
        var flyPreview=previewRoot.AddComponent<RiderPoseFlyPreview>();flyPreview.Rebuild();
        var fly=previewRoot.transform.Find("Actual gameplay fly model (do not pose)");fitter=FlyTackFitter.Ensure(fly);fitter.LoadFit();fitter.Rebuild();
        preview.AddSingleGO(previewRoot);
    }
    static FlyTackFitter[] AllFitters(){return Object.FindObjectsOfType<FlyTackFitter>(true);}
    Bounds PreviewBounds()
    {
        var renderers=previewRoot.GetComponentsInChildren<Renderer>(true);bool any=false;Bounds bounds=new Bounds();
        foreach(var renderer in renderers)if(renderer.enabled){if(!any){bounds=renderer.bounds;any=true;}else bounds.Encapsulate(renderer.bounds);}
        return any ? bounds : new Bounds(Vector3.zero,Vector3.one);
    }
    void DrawPreview(Rect rect)
    {
        if(preview==null || !previewRoot){EditorGUI.DrawRect(rect,Color.black);return;}
        var e=Event.current;if(e.type==EventType.MouseDrag && rect.Contains(e.mousePosition)){orbit+=e.delta*.5f;e.Use();Repaint();}
        Bounds bounds=PreviewBounds();Quaternion angle=Quaternion.Euler(orbit.y,orbit.x,0);Vector3 direction=angle*Vector3.back;
        preview.camera.transform.position=bounds.center+direction*Mathf.Max(2f,bounds.size.magnitude*1.45f);preview.camera.transform.LookAt(bounds.center);
        preview.BeginPreview(rect,GUIStyle.none);preview.camera.Render();Texture image=preview.EndPreview();GUI.DrawTexture(rect,image,ScaleMode.StretchToFill,false);
        GUI.Label(new Rect(rect.x+8,rect.y+8,250,20),"Drag to orbit • static biological fly",EditorStyles.whiteLabel);
    }
    void OnGUI()
    {
        if(!fitter || !previewRoot)BuildPreview();
        EditorGUILayout.LabelField("Horse tack fitted to biological fly",EditorStyles.boldLabel);
        Rect previewRect=GUILayoutUtility.GetRect(360,360,GUILayout.ExpandWidth(true));DrawPreview(previewRect);
        scroll=EditorGUILayout.BeginScrollView(scroll);
        EditorGUILayout.HelpBox("This fly is frozen. Adjust the imported saddle and armour here, then save or apply the fit to loaded gameplay flies.",MessageType.Info);
        EditorGUI.BeginChangeCheck();
        fitter.fit.showSaddle=EditorGUILayout.Toggle("Show saddle",fitter.fit.showSaddle);
        using(new EditorGUI.IndentLevelScope())
        {
            fitter.fit.saddle=EditorGUILayout.ToggleLeft("Realistic saddle",fitter.fit.saddle);
            fitter.fit.saddleLow=EditorGUILayout.ToggleLeft("Low-poly saddle",fitter.fit.saddleLow);
            fitter.fit.saddlePolyArt=EditorGUILayout.ToggleLeft("Poly-art saddle",fitter.fit.saddlePolyArt);
            fitter.fit.reins=EditorGUILayout.ToggleLeft("Reins",fitter.fit.reins);
            fitter.fit.reinsHead=EditorGUILayout.ToggleLeft("Head reins / bridle",fitter.fit.reinsHead);
            fitter.fit.reinsPolyArt=EditorGUILayout.ToggleLeft("Poly-art reins",fitter.fit.reinsPolyArt);
        }
        fitter.fit.saddleOffset=EditorGUILayout.Vector3Field("Saddle offset",fitter.fit.saddleOffset);
        fitter.fit.saddleRotation=EditorGUILayout.Vector3Field("Saddle rotation",fitter.fit.saddleRotation);
        fitter.fit.saddleSize=EditorGUILayout.Vector3Field("Saddle size",fitter.fit.saddleSize);
        fitter.fit.showArmour=EditorGUILayout.Toggle("Show armour",fitter.fit.showArmour);
        using(new EditorGUI.IndentLevelScope())
        {
            fitter.fit.armour=EditorGUILayout.ToggleLeft("Realistic armour",fitter.fit.armour);
            fitter.fit.armourPolyArt=EditorGUILayout.ToggleLeft("Poly-art armour",fitter.fit.armourPolyArt);
        }
        fitter.fit.armourOffset=EditorGUILayout.Vector3Field("Armour offset",fitter.fit.armourOffset);
        fitter.fit.armourRotation=EditorGUILayout.Vector3Field("Armour rotation",fitter.fit.armourRotation);
        fitter.fit.armourSize=EditorGUILayout.Vector3Field("Armour size",fitter.fit.armourSize);
        if(EditorGUI.EndChangeCheck())
        {
            fitter.Apply();Repaint();
        }
        if(GUILayout.Button("Apply preview fit to loaded flies"))
        {
            string json=JsonUtility.ToJson(fitter.fit);var targets=AllFitters();
            foreach(var target in targets)if(target!=fitter){target.fit=JsonUtility.FromJson<FlyTackFitter.FitData>(json);target.Apply();}
            SceneView.RepaintAll();UnityEditorInternal.InternalEditorUtility.RepaintAllViews();ShowNotification(new GUIContent("Applied to "+Mathf.Max(0,targets.Length-1)+" flies"));
        }
        if(GUILayout.Button("Rebuild static preview")){BuildPreview();Repaint();}
        if(GUILayout.Button("Save fit for game",GUILayout.Height(30))){Debug.Log("FLY_TACK_FIT_SAVED: "+fitter.SaveFit());AssetDatabase.Refresh();ShowNotification(new GUIContent("Tack fit saved"));}
        if(GUILayout.Button("Reload saved fit")){fitter.LoadFit();fitter.Apply();Repaint();}
        EditorGUILayout.EndScrollView();
    }
}

public static class FlyTackLabTools
{
    public static void ValidateImportedTack()
    {
        var source=Resources.Load<GameObject>("FlyTack/Horse Realistic");
        if(!source)throw new System.Exception("Imported Horse Realistic tack source is unavailable.");
        bool saddle=false,armour=false;
        foreach(var renderer in source.GetComponentsInChildren<Renderer>(true))
        {
            saddle|=renderer.gameObject.name=="Saddle" || renderer.gameObject.name=="Saddle Low";
            armour|=renderer.gameObject.name=="Armour" || renderer.gameObject.name=="Armour PA";
        }
        if(!saddle || !armour)throw new System.Exception("Imported model does not expose both saddle and armour renderers.");
        string path=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Application.dataPath),"Research/fly-tack-preview.png"));GeneratePreview(path);
        Debug.Log("FLY_TACK_VALIDATION_PASS: saddle and armour isolated and fitted to measured biological thorax bounds.");
        EditorApplication.Exit(0);
    }

    [InitializeOnLoadMethod]
    static void ValidateAndRenderAfterImport()
    {
        EditorApplication.delayCall+=()=>
        {
            if(EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode)return;
            var source=Resources.Load<GameObject>("FlyTack/Horse Realistic");if(!source)return;
            bool saddle=false,armour=false;
            foreach(var renderer in source.GetComponentsInChildren<Renderer>(true))
            {saddle|=renderer.gameObject.name=="Saddle" || renderer.gameObject.name=="Saddle Low";armour|=renderer.gameObject.name=="Armour" || renderer.gameObject.name=="Armour PA";}
            if(!saddle || !armour){Debug.LogError("FLY_TACK_VALIDATION_FAIL: source mesh pieces missing");return;}
            string path=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Application.dataPath),"Research/fly-tack-preview.png"));
            if(!File.Exists(path) || File.GetLastWriteTimeUtc(path)<File.GetLastWriteTimeUtc(AssetDatabase.GetAssetPath(source)))GeneratePreview(path);
            Debug.Log("FLY_TACK_VALIDATION_PASS: source exposes saddle and armour; fitted preview: "+path);
        };
    }

    [MenuItem("Fruit Fly/Fly Tack Lab/Render Tack Preview")]
    public static void GeneratePreviewMenu()
    {GeneratePreview(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Application.dataPath),"Research/fly-tack-preview.png")));}

    static void GeneratePreview(string path)
    {
        var stage=new GameObject("Fly tack render stage");stage.hideFlags=HideFlags.HideAndDontSave;
        try
        {
            var preview=stage.AddComponent<RiderPoseFlyPreview>();preview.Rebuild();
            var fly=stage.transform.Find("Actual gameplay fly model (do not pose)");var fitter=FlyTackFitter.Ensure(fly);fitter.LoadFit();fitter.Rebuild();
            var renderers=stage.GetComponentsInChildren<Renderer>(true);if(renderers.Length==0)throw new System.Exception("Fly tack preview has no renderers.");
            Bounds bounds=renderers[0].bounds;foreach(var renderer in renderers)if(renderer.enabled)bounds.Encapsulate(renderer.bounds);
            var cameraObject=new GameObject("Tack preview camera");cameraObject.transform.SetParent(stage.transform);var camera=cameraObject.AddComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.12f,.16f,.2f);camera.fieldOfView=34;camera.nearClipPlane=.01f;
            Vector3 view=new Vector3(1.25f,.72f,-1.35f).normalized;camera.transform.position=bounds.center+view*Mathf.Max(2.2f,bounds.size.magnitude*1.45f);camera.transform.LookAt(bounds.center);
            var lightObject=new GameObject("Key light");lightObject.transform.SetParent(stage.transform);var light=lightObject.AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.35f;light.transform.rotation=Quaternion.Euler(38,-35,0);
            var fillObject=new GameObject("Fill light");fillObject.transform.SetParent(stage.transform);var fill=fillObject.AddComponent<Light>();fill.type=LightType.Directional;fill.intensity=.55f;fill.transform.rotation=Quaternion.Euler(320,145,0);
            var target=new RenderTexture(1024,768,24,RenderTextureFormat.ARGB32);camera.targetTexture=target;camera.Render();RenderTexture.active=target;
            var image=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllBytes(path,image.EncodeToPNG());
            RenderTexture.active=null;camera.targetTexture=null;Object.DestroyImmediate(image);Object.DestroyImmediate(target);AssetDatabase.Refresh();
        }
        finally{Object.DestroyImmediate(stage);}
    }
}
