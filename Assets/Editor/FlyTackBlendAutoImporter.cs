using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class FlyTackBlendAutoImporter
{
    struct Job
    {
        public string blend;
        public string fbx;
        public Job(string blendPath,string fbxPath){blend=blendPath;fbx=fbxPath;}
    }

    static readonly Queue<Job> pending=new Queue<Job>();
    static Process process;
    static Job active;
    static double nextCheck;

    static string ProjectRoot=>Path.GetDirectoryName(Application.dataPath);
    static string Blender
    {
        get
        {
            string[] versions={"5.2","5.0","4.5"};
            foreach(string version in versions)
            {
                string path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Blender Foundation","Blender "+version,"blender.exe");
                if(File.Exists(path))return path;
            }
            return null;
        }
    }

    static FlyTackBlendAutoImporter(){EditorApplication.update+=Update;}

    [MenuItem("Fruit Fly/Fly Tack Lab/Sync Blender Tack Now")]
    public static void SyncNow()
    {
        QueueIfNeeded("Fly Saddle",true);
        QueueIfNeeded("Fly Armor",true);
        if(process==null)StartNext();
    }

    static void Update()
    {
        if(process!=null)
        {
            if(!process.HasExited)return;
            int exitCode=process.ExitCode;
            process.Dispose();process=null;
            if(exitCode==0)
            {
                UnityEngine.Debug.Log("FLY_TACK_AUTO_IMPORT_PASS: "+Path.GetFileName(active.blend)+" -> "+active.fbx);
                AssetDatabase.ImportAsset(RelativeAssetPath(active.fbx),ImportAssetOptions.ForceUpdate);
                FlyTackLabWindow.RebuildOpenWindows();
            }
            else UnityEngine.Debug.LogError("FLY_TACK_AUTO_IMPORT_FAIL: "+Path.GetFileName(active.blend)+" (Blender exit code "+exitCode+")");
            StartNext();return;
        }
        if(EditorApplication.timeSinceStartup<nextCheck)return;
        nextCheck=EditorApplication.timeSinceStartup+1.0;
        QueueIfNeeded("Fly Saddle",false);
        QueueIfNeeded("Fly Armor",false);
        StartNext();
    }

    static void QueueIfNeeded(string name,bool force)
    {
        string blend=Path.Combine(ProjectRoot,"Exports","FlyTack",name+".blend");
        string fbx=Path.Combine(Application.dataPath,"Resources","FlyTack",name+" Edited.fbx");
        if(!File.Exists(blend))return;
        if(!force && File.Exists(fbx) && File.GetLastWriteTimeUtc(fbx)>=File.GetLastWriteTimeUtc(blend))return;
        foreach(Job job in pending)if(string.Equals(job.blend,blend,StringComparison.OrdinalIgnoreCase))return;
        if(process!=null && string.Equals(active.blend,blend,StringComparison.OrdinalIgnoreCase))return;
        pending.Enqueue(new Job(blend,fbx));
    }

    static void StartNext()
    {
        if(process!=null || pending.Count==0)return;
        string blender=Blender;
        if(string.IsNullOrEmpty(blender)){UnityEngine.Debug.LogError("FLY_TACK_AUTO_IMPORT_FAIL: Blender was not found.");pending.Clear();return;}
        active=pending.Dequeue();
        string script=Path.Combine(ProjectRoot,"Tools","export_current_fly_tack_fbx.py");
        var start=new ProcessStartInfo
        {
            FileName=blender,
            Arguments=Quote(active.blend)+" --background --python "+Quote(script)+" -- "+Quote(active.fbx),
            UseShellExecute=false,
            CreateNoWindow=true,
            RedirectStandardOutput=false,
            RedirectStandardError=false
        };
        process=Process.Start(start);
        UnityEngine.Debug.Log("FLY_TACK_AUTO_IMPORT_STARTED: "+Path.GetFileName(active.blend));
    }

    static string Quote(string value)=>"\""+value.Replace("\"","\\\"")+"\"";
    static string RelativeAssetPath(string absolute)=>"Assets"+absolute.Substring(Application.dataPath.Length).Replace('\\','/');
}
