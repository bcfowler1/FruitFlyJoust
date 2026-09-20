using System.IO;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
static class AutoSurfaceCheck
{
    static AutoSurfaceCheck(){EditorApplication.delayCall+=Run;}
    static void Run()
    {
        string marker=Path.GetFullPath(Path.Combine(Application.dataPath,"../Research/run-surface-check.once"));
        if(!File.Exists(marker) || EditorApplication.isPlayingOrWillChangePlaymode)return;
        File.Delete(marker);
        try { SurfaceChecks.Run();File.WriteAllText(Path.Combine(Application.dataPath,"../Research/surface-traversal-evaluation.json"),
            "{\"status\":\"passed\",\"checks\":\"floor-wall, wall-ceiling, table-edge-down\",\"scope\":\"fast gameplay geometric traversal; not validated biological locomotion\"}"); }
        catch(System.Exception e) { File.WriteAllText(Path.Combine(Application.dataPath,"../Research/surface-traversal-evaluation.json"),
            "{\"status\":\"failed\",\"error\":\""+e.Message.Replace("\\","\\\\").Replace("\"","\\\"")+"\"}");throw; }
    }
}
