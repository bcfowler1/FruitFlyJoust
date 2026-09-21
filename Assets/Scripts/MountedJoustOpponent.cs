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
        public int RiderRagdollBodyCount { get { return riderVisual ? riderVisual.RagdollBodyCount : 0; } }
        public FlyCorpse LastFlyCorpse { get; private set; }
        public Vector3 CurrentVelocity { get { return velocity; } }
        public bool FlyEscaping { get; private set; }
        public bool ReplacementMountScheduled { get; private set; }
        public float EnemyHunger { get; private set; }=.35f;
        public int ReturnedRemountCount { get; private set; }
        public Rigidbody LastDroppedLance { get; private set; }
        public float LanceGripError { get { return lance && riderVisual && riderVisual.Hand(false) ? LanceGeometry.GripError(lance,riderVisual.Hand(false).position) : float.PositiveInfinity; } }
        public float LanceReach { get { return lance && riderVisual && riderVisual.Hand(false) ? Vector3.Distance(riderVisual.Hand(false).position,LanceTip) : 0; } }
        public Vector3 RenderedRiderPosition { get { return riderVisual ? riderVisual.RagdollCenter : transform.position; } }
        public bool RiderTransitioning { get { return riderVisual && riderVisual.Transitioning; } }
        public float GroundClearance
        {
            get
            {
                return EnvironmentRaycast(transform.position+Vector3.up*.5f,Vector3.down,100,out var hit) && Vector3.Dot(hit.normal,Vector3.up)>.65f ?
                    Vector3.Dot(transform.position-hit.point,hit.normal) : float.PositiveInfinity;
            }
        }
        Transform flyVisual,lance,riderAnchor;
        BiologicalPoseMirror poseMirror;
        RiderAnimationVisual riderVisual;
        GameObject biologicalTemplate;
        Material riderMaterial,weaponMaterial;
        CharacterController feet;
        CombatOpponent groundAI;
        CombatTarget health,mountHealth;
        Vector3 velocity,lastLanceTip,flyDeathImpact,spawn,saddleBasePosition,saddleBaseScale,flyTemplateLocalPosition,flyTemplateLocalScale,flyMountedLocalPosition,flyMountedLocalScale;
        Quaternion saddleBaseRotation,flyTemplateLocalRotation,flyMountedLocalRotation;
        float clock,contactCooldown,verticalSpeed,peakFallSpeed,respawnTimer=-1,replacementMountTimer=-1;

        public void Initialize(RiderCombat rider,GameObject biologicalVisual,Transform saddleTemplate,Transform rideRoot,Material sharedRiderMaterial,Material sharedWeaponMaterial,Vector3 position)
        {
            player=rider;biologicalTemplate=biologicalVisual;riderMaterial=sharedRiderMaterial;weaponMaterial=sharedWeaponMaterial;spawn=position;
            if(biologicalTemplate)
            {
                flyTemplateLocalPosition=biologicalTemplate.transform.localPosition;
                flyTemplateLocalRotation=biologicalTemplate.transform.localRotation;
                flyTemplateLocalScale=biologicalTemplate.transform.localScale;
            }
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
            riderVisual=gameObject.AddComponent<RiderAnimationVisual>();riderVisual.visualScale=.78f;riderVisual.mountedSeatHeight=-.33f;riderVisual.mountedSeatForward=-.16f;riderVisual.mountTransitions=true;
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
                flyVisual.localPosition=flyTemplateLocalPosition;
                flyVisual.localRotation=flyTemplateLocalRotation;
                flyVisual.localScale=flyTemplateLocalScale;
                Quaternion originalRotation=flyVisual.localRotation;
                Transform head=FindPart(flyVisual,"0/Head"),a5=FindPart(flyVisual,"0/A5"),a6=FindPart(flyVisual,"0/A6");
                Renderer headRenderer=head ? head.GetComponent<Renderer>() : null,a5Renderer=a5 ? a5.GetComponent<Renderer>() : null,a6Renderer=a6 ? a6.GetComponent<Renderer>() : null;
                if(headRenderer && (a5Renderer || a6Renderer))
                {
                    Vector3 tail=a5Renderer && a6Renderer ? (a5Renderer.bounds.center+a6Renderer.bounds.center)*.5f : (a5Renderer ? a5Renderer.bounds.center : a6Renderer.bounds.center);
                    Vector3 anatomical=Vector3.ProjectOnPlane(transform.InverseTransformDirection(headRenderer.bounds.center-tail),Vector3.up).normalized;
                    if(anatomical.sqrMagnitude>.5f)flyVisual.localRotation=Quaternion.FromToRotation(anatomical,Vector3.forward)*flyVisual.localRotation;
                }
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
                flyMountedLocalPosition=flyVisual.localPosition;flyMountedLocalRotation=flyVisual.localRotation;flyMountedLocalScale=flyVisual.localScale;
            }
            else
            {
                var fly=GameObject.CreatePrimitive(PrimitiveType.Sphere);Destroy(fly.GetComponent<Collider>());
                fly.name="Enemy fly";fly.transform.SetParent(transform,false);fly.transform.localPosition=new Vector3(0,-.25f,0);
                fly.transform.localScale=new Vector3(.8f,.35f,1.25f);fly.GetComponent<Renderer>().sharedMaterial=weaponMaterial;flyVisual=fly.transform;
                flyMountedLocalPosition=flyVisual.localPosition;flyMountedLocalRotation=flyVisual.localRotation;flyMountedLocalScale=flyVisual.localScale;
            }
            BuildLance();
        }
        void BuildLance()
        {
            var prefab=Resources.Load<GameObject>("Weapons/Fly Lance Source") ?? Resources.Load<GameObject>("Weapons/Fly Lance");
            var weapon=prefab ? Instantiate(prefab) : GameObject.CreatePrimitive(PrimitiveType.Cube);
            foreach(var collider in weapon.GetComponentsInChildren<Collider>())Destroy(collider);
            weapon.name="Enemy lance";weapon.transform.SetParent(riderAnchor ? riderAnchor : transform,false);weapon.transform.localPosition=Vector3.zero;
            Vector3 parentScale=weapon.transform.parent ? weapon.transform.parent.lossyScale : Vector3.one;
            Vector3 authoredScale=prefab ? Vector3.one : new Vector3(.055f,.055f,2.5f);
            weapon.transform.localScale=new Vector3(authoredScale.x/Mathf.Max(.001f,parentScale.x),authoredScale.y/Mathf.Max(.001f,parentScale.y),authoredScale.z/Mathf.Max(.001f,parentScale.z));
            var renderer=weapon.GetComponent<Renderer>();if(renderer)renderer.sharedMaterial=weaponMaterial;lance=weapon.transform;
            Transform hand=riderVisual ? riderVisual.Hand(false) : null;
            if(hand)LanceGeometry.AlignGrip(lance,lance,hand.position,riderAnchor ? riderAnchor.rotation : transform.rotation);
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
            Mounted=true;FlyEscaping=false;ReplacementMountScheduled=false;velocity=Vector3.zero;verticalSpeed=peakFallSpeed=0;contactCooldown=1;respawnTimer=replacementMountTimer=-1;
            health.ResetTarget();if(mountHealth)mountHealth.ResetTarget();lastLanceTip=LanceTip;
        }
        public Vector3 LanceTip
        {
            get
            {
                if(!lance)return transform.position;float farthest=0;Vector3 forward=lance.forward;
                foreach(var renderer in lance.GetComponentsInChildren<Renderer>())
                {Bounds b=renderer.bounds;float projection=Vector3.Dot(b.center-lance.position,forward)+Vector3.Dot(b.extents,new Vector3(Mathf.Abs(forward.x),Mathf.Abs(forward.y),Mathf.Abs(forward.z)));farthest=Mathf.Max(farthest,projection);}
                return lance.position+forward*farthest;
            }
        }
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
            Transform head=FindPart(flyVisual,"0/Head"),a5=FindPart(flyVisual,"0/A5"),a6=FindPart(flyVisual,"0/A6");
            Renderer headRenderer=head ? head.GetComponent<Renderer>() : null,a5Renderer=a5 ? a5.GetComponent<Renderer>() : null,a6Renderer=a6 ? a6.GetComponent<Renderer>() : null;
            if(!headRenderer || (!a5Renderer && !a6Renderer))return Vector3.zero;
            Vector3 tail=a5Renderer && a6Renderer ? (a5Renderer.bounds.center+a6Renderer.bounds.center)*.5f : (a5Renderer ? a5Renderer.bounds.center : a6Renderer.bounds.center);
            return (headRenderer.bounds.center-tail).normalized;
        }
        static Transform FindPart(Transform root,string exactName)
        {
            if(!root)return null;foreach(Transform child in root)if(child.name==exactName)return child;return null;
        }
        public float WingMotionDegrees { get { return poseMirror ? poseMirror.MaximumWingMotion : 0; } }
        public float WingSpeedScale { get { return poseMirror ? poseMirror.WingSpeedScale : 0; } }
        public float RiderThoraxDistance
        {
            get
            {
                if(!flyVisual || !riderAnchor || !riderVisual)return float.PositiveInfinity;
                Transform thorax=FindPart(flyVisual,"0/Thorax");Renderer renderer=thorax ? thorax.GetComponent<Renderer>() : null;
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
        public Vector3 SaddleLocalPosition { get { return riderAnchor ? riderAnchor.localPosition : Vector3.positiveInfinity; } }
        public Vector3 RiderLocalPosition
        {
            get { return riderAnchor && riderVisual ? riderAnchor.InverseTransformPoint(riderVisual.VisualRootPosition) : Vector3.positiveInfinity; }
        }
        public CombatTarget RiderHealth { get { return health; } }
        public CombatTarget FlyHealth { get { return mountHealth; } }
        void Update()
        {
            if(!player || player.CombatPaused)return;
            float dt=player.CombatDeltaTime;if(dt<=0)return;
            if(health.Health<=0)
            {
                if(respawnTimer<0)
                {
                    FlyEscaping=Mounted && flyVisual && mountHealth && mountHealth.Health>0;
                    respawnTimer=FlyEscaping ? 6 : 3;Mounted=false;
                    if(groundAI)groundAI.enabled=false;
                    var riderCollider=health.GetComponent<Collider>();if(riderCollider)riderCollider.enabled=false;
                    if(riderVisual)riderVisual.EnterRagdoll(velocity+Vector3.up*.5f);
                    if(lance){lance.gameObject.SetActive(false);Destroy(lance.gameObject);lance=null;}
                }
                if(FlyEscaping)EscapeFly(dt);
                respawnTimer-=dt;if(respawnTimer<=0)RespawnStronger();return;
            }
            if(Mounted && mountHealth && mountHealth.Health<=0){ReceiveLanceContact(3);return;}
            if(ReplacementMountScheduled)
            {
                replacementMountTimer-=dt;
                if(replacementMountTimer<=0)
                {
                    ReplacementMountScheduled=false;replacementMountTimer=-1;
                    if(player)player.EnsureEnemyMountReplacement(transform.parent,spawn,competencyLevel+1);
                }
            }
            contactCooldown=Mathf.Max(0,contactCooldown-dt);
            if(Mounted)Fly(dt);else if(!groundAI)Fall(dt);
        }
        void EscapeFly(float dt)
        {
            Vector3 away=Vector3.ProjectOnPlane(transform.position-player.RiderPosition,Vector3.up);
            if(away.sqrMagnitude<.01f)away=-transform.forward;
            Vector3 desiredDirection=(away.normalized+Vector3.up*.24f).normalized;
            transform.rotation=Quaternion.RotateTowards(transform.rotation,Quaternion.LookRotation(desiredDirection,Vector3.up),65*dt);
            Vector3 desiredVelocity=transform.forward*4.5f+Vector3.up*1.1f;
            velocity=Vector3.Lerp(velocity,desiredVelocity,1-Mathf.Exp(-1.4f*dt));
            Vector3 movement=velocity*dt;
            if(movement.sqrMagnitude>.0001f && EnvironmentSphereCast(transform.position,.48f,movement.normalized,movement.magnitude+.08f,out var obstacle))
            {
                velocity=Vector3.ProjectOnPlane(velocity,obstacle.normal)+obstacle.normal*1.2f;
                transform.position=obstacle.point+obstacle.normal*.52f;
            }
            else transform.position+=movement;
        }
        void LateUpdate()
        {
            if(riderVisual)riderVisual.AdvanceTransition(player ? player.CombatDeltaTime : Time.deltaTime);
            if(riderVisual)riderVisual.Pose(Mounted && riderAnchor ? riderAnchor : transform,Mounted,Mounted ? 0 : velocity.magnitude,health && health.Health<=0);
            Transform hand=riderVisual ? riderVisual.Hand(false) : null;
            if(Mounted && lance && hand)LanceGeometry.AlignGrip(lance,lance,hand.position,riderAnchor ? riderAnchor.rotation : transform.rotation);
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
            EnemyHunger=Mathf.Clamp01(EnemyHunger+dt*(.002f+velocity.magnitude*.0008f));
            Vector3 movement=velocity*dt;
            if(movement.sqrMagnitude>.0001f && EnvironmentSphereCast(transform.position,.48f,movement.normalized,movement.magnitude+.08f,out var obstacle))
            {
                transform.position=obstacle.point+obstacle.normal*.52f;
                velocity=Vector3.ProjectOnPlane(velocity,obstacle.normal)+obstacle.normal*1.5f;
            }
            else transform.position+=movement;
            if(EnvironmentRaycast(transform.position+Vector3.up*.5f,Vector3.down,2,out var floor) && Vector3.Dot(floor.normal,Vector3.up)>.65f)
            {
                float clearance=Vector3.Dot(transform.position-floor.point,floor.normal);
                if(clearance<.58f){transform.position+=floor.normal*(.58f-clearance);velocity=Vector3.ProjectOnPlane(velocity,floor.normal)+floor.normal*Mathf.Max(0,Vector3.Dot(velocity,floor.normal));}
            }
            Vector3 tip=LanceTip;
            if(player.Mounted && contactCooldown<=0 && velocity.magnitude>4)
            {
                float flyContact=DistanceToSegment(player.FlyHitPosition,lastLanceTip,tip);
                float riderContact=DistanceToSegment(player.RiderPosition+Vector3.up*.25f,lastLanceTip,tip);
                if(flyContact<.62f && flyContact<=riderContact){player.TakeFlyDamage(Mathf.Clamp(velocity.magnitude*5,18,45));contactCooldown=.6f;}
                else if(riderContact<.42f){player.ForceUnseat(velocity.normalized*3+Vector3.up*1.5f);contactCooldown=2;}
            }
            lastLanceTip=tip;
        }
        bool IsOwnCollider(Collider collider)
        { return collider && (collider.transform==transform || collider.transform.IsChildOf(transform)); }
        bool EnvironmentRaycast(Vector3 origin,Vector3 direction,float distance,out RaycastHit nearest)
        {
            nearest=default(RaycastHit);float best=float.PositiveInfinity;bool found=false;
            foreach(var hit in Physics.RaycastAll(origin,direction,distance,1,QueryTriggerInteraction.Ignore))
                if(!IsOwnCollider(hit.collider) && hit.distance<best){nearest=hit;best=hit.distance;found=true;}
            return found;
        }
        bool EnvironmentSphereCast(Vector3 origin,float radius,Vector3 direction,float distance,out RaycastHit nearest)
        {
            nearest=default(RaycastHit);float best=float.PositiveInfinity;bool found=false;
            foreach(var hit in Physics.SphereCastAll(origin,radius,direction,distance,1,QueryTriggerInteraction.Ignore))
                if(!IsOwnCollider(hit.collider) && hit.distance<best){nearest=hit;best=hit.distance;found=true;}
            return found;
        }
        static float DistanceToSegment(Vector3 point,Vector3 a,Vector3 b)
        { Vector3 ab=b-a;float t=Mathf.Clamp01(Vector3.Dot(point-a,ab)/Mathf.Max(.0001f,ab.sqrMagnitude));return Vector3.Distance(point,a+ab*t); }
        public bool ReceiveLanceContact(float impact)
        {
            if(!Mounted || impact<3)return false;
            Mounted=false;FlyEscaping=false;verticalSpeed=2;peakFallSpeed=0;
            if(riderVisual)riderVisual.EnterRagdoll(velocity+Vector3.up*2);
            if(flyVisual)
            {
                if(mountHealth && mountHealth.Health<=0)
                {
                    if(poseMirror)poseMirror.StopWings();
                    LastFlyCorpse=FlyCorpse.Create(flyVisual,velocity+flyDeathImpact,true);flyDeathImpact=Vector3.zero;
                    ReplacementMountScheduled=true;replacementMountTimer=6;
                    flyVisual.gameObject.SetActive(false);Destroy(flyVisual.gameObject);flyVisual=null;
                }
                else
                {
                    // The rider was unseated but the mount is alive. Previously this
                    // branch destroyed the fly visual, making a rider hit look like a
                    // killed fly with no corpse. Detach the living mount and let it make
                    // a readable escape instead.
                    FlyEscaping=true;flyVisual.SetParent(null,true);
                    var escape=flyVisual.gameObject.AddComponent<DetachedEnemyFly>();
                    escape.Initialize(this,player ? player.RiderPosition : transform.position-transform.forward,velocity);
                }
            }
            DropEnemyLance();
            feet.enabled=true;return true;
        }
        void DropEnemyLance()
        {
            if(!lance)return;lance.SetParent(null,true);
            if(!lance.GetComponent<Collider>())lance.gameObject.AddComponent<BoxCollider>();
            var body=lance.gameObject.AddComponent<Rigidbody>();body.mass=.7f;body.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;LastDroppedLance=body;
            body.velocity=velocity;body.angularVelocity=transform.right*1.2f;Destroy(lance.gameObject,15);lance=null;
        }
        public bool ReceiveFlyLanceContact(float impact,Vector3 impactDirection=default(Vector3))
        {
            if(!Mounted || !mountHealth)return false;
            Vector3 planarImpact=Vector3.ProjectOnPlane(impactDirection,Vector3.up);
            flyDeathImpact=planarImpact.sqrMagnitude>.001f ? planarImpact.normalized*Mathf.Clamp(impact*.22f,0,2) : Vector3.zero;
            if(mountHealth.Health<=0)return ReceiveLanceContact(Mathf.Max(impact,3));
            contactCooldown=.35f;return true;
        }
        public void FinishDetachedFlyEscape(){FlyEscaping=false;flyVisual=null;poseMirror=null;}
        public bool RiderReadyForFlyReturn { get { return health && health.Health>0 && groundAI; } }
        public void SetEnemyHunger(float value){EnemyHunger=Mathf.Clamp01(value);}
        public void FeedEnemyFly(float nutrition){EnemyHunger=Mathf.Max(0,EnemyHunger-Mathf.Max(0,nutrition));}
        public bool RemountReturnedFly(Transform returnedFly)
        {
            if(!returnedFly || !RiderReadyForFlyReturn)return false;
            if(riderVisual)riderVisual.BeginMountTransition(true);
            if(groundAI){groundAI.enabled=false;Destroy(groundAI);groundAI=null;}
            transform.position=returnedFly.position;Vector3 forward=Vector3.ProjectOnPlane(returnedFly.forward,Vector3.up);
            if(forward.sqrMagnitude>.01f)transform.rotation=Quaternion.LookRotation(forward,Vector3.up);
            returnedFly.SetParent(transform,false);returnedFly.localPosition=flyMountedLocalPosition;
            returnedFly.localRotation=flyMountedLocalRotation;returnedFly.localScale=flyMountedLocalScale;flyVisual=returnedFly;
            foreach(var collider in returnedFly.GetComponentsInChildren<Collider>())collider.enabled=true;
            FlyEscaping=false;Mounted=true;feet.enabled=false;velocity=Vector3.zero;verticalSpeed=peakFallSpeed=0;contactCooldown=1;
            BuildLance();if(riderVisual)riderVisual.Pose(riderAnchor ? riderAnchor : transform,true,0);
            lastLanceTip=LanceTip;ReturnedRemountCount++;return true;
        }
        void Fall(float dt)
        {
            if(feet.isGrounded && verticalSpeed<=0)
            {
                LastFallDamage=Mathf.Clamp((peakFallSpeed-4)*5,0,30);if(LastFallDamage>0)health.Hit(LastFallDamage);
                if(health.Health>0 && riderVisual){riderVisual.ExitRagdoll();riderVisual.BeginMountTransition(false);}
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
                Transform part=flyVisual ? FindPart(flyVisual,partName) : null;Renderer partRenderer=part ? part.GetComponent<Renderer>() : null;
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

    public sealed class DetachedEnemyFly : MonoBehaviour
    {
        MountedJoustOpponent owner;Vector3 velocity,direction,circleCenter;float age,circleAngle,feedTime;FlyFood food;
        public void Initialize(MountedJoustOpponent source,Vector3 threat,Vector3 inheritedVelocity)
        {
            owner=source;direction=Vector3.ProjectOnPlane(transform.position-threat,Vector3.up).normalized;
            if(direction.sqrMagnitude<.1f)direction=Vector3.ProjectOnPlane(transform.forward,Vector3.up).normalized;
            Vector3 planar=Vector3.ProjectOnPlane(inheritedVelocity,Vector3.up);
            velocity=(planar.sqrMagnitude>.01f ? planar.normalized : direction)*Mathf.Min(3.5f,Mathf.Max(1.5f,planar.magnitude));
            circleCenter=transform.position+Vector3.up*1.25f;
            if(owner && owner.EnemyHunger>=.6f)
            {
                float nearest=12;foreach(var candidate in FindObjectsOfType<FlyFood>())if(candidate.Available)
                {float distance=Vector3.Distance(transform.position,candidate.transform.position);if(distance<nearest){nearest=distance;food=candidate;}}
            }
            foreach(var collider in GetComponentsInChildren<Collider>())collider.enabled=false;
        }
        void Update()
        {
            float dt=Time.deltaTime;age+=dt;
            Vector3 target;
            if(age<2.5f || (owner && !owner.RiderReadyForFlyReturn && age<12))
            {
                circleAngle+=dt*1.35f;target=circleCenter+new Vector3(Mathf.Cos(circleAngle)*2.5f,.35f*Mathf.Sin(circleAngle*.5f),Mathf.Sin(circleAngle)*2.5f);
            }
            else if(food && food.Available && owner && owner.EnemyHunger>.25f && feedTime<4)
            {
                target=food.transform.position+Vector3.up*.35f;
                if(Vector3.Distance(transform.position,target)<.65f){owner.FeedEnemyFly(food.Consume(dt));feedTime+=dt;velocity*=.7f;}
            }
            else if(owner)target=owner.transform.position+Vector3.up*.55f;
            else target=transform.position+direction*3;
            Vector3 toTarget=target-transform.position;Vector3 desired=toTarget.sqrMagnitude>.01f ? toTarget.normalized*3.2f : Vector3.zero;
            velocity=Vector3.Lerp(velocity,desired,1-Mathf.Exp(-1.8f*dt));
            if(velocity.sqrMagnitude>.01f)transform.rotation=Quaternion.RotateTowards(transform.rotation,Quaternion.LookRotation(velocity.normalized,Vector3.up),55*dt);
            transform.position+=velocity*dt;
            bool finishedFeeding=!food || !food.Available || !owner || owner.EnemyHunger<=.25f || feedTime>=4;
            if(age>=2.5f && finishedFeeding && owner && owner.RiderReadyForFlyReturn && Vector3.Distance(transform.position,target)<.7f)
            {if(owner.RemountReturnedFly(transform)){Destroy(this);return;}}
            if(age<20)return;if(owner)owner.FinishDetachedFlyEscape();Destroy(gameObject);
        }
    }

    // The detailed fly is assembled from 69 independently animated biomodel parts.
    // Keep an enemy clone in the same measured pose instead of freezing one spawn frame.
    [DefaultExecutionOrder(100)]
    sealed class BiologicalPoseMirror : MonoBehaviour
    {
        [System.Serializable] sealed class WingProfile { public float[] angles_degrees; public float display_frequency_hz=18; }
        Transform source;
        Transform[] sourceParts,targetParts;
        Quaternion[] previousRotations,restRotations;
        WingProfile wingProfile;float wingClock,wingSpeedScale;
        MountedJoustOpponent owner;
        public float MaximumWingMotion { get; private set; }
        public float WingSpeedScale { get { return wingSpeedScale; } }
        public void Initialize(Transform template)
        {
            source=template;owner=GetComponentInParent<MountedJoustOpponent>();
            var wingAsset=Resources.Load<TextAsset>("FlyWingAnimationProfile");if(wingAsset)wingProfile=JsonUtility.FromJson<WingProfile>(wingAsset.text);
            sourceParts=new Transform[source.childCount];targetParts=new Transform[transform.childCount];previousRotations=new Quaternion[transform.childCount];restRotations=new Quaternion[transform.childCount];
            for(int i=0;i<sourceParts.Length;i++)sourceParts[i]=source.GetChild(i);
            for(int i=0;i<targetParts.Length;i++){targetParts[i]=transform.GetChild(i);previousRotations[i]=restRotations[i]=targetParts[i].localRotation;}
            CopyPose();
        }
        void LateUpdate(){CopyPose();}
        void CopyPose()
        {
            if(!source || sourceParts==null)return;int count=Mathf.Min(sourceParts.Length,targetParts.Length);
            float speed=owner ? owner.CurrentVelocity.magnitude : 0;
            bool alive=owner && (owner.Mounted || owner.FlyEscaping) && owner.FlyHealth && owner.FlyHealth.Health>0;
            wingSpeedScale=alive ? Mathf.Lerp(.35f,1.35f,Mathf.InverseLerp(.5f,8f,speed)) : 0;
            if(wingSpeedScale>0)wingClock=Mathf.Repeat(wingClock+Time.deltaTime*(wingProfile!=null ? wingProfile.display_frequency_hz : 18)*wingSpeedScale,1);
            Vector3 measured=SampleWing(wingClock)*Mathf.Lerp(.45f,1f,Mathf.InverseLerp(.5f,7f,speed));
            for(int i=0;i<count;i++)
            {
                if(!sourceParts[i] || !targetParts[i])continue;
                targetParts[i].localPosition=sourceParts[i].localPosition;
                bool wing=targetParts[i].name.Contains("Wing");
                if(wing)
                {
                    float side=targetParts[i].name.Contains("LWing") ? 1 : -1;
                    targetParts[i].localRotation=wingSpeedScale<=0 ? restRotations[i] :
                        Quaternion.AngleAxis(side*(50+measured.x),Vector3.up)*
                        Quaternion.AngleAxis(measured.y,Vector3.forward)*
                        Quaternion.AngleAxis(side*measured.z,Vector3.right)*restRotations[i];
                }
                else targetParts[i].localRotation=sourceParts[i].localRotation;
                targetParts[i].localScale=sourceParts[i].localScale;
                targetParts[i].gameObject.SetActive(sourceParts[i].gameObject.activeSelf);
                if(wing)MaximumWingMotion=Mathf.Max(MaximumWingMotion,Quaternion.Angle(previousRotations[i],targetParts[i].localRotation));
                previousRotations[i]=targetParts[i].localRotation;
            }
        }
        Vector3 SampleWing(float phase)
        {
            if(wingProfile==null || wingProfile.angles_degrees==null || wingProfile.angles_degrees.Length<6)return new Vector3(Mathf.Sin(phase*Mathf.PI*2)*45,0,0);
            int count=wingProfile.angles_degrees.Length/3;float sample=phase*count;int a=Mathf.FloorToInt(sample)%count,b=(a+1)%count;float t=sample-Mathf.Floor(sample);
            int x=a*3,y=b*3;return Vector3.Lerp(new Vector3(wingProfile.angles_degrees[x],wingProfile.angles_degrees[x+1],wingProfile.angles_degrees[x+2]),new Vector3(wingProfile.angles_degrees[y],wingProfile.angles_degrees[y+1],wingProfile.angles_degrees[y+2]),t);
        }
        public void StopWings(){wingSpeedScale=0;for(int i=0;i<targetParts.Length;i++)if(targetParts[i] && targetParts[i].name.Contains("Wing")){targetParts[i].localRotation=restRotations[i];previousRotations[i]=restRotations[i];}}
    }
}


