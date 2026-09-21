using System.IO;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class LancePrefabBuilder
{
    const string Source="Assets/Resources/Weapons/Fly Lance Source.blend";
    const string Prefab="Assets/Resources/Weapons/Fly Lance.prefab";
    static LancePrefabBuilder(){EditorApplication.delayCall+=BuildIfOutdated;}

    [MenuItem("Fruit Fly/Combat/Rebuild Editable Lance Prefab")]
    public static void Rebuild(){Build(true);}

    static void BuildIfOutdated()
    {
        string source=Path.GetFullPath(Source),prefab=Path.GetFullPath(Prefab);
        Build(!File.Exists(prefab) || File.GetLastWriteTimeUtc(source)>File.GetLastWriteTimeUtc(prefab));
    }
    static void Build(bool required)
    {
        if(!required)return;
        var model=AssetDatabase.LoadAssetAtPath<GameObject>(Source);if(!model)return;
        var instance=(GameObject)PrefabUtility.InstantiatePrefab(model);instance.name="Fly Lance";
        foreach(var collider in instance.GetComponentsInChildren<Collider>())Object.DestroyImmediate(collider);
        PrefabUtility.SaveAsPrefabAsset(instance,Prefab);Object.DestroyImmediate(instance);
        AssetDatabase.SaveAssets();Debug.Log("LANCE_PREFAB_READY: "+Prefab);
    }
}
