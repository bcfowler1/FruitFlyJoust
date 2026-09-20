using System.Collections;
using System.IO;
using UnityEngine;

namespace FruitFlyJoust
{
    // Gameplay jouster. Its competency advances only after defeat and respawn.
    [RequireComponent(typeof(CombatTarget))]
    public sealed class MountedJoustOpponent : MonoBehaviour
    {
        public RiderCombat player;
        public int competencyLevel;
        public float Competence { get { return Mathf.Clamp(.18f+competencyLevel*.12f,.18f,.92f); } }
        public bool Mounted { get; private set; }=true;
        public float LastFallDamage { get; private set; }
        public int RespawnCount { get; private set; }
        public bool RiderRagdolled { get { return riderVisual && riderVisual.Ragdolled; } }
        public FlyCorpse LastFlyCorpse { get; private set; }
        public Vector3 CurrentVelocity { get { return velocity; } }
        Transform flyVisual,lance,riderAnchor;
        BiologicalPoseMirror poseMirror;
        RiderAnimationVisual riderVisual;
        GameObject biologicalTemplate;
        Material riderMaterial,weaponMaterial;
        CharacterController feet;
        CombatOpponent groundAI;
        CombatTarget health,mountHealth;
        Vector3 velocity,lastLanceTip,spawn,saddleBasePosition,saddleBaseScale;
        Quaternion saddleBaseRotation;
        float clock,contactCooldown,verticalSpeed,peakFallSpeed,respawnTimer=-1;

