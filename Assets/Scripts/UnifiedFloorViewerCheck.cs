#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace FruitFlyJoust
{
    [DefaultExecutionOrder(80)]
    public sealed class UnifiedFloorViewerCheck : MonoBehaviour
    {
        // The detailed body can run substantially slower than real time on CPU. This is only
        // a harness watchdog; physical qualification durations and limits remain unchanged.
        const float WallClockTimeoutSeconds = 600f;
        [Serializable] sealed class Report
        {
            public bool qualified;
            public string error, session;
            public int geometryCount;
            public float maximumPositionError, maximumRotationError;
            public string[] checks;
        }
        ResearchViewer viewer;
        Transform[] geometry;
        Vector3[] pausedPositions;
        Quaternion[] pausedRotations;
        string session, reportPath;
        float started, pausedAt, pausedTime;
        int stage;
        readonly List<string> checks = new List<string>();
        readonly HashSet<string> captures = new HashSet<string>();
        readonly Report report = new Report();
        void Start()
        {
            viewer=GetComponent<ResearchViewer>();started=Time.realtimeSinceStartup;
            reportPath=Path.Combine(Application.dataPath,"../Research/unified-viewer-"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff")+"-evaluation.json");
        }
        void Require(bool value,string label)
        { if(!value)throw new Exception(label);checks.Add(label);Debug.Log("UNIFIED_VIEWER_CHECK: "+label); }
        void InspectPose(ResearchViewer.Frame frame)
        {
            if(geometry==null)
            {
                var list=new List<Transform>();
                foreach(Transform child in transform)if(child.GetComponent<MeshFilter>())list.Add(child);
                geometry=list.ToArray();report.geometryCount=geometry.Length;
                Require(geometry.Length>0 && frame.positions.Length==geometry.Length*3 && frame.rotations.Length==geometry.Length*4,"all physical visual geometries received");
            }
            float positionError=0,rotationError=0;
            for(int i=0;i<geometry.Length;i++)
            {
                int p=i*3,q=i*4;
                Vector3 expected=ResearchViewer.Position(frame.positions[p],frame.positions[p+1],frame.positions[p+2])*viewer.displayUnitsPerMeter;
                Quaternion rotation=ResearchViewer.Rotation(frame.rotations[q],frame.rotations[q+1],frame.rotations[q+2],frame.rotations[q+3]);
                positionError=Mathf.Max(positionError,Vector3.Distance(geometry[i].localPosition,expected));
                rotationError=Mathf.Max(rotationError,Quaternion.Angle(geometry[i].localRotation,rotation));
            }
            report.maximumPositionError=Mathf.Max(report.maximumPositionError,positionError);
            report.maximumRotationError=Mathf.Max(report.maximumRotationError,rotationError);
            if(positionError>1e-5f || rotationError>.05f)throw new Exception("rendered geometry differs from physical frame");
        }
        void Capture(string phase)
        {
            if(!captures.Add(phase))return;
            Bounds bounds=new Bounds(viewer.riderAnchor.position,Vector3.zero);
            foreach(var renderer in viewer.GetComponentsInChildren<MeshRenderer>())bounds.Encapsulate(renderer.bounds);
            foreach(var renderer in viewer.riderAnchor.GetComponentsInChildren<Renderer>())bounds.Encapsulate(renderer.bounds);
            var cameraObject=new GameObject("Unified pose inspection camera");
            var camera=cameraObject.AddComponent<Camera>();camera.fieldOfView=35;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.18f,.22f,.28f);
            var texture=new RenderTexture(1000,1000,24);camera.targetTexture=texture;
            float distance=bounds.extents.magnitude/Mathf.Tan(17.5f*Mathf.Deg2Rad)*1.1f;
            foreach(var direction in new[]{viewer.riderAnchor.right,viewer.riderAnchor.forward})
            {
                camera.transform.position=bounds.center+direction*distance;
                camera.transform.LookAt(bounds.center,viewer.riderAnchor.up);camera.Render();
                var previous=RenderTexture.active;RenderTexture.active=texture;
                var pixels=new Texture2D(1000,1000,TextureFormat.RGB24,false);
                pixels.ReadPixels(new Rect(0,0,1000,1000),0,0);pixels.Apply();RenderTexture.active=previous;
                string side=direction==viewer.riderAnchor.right ? "side" : "front";
                File.WriteAllBytes(Path.Combine(Application.dataPath,"../Research/unified-"+phase+"-"+side+".rgb"),pixels.GetRawTextureData());Destroy(pixels);
            }
            camera.targetTexture=null;texture.Release();Destroy(texture);Destroy(cameraObject);
        }
        void Finish(bool passed,string error=null)
        {
            report.qualified=passed;report.error=error;report.session=session;report.checks=checks.ToArray();
            File.WriteAllText(reportPath,JsonUtility.ToJson(report,true));
            if(passed)Debug.Log("UNIFIED_VIEWER_CHECK_PASSED"+checks.Count);else Debug.LogError("UNIFIED_VIEWER_CHECK_FAILED: "+error);
            enabled=false;UnityEditor.EditorApplication.isPlaying=false;
        }
        void Update()
        {
            try
            {
                if(Time.realtimeSinceStartup-started>WallClockTimeoutSeconds)throw new Exception("viewer check timeout");
                var frame=viewer.CurrentFrame;if(frame==null)return;
                InspectPose(frame);
                if(frame.sim_time>=1.2f && frame.sim_time<1.7f)Capture("flight");
                if(frame.sim_time>=3.1f && frame.sim_time<4.7f)Capture("landed");
                if(frame.sim_time>=6)Capture("walking");
                if(stage==0 && frame.sim_time>=.08f)
                {
                    session=frame.session;
                    Require(viewer.unifiedLab && viewer.trainedFlight && frame.neurons==0,"trained scientific lab distinct from connectome mode");
                    var visual=viewer.GetComponent<RiderAnimationVisual>();
                    Require(visual && Mathf.Abs(visual.mountedSeatHeight+.33f)<1e-6f && Mathf.Abs(visual.visualScale-RiderCombat.CanonicalRiderVisualScale)<1e-6f,"correct fitted thorax seat");
                    viewer.RequestPause(true);stage=1;
                }
                else if(stage==1 && frame.paused)
                {
                    pausedTime=frame.sim_time;pausedAt=Time.realtimeSinceStartup;
                    pausedPositions=new Vector3[geometry.Length];pausedRotations=new Quaternion[geometry.Length];
                    for(int i=0;i<geometry.Length;i++){pausedPositions[i]=geometry[i].localPosition;pausedRotations[i]=geometry[i].localRotation;}
                    stage=2;
                }
                else if(stage==2)
                {
                    if(frame.sim_time!=pausedTime || viewer.BodyDeltaTime!=0)throw new Exception("paused body clock advanced");
                    for(int i=0;i<geometry.Length;i++)if(geometry[i].localPosition!=pausedPositions[i] || geometry[i].localRotation!=pausedRotations[i])throw new Exception("paused rendered pose moved");
                    if(Time.realtimeSinceStartup-pausedAt>.5f)
                    { Require(true,"paused physical clock and all rendered poses frozen");viewer.RequestPause(false);stage=3; }
                }
                else if(stage==3 && !frame.paused && frame.sim_time>pausedTime)
                { Require(viewer.BodyDeltaTime>=0,"resumed physical clock advances");stage=4; }
                else if(stage==4 && frame.episode_ended)
                {
                    Require(frame.episode_status=="complete: qualification passed","whole streamed floor loop qualified");
                    Require(captures.Count==3,"actual flight landed and walking captures saved");
                    Require(report.maximumPositionError<=1e-5f && report.maximumRotationError<=.05f,"all rendered poses track actual scientific state");
                    viewer.RequestReset();stage=5;
                }
                else if(stage==5 && frame.session!=session)
                {
                    Require(frame.sim_time<.2f && viewer.BodyDeltaTime>=0,"explicit replay starts a new body session");
                    Finish(true);
                }
            }
            catch(Exception error){Finish(false,error.Message);}
        }
    }
}
#endif
