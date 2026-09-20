using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEngine;
using UnityEngine.Rendering;
namespace FruitFlyJoust
{
    // Scientific mesh + recorded gait; fast gameplay remains an engineering approximation.
    public sealed class DetailedFlyVisual : MonoBehaviour
    {
        [Serializable] class Pose { public float[] positions=null,rotations=null; }
        [Serializable] class Data { public ResearchViewer.MeshData[] meshes=null;public ResearchViewer.GeomData[] geoms=null;public Pose[] poses=null;public float frame_seconds=0; }
        [Serializable] class FlightEntry { public string name="",source="";public float[] position=null,rotation=null,rest_position=null,rest_rotation=null; }
        [Serializable] class FlightPose { public float simulation_time;public FlightEntry[] entries=null; }
        [Serializable] class WingProfile
        {
            public string source="",source_asset="",source_license="";
            public float source_frequency_hz=218,display_frequency_hz=31,turn_differential=.22f,bank_differential=.14f,turn_plane_degrees=9,bank_plane_degrees=13;
            public float[] angles_degrees=null;
        }
        public bool detailed=true;
        FlyMotor motor;Data data;WingProfile wingProfile;Transform[] parts;GameObject root;
        readonly List<Mesh> meshes=new List<Mesh>();readonly List<Material> materials=new List<Material>();
        readonly List<Renderer> old=new List<Renderer>();float clock;
        readonly Dictionary<string,Vector3> legPivots=new Dictionary<string,Vector3>();
        readonly Dictionary<string,FlightEntry> flightPose=new Dictionary<string,FlightEntry>();
        Vector3 thoraxPosition,headPivot;Quaternion thoraxRotation;float gaitRate,flightBlend,idleAnimationClock,wingClock,wingDifferential;
        Vector3 flightFrameSourceCenter,flightFrameTargetCenter;Quaternion flightFrameRotation=Quaternion.identity;
        readonly HashSet<string> headParts=new HashSet<string>{"Head","LEye","REye","Rostrum","Haustellum","LPedicel","LFuniculus","LArista","RPedicel","RFuniculus","RArista"};
        void Start()
        {
            motor=GetComponent<FlyMotor>();
            var asset=Resources.Load<TextAsset>("FlyPlayableGeometry");if(!asset)return;
            using(var stream=new MemoryStream(asset.bytes))using(var gzip=new GZipStream(stream,CompressionMode.Decompress))using(var reader=new StreamReader(gzip))data=JsonUtility.FromJson<Data>(reader.ReadToEnd());
            var flightAsset=Resources.Load<TextAsset>("FlyUnifiedFlightPose");
            if(flightAsset)
            {
                var captured=JsonUtility.FromJson<FlightPose>(flightAsset.text);
                if(captured!=null && captured.entries!=null)foreach(var entry in captured.entries)flightPose[entry.name]=entry;
            }
            var wingAsset=Resources.Load<TextAsset>("FlyWingAnimationProfile");
            if(wingAsset)wingProfile=JsonUtility.FromJson<WingProfile>(wingAsset.text);
            foreach(var r in motor.bodyVisual.GetComponentsInChildren<Renderer>())
                if(r.transform.name!="Rider" && r.transform.name!="Rider head" && !r.GetComponentInParent<Animator>())old.Add(r);
            root=new GameObject("Detailed NeuroMechFly appearance");root.transform.SetParent(transform,false);
            var lookup=new Dictionary<int,Mesh>();
            foreach(var source in data.meshes)
            {
                var vertices=new Vector3[source.vertices.Length/3];for(int i=0;i<vertices.Length;i++)vertices[i]=ResearchViewer.Position(source.vertices[i*3],source.vertices[i*3+1],source.vertices[i*3+2])*500;
                for(int i=0;i<source.triangles.Length;i+=3){int first=source.triangles[i];source.triangles[i]=source.triangles[i+2];source.triangles[i+2]=first;}
                var mesh=new Mesh{name="NeuroMechFly "+source.id,indexFormat=IndexFormat.UInt32};mesh.vertices=vertices;mesh.triangles=source.triangles;mesh.RecalculateNormals();mesh.RecalculateBounds();meshes.Add(mesh);lookup.Add(source.id,mesh);
            }
            parts=new Transform[data.geoms.Length];
            for(int i=0;i<parts.Length;i++)
            {
                var source=data.geoms[i];var part=new GameObject(source.name);part.transform.SetParent(root.transform,false);
                part.AddComponent<MeshFilter>().sharedMesh=lookup[source.mesh];var material=ResearchViewer.CreateBodyMaterial(source);
                part.AddComponent<MeshRenderer>().sharedMaterial=material;materials.Add(material);parts[i]=part.transform;
                if(source.name.EndsWith("Coxa"))
                {
                    legPivots[source.name.Substring(2,2)]=ResearchViewer.Position(data.poses[0].positions[i*3],data.poses[0].positions[i*3+1],data.poses[0].positions[i*3+2])*500;
                }
                if(source.name=="0/Thorax")
                {
                    thoraxPosition=ResearchViewer.Position(data.poses[0].positions[i*3],data.poses[0].positions[i*3+1],data.poses[0].positions[i*3+2])*500;
                    thoraxRotation=ResearchViewer.Rotation(data.poses[0].rotations[i*4],data.poses[0].rotations[i*4+1],data.poses[0].rotations[i*4+2],data.poses[0].rotations[i*4+3]);
                }
                if(source.name=="0/Head")
                    headPivot=ResearchViewer.Position(data.poses[0].positions[i*3],data.poses[0].positions[i*3+1],data.poses[0].positions[i*3+2])*500;
            }
            if(flightPose.Count>0)
            {
                Vector3 Source(string leg)
                {
                    var e=flightPose["0/"+leg+"Coxa"];
                    return ResearchViewer.Position(e.rest_position[0],e.rest_position[1],e.rest_position[2])*500;
                }
                Vector3 sf=(Source("LF")+Source("RF"))*.5f,sh=(Source("LH")+Source("RH"))*.5f;
                Vector3 sl=(Source("LF")+Source("LM")+Source("LH"))/3,sr=(Source("RF")+Source("RM")+Source("RH"))/3;
                Vector3 tf=(legPivots["LF"]+legPivots["RF"])*.5f,th=(legPivots["LH"]+legPivots["RH"])*.5f;
                Vector3 tl=(legPivots["LF"]+legPivots["LM"]+legPivots["LH"])/3,tr=(legPivots["RF"]+legPivots["RM"]+legPivots["RH"])/3;
                Vector3 sourceForward=(sf-sh).normalized,sourceLateral=(sr-sl).normalized;
                Vector3 targetForward=(tf-th).normalized,targetLateral=(tr-tl).normalized;
                Quaternion sourceFrame=Quaternion.LookRotation(sourceForward,Vector3.Cross(sourceLateral,sourceForward).normalized);
                Quaternion targetFrame=Quaternion.LookRotation(targetForward,Vector3.Cross(targetLateral,targetForward).normalized);
                flightFrameRotation=targetFrame*Quaternion.Inverse(sourceFrame);
                flightFrameSourceCenter=(sf+sh)*.5f;flightFrameTargetCenter=(tf+th)*.5f;
            }
            // Keep rider on the thorax rather than the placeholder's raised back.
            var visual=GetComponent<RiderAnimationVisual>();if(visual){visual.visualScale=.78f;visual.mountedSeatHeight=-.33f;visual.mountedSeatForward=-.16f;}
        }
        void Update()
        {
            // RiderCombat creates this component in Start; configure after all Starts.
            var visual=GetComponent<RiderAnimationVisual>();
            if(visual && detailed){visual.visualScale=.78f;visual.mountedSeatHeight=-.33f;visual.mountedSeatForward=-.16f;}
        }
        void LateUpdate()
        {
            if(!root)return;root.SetActive(detailed);foreach(var r in old)if(r)r.enabled=!detailed;
            if(!detailed)return;
            root.transform.localPosition=motor.bodyVisual.localPosition;
            root.transform.localRotation=motor.bodyVisual.localRotation*Quaternion.Euler(0,-90,0);
            bool walking=motor.Phase==RidePhase.Perched && motor.SurfaceWalkingSpeed>.01f;
            bool flying=motor.Phase==RidePhase.Flying || motor.Phase==RidePhase.Launching;
            bool idle=motor.Phase==RidePhase.Perched && !walking;
            idleAnimationClock=idle ? idleAnimationClock+Time.deltaTime : 0;
            float idleCycle=Mathf.Repeat(idleAnimationClock,6);
            float cleaning=Mathf.SmoothStep(0,1,Mathf.Clamp01((idleCycle-1.2f)/.35f))*
                (1-Mathf.SmoothStep(0,1,Mathf.Clamp01((idleCycle-3.4f)/.35f)));
            float cleaningSide=Mathf.Sin(idleAnimationClock*5.5f);
            // During grooming the head inclines toward alternating foreleg wipes as one rigid assembly.
            // Eyes, mouthparts, and every antenna segment receive this same transform below.
            float headRoll=Mathf.Sin(idleAnimationClock*.8f)*3.5f+cleaning*cleaningSide*5.5f;
            Quaternion headIdle=Quaternion.AngleAxis(headRoll,Vector3.forward)*
                Quaternion.AngleAxis(Mathf.Sin(idleAnimationClock*1.15f+.7f)*1.5f,Vector3.right);
            flightBlend=Mathf.MoveTowards(flightBlend,flying ? 1 : 0,Time.deltaTime*3.5f);
            if(flying && wingProfile!=null)wingClock=Mathf.Repeat(wingClock+Time.deltaTime*wingProfile.display_frequency_hz,1);
            gaitRate=Mathf.MoveTowards(gaitRate,walking ? Mathf.Min(.85f,motor.SurfaceWalkingSpeed/.8f)*motor.SurfaceWalkingDirection : 0,Time.deltaTime*2);
            if(walking)clock+=Time.deltaTime*gaitRate;
            float sample=Mathf.Repeat(clock/data.frame_seconds,data.poses.Length);int a=(int)sample,b=(a+1)%data.poses.Length;float blend=sample-a;
            for(int i=0;i<parts.Length;i++)
            {
                int p=i*3,q=i*4;bool wing=data.geoms[i].name.Contains("Wing");
                string leg=data.geoms[i].name.Length>=4 ? data.geoms[i].name.Substring(2,2) : "";
                bool isLeg=!data.geoms[i].name.Contains("Haltere") && legPivots.ContainsKey(leg);
                var x=data.poses[walking && isLeg ? a : 0];var y=data.poses[walking && isLeg ? b : 0];
                Vector3 position=Vector3.Lerp(ResearchViewer.Position(x.positions[p],x.positions[p+1],x.positions[p+2]),ResearchViewer.Position(y.positions[p],y.positions[p+1],y.positions[p+2]),blend)*500;
                Quaternion rotation=Quaternion.Slerp(ResearchViewer.Rotation(x.rotations[q],x.rotations[q+1],x.rotations[q+2],x.rotations[q+3]),ResearchViewer.Rotation(y.rotations[q],y.rotations[q+1],y.rotations[q+2],y.rotations[q+3]),blend);
                if(isLeg && flightBlend>0 && flightPose.TryGetValue(data.geoms[i].name,out var captured))
                {
                    // Exact segment transforms captured from the qualified Unified biomodel.
                    Vector3 relative=ResearchViewer.Position(captured.position[0],captured.position[1],captured.position[2])*500;
                    Quaternion relativeRotation=ResearchViewer.Rotation(captured.rotation[0],captured.rotation[1],captured.rotation[2],captured.rotation[3]);
                    // One rigid alignment preserves every captured inter-segment transform.
                    // Independent per-leg alignment visibly disconnected adjacent meshes.
                    Vector3 targetPosition=flightFrameTargetCenter+flightFrameRotation*(relative-flightFrameSourceCenter);
                    Quaternion targetRotation=flightFrameRotation*relativeRotation;
                    float transition=flightBlend*flightBlend*(3-2*flightBlend);
                    position=Vector3.Lerp(position,targetPosition,transition);
                    rotation=Quaternion.Slerp(rotation,targetRotation,transition);
                }
                string shortName=data.geoms[i].name.Substring(2);
                if(idle && headParts.Contains(shortName))
                {
                    position=headPivot+headIdle*(position-headPivot);rotation=headIdle*rotation;
                }
                if(idle && cleaning>0 && (leg=="LF" || leg=="RF"))
                {
                    float side=leg[0]=='L' ? 1 : -1;
                    // Alternate the complete front-leg chains and keep them lateral to the face.
                    // The prior simultaneous 32-degree lift could visibly cross eyes/antennae.
                    float active=.25f+.75f*Mathf.Clamp01(.5f+.5f*cleaningSide*side);
                    float stroke=Mathf.Sin(idleAnimationClock*11+side*.8f)*2;
                    Quaternion grooming=Quaternion.AngleAxis(cleaning*active*(20+stroke),Vector3.forward)*
                        Quaternion.AngleAxis(side*cleaning*active*11,Vector3.up);
                    Vector3 pivot=legPivots[leg];position=pivot+grooming*(position-pivot);rotation=grooming*rotation;
                }
                if(wing && motor.Phase!=RidePhase.Perched)
                {
                    float side=data.geoms[i].name.Contains("LWing") ? 1 : -1;
                    // Sample the published three-axis FMech wing cycle. The 218 Hz source
                    // is displayed stroboscopically at 31 Hz to avoid frame-rate aliasing.
                    Vector3 measured=SampleWingCycle(wingClock);
                    float steering=Mathf.Clamp(motor.intent.turn,-1,1);
                    float banking=Mathf.Clamp(motor.rider.roll,-1,1);
                    float differential=steering*wingProfile.turn_differential+banking*wingProfile.bank_differential;
                    float amplitude=Mathf.Clamp(1-side*differential,.62f,1.38f);
                    wingDifferential=Mathf.Abs(differential)*2;
                    float stroke=(measured.x-15.2831f)*.78f*amplitude;
                    float deviation=(measured.y+4.566f)+side*(steering*wingProfile.turn_plane_degrees+banking*wingProfile.bank_plane_degrees);
                    float feather=(measured.z-40.6689f)*.30f;
                    Quaternion movement=Quaternion.AngleAxis(side*(50+stroke),Vector3.up)*
                        Quaternion.AngleAxis(deviation,Vector3.forward)*
                        Quaternion.AngleAxis(side*feather,Vector3.right);
                    var hinge=data.geoms[i].pivot;
                    if(hinge!=null && hinge.Length==3)
                    {
                        Vector3 wingPivot=ResearchViewer.Position(hinge[0],hinge[1],hinge[2])*500;
                        position=wingPivot+movement*(position-wingPivot);
                    }
                    rotation=movement*rotation;
                }
                float smoothing=1-Mathf.Exp(-18*Time.deltaTime);
                // Apply the sampled skeleton together: segment-specific lag breaks leg/head alignment.
                bool sampled=walking && isLeg || isLeg && flightBlend>0 || idle && (headParts.Contains(shortName) || cleaning>0 && (leg=="LF" || leg=="RF"));
                parts[i].localPosition=Vector3.Lerp(parts[i].localPosition,position,wing || sampled ? 1 : smoothing);
                parts[i].localRotation=Quaternion.Slerp(parts[i].localRotation,rotation,wing || sampled ? 1 : smoothing);
            }
        }
        Vector3 SampleWingCycle(float phase)
        {
            if(wingProfile==null || wingProfile.angles_degrees==null || wingProfile.angles_degrees.Length<6)
                return new Vector3(15.2831f,-4.566f,40.6689f);
            int count=wingProfile.angles_degrees.Length/3;
            float sample=Mathf.Repeat(phase,1)*count;int a=Mathf.FloorToInt(sample)%count,b=(a+1)%count;float t=sample-Mathf.Floor(sample);
            int x=a*3,y=b*3;
            return Vector3.Lerp(new Vector3(wingProfile.angles_degrees[x],wingProfile.angles_degrees[x+1],wingProfile.angles_degrees[x+2]),
                new Vector3(wingProfile.angles_degrees[y],wingProfile.angles_degrees[y+1],wingProfile.angles_degrees[y+2]),t);
        }
#if UNITY_EDITOR
        public bool UsesMeasuredWingCycle { get { return wingProfile!=null && wingProfile.angles_degrees!=null && wingProfile.angles_degrees.Length>=96 && wingProfile.source_frequency_hz>200; } }
        public float WingSteeringDifferential { get { return wingDifferential; } }
        public float MaximumFlightPoseError()
        {
            if(data==null || parts==null || flightPose.Count==0 || flightBlend<.999f)return float.PositiveInfinity;
            float maximum=0;int count=0;
            for(int i=0;i<parts.Length;i++)
            {
                if(!flightPose.TryGetValue(data.geoms[i].name,out var captured))continue;
                Vector3 relative=ResearchViewer.Position(captured.position[0],captured.position[1],captured.position[2])*500;
                Quaternion relativeRotation=ResearchViewer.Rotation(captured.rotation[0],captured.rotation[1],captured.rotation[2],captured.rotation[3]);
                maximum=Mathf.Max(maximum,
                    Vector3.Distance(parts[i].localPosition,flightFrameTargetCenter+flightFrameRotation*(relative-flightFrameSourceCenter)),
                    Quaternion.Angle(parts[i].localRotation,flightFrameRotation*relativeRotation));
                count++;
            }
            return count==flightPose.Count ? maximum : float.PositiveInfinity;
        }
        public float MaximumWingHingeError()
        {
            if(data==null || parts==null)return float.PositiveInfinity;
            float maximum=0;int count=0;
            for(int i=0;i<parts.Length;i++)
            {
                var geom=data.geoms[i];if(!geom.name.Contains("Wing"))continue;
                if(geom.pivot==null || geom.pivot.Length!=3)return float.PositiveInfinity;
                int p=i*3,q=i*4;var rest=data.poses[0];
                Vector3 hinge=ResearchViewer.Position(geom.pivot[0],geom.pivot[1],geom.pivot[2])*500;
                Vector3 origin=ResearchViewer.Position(rest.positions[p],rest.positions[p+1],rest.positions[p+2])*500;
                Quaternion rotation=ResearchViewer.Rotation(rest.rotations[q],rest.rotations[q+1],rest.rotations[q+2],rest.rotations[q+3]);
                Vector3 meshHinge=Quaternion.Inverse(rotation)*(hinge-origin);
                maximum=Mathf.Max(maximum,(parts[i].localPosition+parts[i].localRotation*meshHinge-hinge).magnitude);count++;
            }
            return count==2 ? maximum : float.PositiveInfinity;
        }
#endif
        void OnGUI()
        {
            GUI.color=new Color(.04f,.08f,.12f,.95f);GUI.DrawTexture(new Rect(16,246,610,76),Texture2D.whiteTexture);GUI.color=Color.white;
            detailed=GUI.Toggle(new Rect(24,250,560,24),detailed,"Detailed NeuroMechFly appearance (recorded gait / biomodel flight legs)");
            motor.autonomousIdle=GUI.Toggle(new Rect(24,274,560,24),motor.autonomousIdle,"Fly takes short walks while dismounted (gameplay approximation)");
            GUI.Label(new Rect(24,298,580,20),"Choose WalkingLab for live articulated MuJoCo movement.");
        }
        void OnDestroy(){foreach(var mesh in meshes)Destroy(mesh);foreach(var material in materials)Destroy(material);}
    }
}