        public void Initialize(RiderCombat rider,GameObject biologicalVisual,Transform saddleTemplate,Transform rideRoot,Material sharedRiderMaterial,Material sharedWeaponMaterial,Vector3 position)
        {
            player=rider;biologicalTemplate=biologicalVisual;riderMaterial=sharedRiderMaterial;weaponMaterial=sharedWeaponMaterial;spawn=position;
            health=GetComponent<CombatTarget>();
            var placeholder=GetComponent<Renderer>();if(placeholder)placeholder.enabled=false;
            var primitiveCollider=GetComponent<CapsuleCollider>();if(primitiveCollider)Destroy(primitiveCollider);
            feet=gameObject.AddComponent<CharacterController>();feet.height=1.2f;feet.radius=.25f;feet.center=new Vector3(0,.6f,0);feet.enabled=false;
            riderAnchor=new GameObject("Enemy saddle").transform;riderAnchor.SetParent(transform,false);
            if(saddleTemplate && rideRoot)
            {
                riderAnchor.localPosition=rideRoot.InverseTransformPoint(saddleTemplate.position);
                riderAnchor.localRotation=Quaternion.Inverse(rideRoot.rotation)*saddleTemplate.rotation;
                Vector3 rootScale=rideRoot.lossyScale,saddleScale=saddleTemplate.lossyScale;
                riderAnchor.localScale=new Vector3(saddleScale.x/Mathf.Max(.0001f,rootScale.x),saddleScale.y/Mathf.Max(.0001f,rootScale.y),saddleScale.z/Mathf.Max(.0001f,rootScale.z));
            }
            saddleBasePosition=riderAnchor.localPosition;saddleBaseRotation=riderAnchor.localRotation;saddleBaseScale=riderAnchor.localScale;
            riderVisual=gameObject.AddComponent<RiderAnimationVisual>();riderVisual.visualScale=.78f;riderVisual.mountedSeatHeight=-.33f;riderVisual.mountedSeatForward=-.16f;
            riderVisual.Create(riderAnchor,riderMaterial);riderVisual.Pose(riderAnchor,true,0);
            BuildMount();BuildHitZones();ResetPose();
#if UNITY_EDITOR
            StartCoroutine(CaptureRenderedOpponent());
#endif
        }
        void BuildMount()
        {
            if(riderAnchor){riderAnchor.localPosition=saddleBasePosition;riderAnchor.localRotation=saddleBaseRotation;riderAnchor.localScale=saddleBaseScale;}
            if(biologicalTemplate)
            {
                flyVisual=Instantiate(biologicalTemplate,transform,false).transform;flyVisual.name="Enemy biological fly";
                // The biomodel vertices are authored in the player's ride-root frame. Preserve
                // the complete root transform; guessed offsets/scale shear the assembled fly.
                flyVisual.localPosition=biologicalTemplate.transform.localPosition;
                flyVisual.localRotation=biologicalTemplate.transform.localRotation;
                flyVisual.localScale=biologicalTemplate.transform.localScale;
                Quaternion originalRotation=flyVisual.localRotation;
                Transform head=flyVisual.Find("0/Head"),thorax=flyVisual.Find("0/Thorax");
                if(head && thorax)
                {
                    Vector3 anatomical=Vector3.ProjectOnPlane(flyVisual.localRotation*(head.localPosition-thorax.localPosition),Vector3.up).normalized;
                    if(anatomical.sqrMagnitude>.5f)flyVisual.localRotation=Quaternion.FromToRotation(anatomical,Vector3.forward)*flyVisual.localRotation;
                }
                // The mesh's visible longitudinal axis is 90 degrees clockwise from the
                // part-origin head/thorax axis above. The top render therefore needs this
                // explicit opponent-frame correction so head, velocity, and lance agree.
                flyVisual.localRotation=Quaternion.AngleAxis(-90,Vector3.up)*flyVisual.localRotation;
                // The saddle was copied from the same player ride-root frame. Rotate its
                // complete frame with the fly so the rider stays planted over the thorax
                // through yaw changes instead of orbiting beside the corrected model.
                Quaternion saddleCorrection=flyVisual.localRotation*Quaternion.Inverse(originalRotation);
                if(riderAnchor)
                {
                    riderAnchor.localPosition=saddleCorrection*riderAnchor.localPosition;
                    // The player saddle already faces gameplay forward. Only its location
                    // belongs to the corrected biomodel frame; rotating its orientation
                    // here turns the rider 90 degrees sideways while standing still.
                }
                poseMirror=flyVisual.gameObject.AddComponent<BiologicalPoseMirror>();poseMirror.Initialize(biologicalTemplate.transform);
            }
            else
            {
                var fly=GameObject.CreatePrimitive(PrimitiveType.Sphere);Destroy(fly.GetComponent<Collider>());
                fly.name="Enemy fly";fly.transform.SetParent(transform,false);fly.transform.localPosition=new Vector3(0,-.25f,0);
                fly.transform.localScale=new Vector3(.8f,.35f,1.25f);fly.GetComponent<Renderer>().sharedMaterial=weaponMaterial;flyVisual=fly.transform;
            }
            var weapon=GameObject.CreatePrimitive(PrimitiveType.Cube);Destroy(weapon.GetComponent<Collider>());
            weapon.name="Enemy lance";weapon.transform.SetParent(riderAnchor ? riderAnchor : transform,false);weapon.transform.localPosition=new Vector3(.25f,.32f,1.35f);
            weapon.transform.localScale=new Vector3(.055f,.055f,2.5f);weapon.GetComponent<Renderer>().sharedMaterial=weaponMaterial;lance=weapon.transform;
        }
        void BuildHitZones()
        {
            var riderZone=new GameObject("Enemy rider hitbox");riderZone.transform.SetParent(riderAnchor,false);
            riderZone.transform.localPosition=new Vector3(0,.28f,-.03f);
            var riderCollider=riderZone.AddComponent<CapsuleCollider>();riderCollider.direction=1;riderCollider.center=new Vector3(0,.35f,0);riderCollider.height=1.25f;riderCollider.radius=.28f;
            var previousRootHealth=health;health=riderZone.AddComponent<CombatTarget>();health.maximumHealth=100;health.ResetTarget();
            var riderHit=riderZone.AddComponent<MountedHitZone>();riderHit.owner=this;riderHit.fly=false;
            if(previousRootHealth)previousRootHealth.enabled=false;

            var flyZone=new GameObject("Enemy fly hitbox");flyZone.transform.SetParent(flyVisual ? flyVisual : transform,false);
            var flyCollider=flyZone.AddComponent<BoxCollider>();
            if(flyVisual)
            {
                Renderer[] renderers=flyVisual.GetComponentsInChildren<Renderer>();
                if(renderers.Length>0)
                {
                    Bounds bounds=renderers[0].bounds;foreach(var renderer in renderers)bounds.Encapsulate(renderer.bounds);
                    flyZone.transform.position=bounds.center;flyZone.transform.rotation=flyVisual.rotation;
                    Vector3 scale=flyZone.transform.lossyScale;
                    flyCollider.size=new Vector3(bounds.size.x/Mathf.Max(.001f,scale.x),bounds.size.y/Mathf.Max(.001f,scale.y),bounds.size.z/Mathf.Max(.001f,scale.z))*.72f;
                }
            }
            mountHealth=flyZone.AddComponent<CombatTarget>();mountHealth.maximumHealth=120;mountHealth.ResetTarget();
            var flyHit=flyZone.AddComponent<MountedHitZone>();flyHit.owner=this;flyHit.fly=true;
        }
        void ResetPose()
        {
            feet.enabled=false;transform.position=spawn;transform.rotation=Quaternion.LookRotation(player ? -player.transform.forward : Vector3.back);
            Mounted=true;velocity=Vector3.zero;verticalSpeed=peakFallSpeed=0;contactCooldown=1;respawnTimer=-1;
            health.ResetTarget();if(mountHealth)mountHealth.ResetTarget();lastLanceTip=LanceTip;
        }
        public Vector3 LanceTip { get { return lance ? lance.position+lance.forward*lance.lossyScale.z*.5f : transform.position; } }
        public float AnatomicalForwardAlignment
        {
            get
            {
                Vector3 direction=VisibleLongitudinalDirection();return direction.sqrMagnitude>.5f ? Vector3.Dot(direction,transform.forward) : 0;
            }
        }
        public float AnatomicalForwardAngle
        {
            get
            {
                Vector3 direction=VisibleLongitudinalDirection();
                return direction.sqrMagnitude>.5f ? Vector3.SignedAngle(transform.forward,Vector3.ProjectOnPlane(direction,transform.up),transform.up) : 180;
            }
        }
        Vector3 VisibleLongitudinalDirection()
        {
            if(!flyVisual)return Vector3.zero;
            Transform head=flyVisual.Find("0/Head"),a5=flyVisual.Find("0/A5"),a6=flyVisual.Find("0/A6");
            Renderer headRenderer=head ? head.GetComponent<Renderer>() : null,a5Renderer=a5 ? a5.GetComponent<Renderer>() : null,a6Renderer=a6 ? a6.GetComponent<Renderer>() : null;
            if(!headRenderer || (!a5Renderer && !a6Renderer))return Vector3.zero;
            Vector3 tail=a5Renderer && a6Renderer ? (a5Renderer.bounds.center+a6Renderer.bounds.center)*.5f : (a5Renderer ? a5Renderer.bounds.center : a6Renderer.bounds.center);
            return (headRenderer.bounds.center-tail).normalized;
        }
        public float WingMotionDegrees { get { return poseMirror ? poseMirror.MaximumWingMotion : 0; } }
        public float RiderThoraxDistance
        {
            get
            {
                if(!flyVisual || !riderAnchor || !riderVisual)return float.PositiveInfinity;
                Transform thorax=flyVisual.Find("0/Thorax");Renderer renderer=thorax ? thorax.GetComponent<Renderer>() : null;
                return renderer ? Vector3.ProjectOnPlane(riderVisual.VisualRootPosition-renderer.bounds.center,transform.up).magnitude : float.PositiveInfinity;
            }
        }
        public float RiderForwardAlignment
        {
            get
            {
                if(!riderAnchor)return 0;
                Vector3 riderForward=Vector3.ProjectOnPlane(riderAnchor.forward,transform.up).normalized;
                Vector3 travelForward=Vector3.ProjectOnPlane(transform.forward,transform.up).normalized;
                return Vector3.Dot(riderForward,travelForward);
            }
        }
        public CombatTarget RiderHealth { get { return health; } }
        public CombatTarget FlyHealth { get { return mountHealth; } }
        void Update()
        {
            if(!player || player.CombatPaused)return;
            float dt=player.CombatDeltaTime;if(dt<=0)return;
            if(health.Health<=0)
            {
                if(respawnTimer<0){respawnTimer=3;if(groundAI)groundAI.enabled=false;if(riderVisual)riderVisual.EnterRagdoll(velocity+Vector3.up*.5f);}
                respawnTimer-=dt;if(respawnTimer<=0)RespawnStronger();return;
            }
            contactCooldown=Mathf.Max(0,contactCooldown-dt);
            if(Mounted)Fly(dt);else if(!groundAI)Fall(dt);
        }
        void LateUpdate()
        {
            if(riderVisual)riderVisual.Pose(Mounted && riderAnchor ? riderAnchor : transform,Mounted,Mounted ? 0 : velocity.magnitude,health && health.Health<=0);
        }
        void Fly(float dt)
        {
            clock+=dt;float skill=Competence;
            Vector3 target=player.RiderPosition+Vector3.up*(player.Mounted ? .2f : 1.1f);
            float miss=(1-skill)*3.4f;
            Vector3 aim=target+transform.right*Mathf.Sin(clock*.73f+1.1f)*miss+Vector3.up*Mathf.Sin(clock*.41f)*miss*.3f;
            Vector3 desired=(aim-transform.position).normalized;
            transform.rotation=Quaternion.RotateTowards(transform.rotation,Quaternion.LookRotation(desired,Vector3.up),Mathf.Lerp(35,125,skill)*dt);
            velocity=Vector3.Lerp(velocity,transform.forward*Mathf.Lerp(3.5f,8f,skill),1-Mathf.Exp(-2.5f*dt));
            transform.position+=velocity*dt;
            Vector3 tip=LanceTip;
            if(player.Mounted && contactCooldown<=0 && velocity.magnitude>4)
            {
                float flyContact=DistanceToSegment(player.FlyHitPosition,lastLanceTip,tip);
                float riderContact=DistanceToSegment(player.RiderPosition+Vector3.up*.25f,lastLanceTip,tip);
                if(flyContact<.62f && flyContact<=riderContact){player.TakeFlyDamage(Mathf.Clamp(velocity.magnitude*5,18,45));contactCooldown=.6f;}
                else if(riderContact<.42f){player.ForceUnseat(velocity.normalized*3+Vector3.up*1.5f);contactCooldown=2;}
            }
            lastLanceTip=tip;
            if(Vector3.Distance(transform.position,target)>30)transform.position=spawn;
        }
        static float DistanceToSegment(Vector3 point,Vector3 a,Vector3 b)
        { Vector3 ab=b-a;float t=Mathf.Clamp01(Vector3.Dot(point-a,ab)/Mathf.Max(.0001f,ab.sqrMagnitude));return Vector3.Distance(point,a+ab*t); }
        public bool ReceiveLanceContact(float impact)
        {
            if(!Mounted || impact<3)return false;
            Mounted=false;verticalSpeed=2;peakFallSpeed=0;
            if(riderVisual)riderVisual.EnterRagdoll(velocity+Vector3.up*2);
            if(riderVisual)riderVisual.BeginMountTransition(false);
            if(flyVisual)
            {
                if(mountHealth && mountHealth.Health<=0)LastFlyCorpse=FlyCorpse.Create(flyVisual,velocity,true);
                flyVisual.gameObject.SetActive(false);Destroy(flyVisual.gameObject);flyVisual=null;
            }
            if(lance){lance.SetParent(null,true);Destroy(lance.gameObject,2);lance=null;}
            feet.enabled=true;return true;
        }
        public bool ReceiveFlyLanceContact(float impact)
        {
            if(!Mounted || !mountHealth)return false;
            if(mountHealth.Health<=0)return ReceiveLanceContact(Mathf.Max(impact,3));
            contactCooldown=.35f;return true;
        }
        void Fall(float dt)
        {
            if(feet.isGrounded && verticalSpeed<=0)
            {
                LastFallDamage=Mathf.Clamp((peakFallSpeed-4)*5,0,30);if(LastFallDamage>0)health.Hit(LastFallDamage);
                if(health.Health>0 && riderVisual)riderVisual.ExitRagdoll();
                verticalSpeed=-2;groundAI=gameObject.AddComponent<CombatOpponent>();groundAI.style=CombatOpponent.Style.Swordsman;
                groundAI.speed=Mathf.Lerp(1.2f,2.7f,Competence);groundAI.attackInterval=Mathf.Lerp(1.8f,.75f,Competence);return;
            }
            verticalSpeed+=Physics.gravity.y*dt;peakFallSpeed=Mathf.Max(peakFallSpeed,-verticalSpeed);
            velocity=Vector3.MoveTowards(velocity,Vector3.zero,dt*2);
            feet.Move((Vector3.ProjectOnPlane(velocity,Vector3.up)+Vector3.up*verticalSpeed)*dt);
        }
        void RespawnStronger()
        {
            competencyLevel++;RespawnCount++;clock=0;
            if(riderVisual)riderVisual.ExitRagdoll();
            if(groundAI){groundAI.enabled=false;Destroy(groundAI);groundAI=null;}
            if(flyVisual){flyVisual.gameObject.SetActive(false);Destroy(flyVisual.gameObject);}if(lance){lance.gameObject.SetActive(false);Destroy(lance.gameObject);}
            foreach(var zone in GetComponentsInChildren<MountedHitZone>())
            {var target=zone.GetComponent<CombatTarget>();if(target)target.enabled=false;var collider=zone.GetComponent<Collider>();if(collider)collider.enabled=false;zone.gameObject.SetActive(false);Destroy(zone.gameObject);}
            BuildMount();BuildHitZones();ResetPose();if(riderVisual){riderVisual.CancelMountTransition();riderVisual.Pose(riderAnchor ? riderAnchor : transform,true,0);}
        }
#if UNITY_EDITOR
        IEnumerator CaptureRenderedOpponent()
        {
            yield return null;yield return new WaitForEndOfFrame();
            var renderers=GetComponentsInChildren<Renderer>();
            if(renderers.Length==0)yield break;
            Bounds bounds=renderers[0].bounds;foreach(var renderer in renderers)if(renderer.enabled)bounds.Encapsulate(renderer.bounds);
            string directory=Path.GetFullPath(Path.Combine(Application.dataPath,"../Research"));Directory.CreateDirectory(directory);
            string diagnostic="rootForward="+transform.forward.ToString("F4")+" lanceForward="+(lance ? lance.forward.ToString("F4") : "missing")+"\n";
            foreach(string partName in new[]{"0/Head","0/Thorax","0/A1A2","0/A3","0/A4","0/A5","0/A6"})
            {
                Transform part=flyVisual ? flyVisual.Find(partName) : null;Renderer partRenderer=part ? part.GetComponent<Renderer>() : null;
                diagnostic+=partName+"="+(partRenderer ? transform.InverseTransformPoint(partRenderer.bounds.center).ToString("F4") : "missing")+"\n";
            }
            File.WriteAllText(Path.Combine(directory,"opponent-mounted-alignment.txt"),diagnostic);
            Vector3[] directions={-transform.forward,transform.right,transform.forward,Vector3.up-transform.forward*.35f};
            string[] names={"front","right","rear","top"};
            for(int i=0;i<directions.Length;i++)
            {
                var cameraObject=new GameObject("Opponent inspection camera");var camera=cameraObject.AddComponent<Camera>();
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.12f,.16f,.2f);camera.fieldOfView=32;camera.nearClipPlane=.01f;camera.farClipPlane=100;
                float distance=Mathf.Max(2.5f,bounds.extents.magnitude/Mathf.Tan(camera.fieldOfView*Mathf.Deg2Rad*.5f)*1.2f);
                Vector3 direction=directions[i].normalized;camera.transform.position=bounds.center+direction*distance;
                camera.transform.rotation=Quaternion.LookRotation(bounds.center-camera.transform.position,i==3 ? transform.forward : Vector3.up);
                var target=new RenderTexture(1000,1000,24,RenderTextureFormat.ARGB32);camera.targetTexture=target;camera.Render();
                RenderTexture previous=RenderTexture.active;RenderTexture.active=target;
                var image=new Texture2D(1000,1000,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1000,1000),0,0);image.Apply();
                WriteBmp(Path.Combine(directory,"opponent-mounted-"+names[i]+".bmp"),image);
                RenderTexture.active=previous;camera.targetTexture=null;Destroy(target);Destroy(image);Destroy(cameraObject);
            }
        }
        static void WriteBmp(string path,Texture2D image)
        {
            Color32[] pixels=image.GetPixels32();int width=image.width,height=image.height,row=((width*3+3)/4)*4;
            byte[] bytes=new byte[54+row*height];bytes[0]=(byte)'B';bytes[1]=(byte)'M';
            void Int(int offset,int value){bytes[offset]=(byte)value;bytes[offset+1]=(byte)(value>>8);bytes[offset+2]=(byte)(value>>16);bytes[offset+3]=(byte)(value>>24);}
            Int(2,bytes.Length);Int(10,54);Int(14,40);Int(18,width);Int(22,height);bytes[26]=1;bytes[28]=24;Int(34,row*height);
            for(int y=0;y<height;y++)for(int x=0;x<width;x++)
            {Color32 color=pixels[y*width+x];int target=54+y*row+x*3;bytes[target]=color.b;bytes[target+1]=color.g;bytes[target+2]=color.r;}
            File.WriteAllBytes(path,bytes);
        }
