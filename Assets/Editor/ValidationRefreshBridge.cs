using System.IO;
using UnityEditor;
using UnityEngine;

// External validation creates a marker outside Assets. Refresh each new marker
// once before starting Play so the check runs against the latest compiled code.
[InitializeOnLoad]
static class ValidationRefreshBridge
{
    static readonly string[] Markers={
        "run-combat-check.once","run-enemy-check.once","run-enemy-lifecycle-check.once",
        "run-surface-check.once","run-plant-check.once"
    };
    static bool pending;
    static double refreshAt;
    const string LastRefreshKey="FruitFlyJoust.ValidationRefreshBridge.LastMarker";

    static ValidationRefreshBridge(){EditorApplication.update+=Poll;}

    static void Poll()
    {
        if(EditorApplication.isCompiling || EditorApplication.isUpdating)return;
        string marker=null;
        string research=Path.GetFullPath(Path.Combine(Application.dataPath,"../Research"));
        foreach(string name in Markers)
        {
            string path=Path.Combine(research,name);
            if(File.Exists(path)){marker=path;break;}
        }
        if(marker==null){pending=false;return;}
        // A queued validation owns the next Play session. Exit an existing manual
        // session so the marker can open its test scene and run unattended.
        if(EditorApplication.isPlaying)
        {
            EditorApplication.isPlaying=false;
            return;
        }
        if(EditorApplication.isPlayingOrWillChangePlaymode)return;
        if(!pending){pending=true;refreshAt=EditorApplication.timeSinceStartup+.35;return;}
        if(EditorApplication.timeSinceStartup<refreshAt)return;
        pending=false;
        string markerStamp=marker+":"+File.GetLastWriteTimeUtc(marker).Ticks;
        if(SessionState.GetString(LastRefreshKey,"")!=markerStamp)
        {
            SessionState.SetString(LastRefreshKey,markerStamp);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            return;
        }
        if(Path.GetFileName(marker)=="run-plant-check.once")
        {
            File.Delete(marker);
            PlantLabTools.BuildIndoorLab();
            return;
        }
        AutoCombatCheck.Run();
    }
}