#endif
    }

    public sealed class MountedHitZone : MonoBehaviour
    {
        public MountedJoustOpponent owner;
        public bool fly;
    }

    // The detailed fly is assembled from 69 independently animated biomodel parts.
    // Keep an enemy clone in the same measured pose instead of freezing one spawn frame.
    [DefaultExecutionOrder(100)]
    sealed class BiologicalPoseMirror : MonoBehaviour
    {
        Transform source;
        Transform[] sourceParts,targetParts;
        Quaternion[] previousRotations;
        public float MaximumWingMotion { get; private set; }
        public void Initialize(Transform template)
        {
            source=template;sourceParts=new Transform[source.childCount];targetParts=new Transform[transform.childCount];previousRotations=new Quaternion[transform.childCount];
            for(int i=0;i<sourceParts.Length;i++)sourceParts[i]=source.GetChild(i);
            for(int i=0;i<targetParts.Length;i++){targetParts[i]=transform.GetChild(i);previousRotations[i]=targetParts[i].localRotation;}
            CopyPose();
        }
        void LateUpdate(){CopyPose();}
        void CopyPose()
        {
            if(!source || sourceParts==null)return;int count=Mathf.Min(sourceParts.Length,targetParts.Length);
            for(int i=0;i<count;i++)
            {
                if(!sourceParts[i] || !targetParts[i])continue;
                targetParts[i].localPosition=sourceParts[i].localPosition;
                targetParts[i].localRotation=sourceParts[i].localRotation;
                targetParts[i].localScale=sourceParts[i].localScale;
                targetParts[i].gameObject.SetActive(sourceParts[i].gameObject.activeSelf);
                if(targetParts[i].name.Contains("Wing"))MaximumWingMotion=Mathf.Max(MaximumWingMotion,Quaternion.Angle(previousRotations[i],targetParts[i].localRotation));
                previousRotations[i]=targetParts[i].localRotation;
            }
        }
    }
}


