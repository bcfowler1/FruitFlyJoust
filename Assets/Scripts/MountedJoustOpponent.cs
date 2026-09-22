using System.Collections;
using System.IO;
using System.IO.Compression;
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
        public int UnseatingCount { get; private set; }
        public bool RiderRagdolled { get { return riderVisual && riderVisual.Ragdolled; } }
        public int RiderRagdollBodyCount { get { return riderVisual ? riderVisual.RagdollBodyCount : 0; } }
        public FlyCorpse LastFlyCorpse { get; private set; }
        public Vector3 CurrentVelocity { get { return velocity; } }
        public bool FlyEscaping { get; private set; }
        public bool ReplacementMountScheduled { get; private set; }
        public float EnemyHunger { get; private set; }=.35f;
        public float HaltereIntegrity { get; private set; }=1;
        Vector3 scentSearchPoint;float scentMemory;
        public bool LoomingDodging { get; private set; }
        public bool ScentSearching { get { return scentMemory>0; } }
        public float EnemyTopSpeed { get { return Mathf.Lerp(3.5f,8f,Competence)*FlyMotor.SpeedMultiplierForHunger(EnemyHunger); } }
        public int ReturnedRemountCount { get; private set; }
        public Rigidbody LastDroppedLance { get; private set; }
        public Vector3 LastRespawnPosition { get; private set; }
        public float LanceGripError { get { return lance && riderVisual && riderVisual.Hand(false) ? LanceGeometry.GripError(lance,riderVisual.Hand(false).position) : float.PositiveInfinity; } }
        public float LanceReach { get { return lance && riderVisual && riderVisual.Hand(false) ? Vector3.Distance(riderVisual.Hand(false).position,LanceTip) : 0; } }
        public Vector3 RenderedRiderPosition { get { return riderVisual ? riderVisual.RagdollCenter : transform.position; } }
        public Vector3 RiderWorldScale { get { return riderVisual ? riderVisual.VisualWorldScale : Vector3.zero; } }
        public float RiderVisualHeight { get { return riderVisual ? riderVisual.VisualHeight : 0; } }
        public int FlyVisibleRendererCount
        {
            get{int count=0;if(flyVisual)foreach(var renderer in flyVisual.GetComponentsInChildren<Renderer>())if(renderer.enabled&&renderer.gameObject.activeInHierarchy)count++;return count;}
        }
        public float FlyVisualBoundsSize
        {
            get
            {
                if(!flyVisual)return 0;var renderers=flyVisual.GetComponentsInChildren<Renderer>();if(renderers.Length==0)return 0;
                Bounds bounds=renderers[0].bounds;foreach(var renderer in renderers)if(renderer.enabled)bounds.Encapsulate(renderer.bounds);return bounds.size.magnitude;
            }
        }
        public bool RiderTransitioning { get { return riderVisual && riderVisual.Transitioning; } }
        public float GroundClearance
        {
            get
            {
                return EnvironmentRaycast(transform.position+Vector3.up*.5f,Vector3.down,100,out var hit) && Vector3.Dot(hit.normal,Vector3.up)>.65f ?
                    Vector3.Dot(transform.position-hit.point,hit.normal) : float.PositiveInfinity;
            }
        }
        public float FallVerticalSpeed { get { return verticalSpeed; } }
        public bool FeetGrounded { get { return feet && feet.isGrounded; } }
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
        float clock,contactCooldown,verticalSpeed,peakFallSpeed,respawnTimer=-1,replacementMountTimer=-1,groundedRecoveryTimer=-1;

        public void Initialize(RiderCombat rider,GameObject biologicalVisual,Transform saddleTemplate,Transform rideRoot,Material sharedRiderMaterial,Material sharedWeaponMaterial,Vector3 position)
        {
            player=rider;biologicalTemplate=biologicalVisual;riderMaterial=sharedRiderMaterial;weaponMaterial=sharedWeaponMaterial;spawn=position;
            NormalizeRootScale();
            if(biologicalTemplate)
            {
                flyTemplateLocalPosition=biologicalTemplate.transform.localPosition;
                flyTemplateLocalRotation=biologicalTemplate.transform.localRotation;
                flyTemplateLocalScale=biologicalTemplate.transform.localScale;
            }
            health=GetComponent<CombatTarget>();
            var placeholder=GetComponent<Renderer>();if(placeholder)placeholder.enabled=false;
            var primitiveCollider=GetComponent<CapsuleCollider>();if(primitiveCollider)Destroy(primitiveCollider);
            feet=gameObject.AddComponent<CharacterController>();feet.height=1.2f*RiderCombat.RiderBodyScale;feet.radius=.25f*RiderCombat.RiderBodyScale;feet.center=new Vector3(0,.6f*RiderCombat.RiderBodyScale,0);feet.enabled=false;
            riderAnchor=new GameObject("Enemy saddle").transform;riderAnchor.SetParent(transform,false);
            if(saddleTemplate && rideRoot)
            {
                riderAnchor.localPosition=rideRoot.InverseTransformPoint(saddleTemplate.position);
                riderAnchor.localRotation=Quaternion.Inverse(rideRoot.rotation)*saddleTemplate.rotation;
                Vector3 rootScale=transform.lossyScale;
                // The saddle transform supplies placement and rotation only. Its
                // authored tack scale must never resize the humanoid rider.
                riderAnchor.localScale=new Vector3(1/Mathf.Max(.0001f,rootScale.x),1/Mathf.Max(.0001f,rootScale.y),1/Mathf.Max(.0001f,rootScale.z));
            }
            saddleBasePosition=riderAnchor.localPosition;saddleBaseRotation=riderAnchor.localRotation;saddleBaseScale=riderAnchor.localScale;
            riderVisual=gameObject.AddComponent<RiderAnimationVisual>();riderVisual.visualScale=RiderCombat.CanonicalRiderVisualScale;riderVisual.mountedSeatHeight=-.33f;riderVisual.mountedSeatForward=-.16f;riderVisual.mountTransitions=true;
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
                // A newly instantiated rider starts at its prefab origin. Pose it
                // on the saddle before measuring the visible hips; otherwise the
                // first correction of each respawn uses an unposed skeleton.
                if(riderVisual)riderVisual.Pose(riderAnchor,true,0);
                CenterRiderOnThoraxAxis();
                // Keep the authored seat as the rebuild baseline. Saving this
                // corrected position would apply the correction again on every
                // respawn and gradually move the rider away from the thorax.
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
        void CenterRiderOnThoraxAxis(float maximumCorrection=float.PositiveInfinity)
        {
            if(!flyVisual || !riderAnchor || !riderVisual)return;
            Transform thorax=FindPart(flyVisual,"0/Thorax");
            Renderer thoraxRenderer=thorax ? thorax.GetComponent<Renderer>() : null;
            Vector3 forward=VisibleLongitudinalDirection();
            if(!thoraxRenderer || forward.sqrMagnitude<.5f)return;
            forward=Vector3.ProjectOnPlane(forward,transform.up).normalized;
            Vector3 right=Vector3.Cross(transform.up,forward).normalized;
            // The imported humanoid root pivot is offset from its seated pelvis.
            // Align the actual hips, which are the visible saddle contact point.
            float lateral=Vector3.Dot(riderVisual.RagdollCenter-thoraxRenderer.bounds.center,right);
            riderAnchor.position-=right*Mathf.Clamp(lateral,-maximumCorrection,maximumCorrection);
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
            if(hand)LanceGeometry.NormalizeReach(lance,lance,hand.position,
                riderAnchor ? riderAnchor.rotation : transform.rotation,LanceGeometry.CanonicalReach);
        }
        void BuildHitZones()
        {
            var riderZone=new GameObject("Enemy rider hitbox");riderZone.transform.SetParent(riderAnchor,false);
            riderZone.transform.localPosition=new Vector3(0,.28f,-.03f)*RiderCombat.FlyAssemblyScale;
            var riderCollider=riderZone.AddComponent<CapsuleCollider>();riderCollider.direction=1;riderCollider.center=new Vector3(0,.35f,0)*RiderCombat.FlyAssemblyScale;riderCollider.height=1.25f*RiderCombat.FlyAssemblyScale;riderCollider.radius=.28f*RiderCombat.FlyAssemblyScale;
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
            for(int side=-1;side<=1;side+=2)
            {
                var zone=new GameObject(side<0 ? "Left haltere hitbox" : "Right haltere hitbox");zone.transform.SetParent(flyVisual ? flyVisual : transform,false);
                zone.transform.localPosition=new Vector3(side*.24f,.02f,-.28f);var sphere=zone.AddComponent<SphereCollider>();sphere.radius=.1f;sphere.isTrigger=true;
                var sensor=zone.AddComponent<CombatTarget>();sensor.maximumHealth=30;sensor.ResetTarget();var relay=zone.AddComponent<HaltereHitZone>();relay.enemyFly=this;
                var hit=zone.AddComponent<MountedHitZone>();hit.owner=this;hit.fly=true;hit.haltere=true;
            }
        }
        void ResetPose()
        {
            NormalizeRootScale();
            feet.enabled=false;transform.position=spawn;transform.rotation=Quaternion.LookRotation(player ? -player.transform.forward : Vector3.back);
            Mounted=true;FlyEscaping=false;ReplacementMountScheduled=false;velocity=Vector3.zero;verticalSpeed=peakFallSpeed=0;contactCooldown=1;respawnTimer=replacementMountTimer=-1;
            health.ResetTarget();if(mountHealth)mountHealth.ResetTarget();HaltereIntegrity=1;scentMemory=0;LoomingDodging=false;lastLanceTip=LanceTip;
        }
        void NormalizeRootScale()
        {
            Vector3 parentScale=transform.parent ? transform.parent.lossyScale : Vector3.one;
            transform.localScale=new Vector3(1/Mathf.Max(.0001f,Mathf.Abs(parentScale.x)),
                1/Mathf.Max(.0001f,Mathf.Abs(parentScale.y)),
                1/Mathf.Max(.0001f,Mathf.Abs(parentScale.z)));
        }
        public Vector3 LanceTip
        {
            get
            {
                return lance ? LanceGeometry.TipPoint(lance,lance) : transform.position;
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
        public float RiderThoraxLateralOffset
        {
            get
            {
                if(!flyVisual || !riderVisual)return float.PositiveInfinity;
                Transform thorax=FindPart(flyVisual,"0/Thorax");Renderer renderer=thorax ? thorax.GetComponent<Renderer>() : null;
                Vector3 forward=VisibleLongitudinalDirection();
                if(!renderer || forward.sqrMagnitude<.5f)return float.PositiveInfinity;
                forward=Vector3.ProjectOnPlane(forward,transform.up).normalized;
                Vector3 right=Vector3.Cross(transform.up,forward).normalized;
                return Vector3.Dot(riderVisual.RagdollCenter-renderer.bounds.center,right);
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
            if(!player)return;
            if(FlyMotor.TryArenaCeiling(out var innerCeiling) && transform.position.y>innerCeiling)
            {
                transform.position=new Vector3(transform.position.x,innerCeiling,transform.position.z);
                velocity=new Vector3(velocity.x,Mathf.Min(-2,velocity.y),velocity.z);
                verticalSpeed=Mathf.Min(verticalSpeed,-2);
            }
            // Death and unseating must finish even if another system temporarily
            // pauses normal combat. Otherwise a zero-health rider remains welded to
            // the saddle until combat resumes.
            bool forcedTransition=(health && health.Health<=0) || (Mounted && mountHealth && mountHealth.Health<=0);
            // A disconnected research stream is not an intentional gameplay pause:
            // keep opponents alive on Unity's clock so they still pursue and attack.
            bool deliberatePause=player.research && player.research.Connected && player.CombatPaused;
            if(deliberatePause && !forcedTransition)return;
            float dt=player.research && player.research.Connected && !player.CombatPaused ? player.CombatDeltaTime : Time.deltaTime;
            if(dt<=0)return;
            if(health.Health<=0)
            {
                if(respawnTimer<0)
                {
                    FlyEscaping=Mounted && flyVisual && mountHealth && mountHealth.Health>0;
                    respawnTimer=FlyEscaping ? 6 : 3;Mounted=false;
                    if(groundAI)groundAI.enabled=false;
                    var riderCollider=health.GetComponent<Collider>();if(riderCollider)riderCollider.enabled=false;
                    // A rider-only kill slumps sideways and down from the saddle. The
                    // living fly keeps its independent health and continues escaping.
                    if(riderVisual)riderVisual.EnterRagdoll(velocity*.55f+transform.right*.45f+Vector3.down*.35f);
                    if(lance){lance.gameObject.SetActive(false);Destroy(lance.gameObject);lance=null;}
                }
                if(FlyEscaping)EscapeFly(dt);
                respawnTimer-=dt;
                if(respawnTimer<=0)
                {
                    // Do not make a living mount disappear on camera when only its rider
                    // was killed. Continue the escape until the fly is out of view.
                    if(FlyEscaping && player && player.PlayerCameraCanSee(transform.position,1.25f))respawnTimer=.5f;
                    else RespawnStronger();
                }
                return;
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
            if(movement.sqrMagnitude>.0001f && EnvironmentSphereCast(transform.position,.48f*RiderCombat.FlyAssemblyScale,movement.normalized,movement.magnitude+.08f,out var obstacle))
            {
                velocity=Vector3.ProjectOnPlane(velocity,obstacle.normal)+obstacle.normal*1.2f;
                Vector3 resolved=obstacle.point+obstacle.normal*(.52f*RiderCombat.FlyAssemblyScale);
                transform.position=Vector3.MoveTowards(transform.position,resolved,movement.magnitude+.08f);
            }
            else transform.position+=movement;
        }
        void LateUpdate()
        {
            if(riderVisual)riderVisual.AdvanceTransition(player ? player.CombatDeltaTime : Time.deltaTime);
            if(riderVisual)riderVisual.Pose(Mounted && riderAnchor ? riderAnchor : transform,Mounted,Mounted ? 0 : velocity.magnitude,health && health.Health<=0);
            // Pose evaluation can move the humanoid pelvis away from its imported
            // root pivot during the initial cross-fade. Recenter after animation so
            // the visible seated pelvis, rather than the FBX pivot, stays on-axis.
            if(Mounted && riderVisual && !riderVisual.Transitioning)CenterRiderOnThoraxAxis(.12f);
            if(riderVisual && riderVisual.Ragdolled)riderVisual.KeepRagdollAboveGround();
            Transform hand=riderVisual ? riderVisual.Hand(false) : null;
            if(Mounted && lance && hand)
            {
                Quaternion couch=riderAnchor ? riderAnchor.rotation : transform.rotation;
                Quaternion rotation=LanceGeometry.RaisedForWall(lance,lance,hand.position,couch,transform);
                LanceGeometry.NormalizeReach(lance,lance,hand.position,rotation,LanceGeometry.CanonicalReach);
            }
        }
        void Fly(float dt)
        {
            clock+=dt;float skill=Competence;
            Vector3 target=player.RiderPosition+Vector3.up*(player.Mounted ? .2f : 1.1f);
            scentMemory=Mathf.Max(0,scentMemory-dt);
            foreach(var bait in FindObjectsOfType<ScentedBait>())if(bait.Contains(transform.position)){scentSearchPoint=bait.transform.position;scentMemory=8;}
            if(scentMemory>0)target=scentSearchPoint+Vector3.up*.6f;
            float miss=(1-skill)*3.4f;
            Vector3 aim=target+transform.right*Mathf.Sin(clock*.73f+1.1f)*miss+Vector3.up*Mathf.Sin(clock*.41f)*miss*.3f;
            Vector3 desired=(aim-transform.position).normalized;
            Vector3 playerApproach=player.RiderPosition-transform.position;
            Vector3 relativeVelocity=player.RideWorldVelocity-velocity;
            float closing=-Vector3.Dot(relativeVelocity,playerApproach.normalized);
            float angularExpansion=closing/Mathf.Max(.25f,playerApproach.magnitude);
            LoomingDodging=playerApproach.magnitude<7 && angularExpansion>.35f;
            if(LoomingDodging)
                desired=(desired+transform.right*(Mathf.Sign(Vector3.Dot(playerApproach,transform.right))>=0 ? -1 : 1)*Mathf.Lerp(.35f,1.1f,Competence)*HaltereIntegrity).normalized;
            // Begin steering away while the couched lance still has clearance;
            // body-only collision detection reacts several metres too late.
            if(StaticEnvironmentAhead(transform.position+Vector3.up*.15f,transform.forward,Mathf.Clamp(LanceReach,1,4),out var lanceObstacle))
                desired=(desired+lanceObstacle.normal*1.35f+Vector3.up*.3f).normalized;
            transform.rotation=Quaternion.RotateTowards(transform.rotation,Quaternion.LookRotation(desired,Vector3.up),Mathf.Lerp(35,125,skill)*Mathf.Lerp(.55f,1,HaltereIntegrity)*dt);
            velocity=Vector3.Lerp(velocity,transform.forward*(Mathf.Lerp(3.5f,8f,skill)*FlyMotor.SpeedMultiplierForHunger(EnemyHunger)),1-Mathf.Exp(-2.5f*dt));
            EnemyHunger=Mathf.Clamp01(EnemyHunger+dt*(.002f+velocity.magnitude*.0008f));
            Vector3 movement=velocity*dt;
            if(movement.sqrMagnitude>.0001f && EnvironmentSphereCast(transform.position,.48f*RiderCombat.FlyAssemblyScale,movement.normalized,movement.magnitude+.08f,out var obstacle))
            {
                Vector3 resolved=obstacle.point+obstacle.normal*(.52f*RiderCombat.FlyAssemblyScale);
                // A cast hit point is not a valid new root position when a collider
                // starts overlapped or reports an unusual contact point. Bound the
                // visible step by the distance this fly could travel this frame.
                transform.position=Vector3.MoveTowards(transform.position,resolved,movement.magnitude+.08f);
                velocity=Vector3.ProjectOnPlane(velocity,obstacle.normal)+obstacle.normal*1.5f;
            }
            else transform.position+=movement;
            if(EnvironmentRaycast(transform.position+Vector3.up*.5f,Vector3.down,2,out var floor) && Vector3.Dot(floor.normal,Vector3.up)>.65f)
            {
                float clearance=Vector3.Dot(transform.position-floor.point,floor.normal);
                float minimumClearance=.58f*RiderCombat.FlyAssemblyScale;
                if(clearance<minimumClearance){transform.position+=floor.normal*(minimumClearance-clearance);velocity=Vector3.ProjectOnPlane(velocity,floor.normal)+floor.normal*Mathf.Max(0,Vector3.Dot(velocity,floor.normal));}
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
        bool StaticEnvironmentAhead(Vector3 origin,Vector3 direction,float distance,out RaycastHit nearest)
        {
            nearest=default(RaycastHit);float best=float.PositiveInfinity;bool found=false;
            foreach(var hit in Physics.SphereCastAll(origin,.2f,direction,distance,1,QueryTriggerInteraction.Ignore))
            {
                if(IsOwnCollider(hit.collider) || hit.collider.attachedRigidbody ||
                   hit.collider.GetComponentInParent<CharacterController>() || hit.collider.GetComponentInParent<CombatTarget>())continue;
                if(hit.distance<best){nearest=hit;best=hit.distance;found=true;}
            }
            return found;
        }
        bool HasStableWalkableSupport()
        {
            if(!feet || !feet.enabled)return false;
            if(!EnvironmentRaycast(transform.position+Vector3.up*.18f,Vector3.down,.5f,out var hit))return false;
            if(Vector3.Dot(hit.normal,Vector3.up)<.65f || hit.collider.attachedRigidbody)return false;
            if(hit.collider.GetComponentInParent<CharacterController>() || hit.collider.GetComponentInParent<MountedJoustOpponent>())return false;
            float clearance=Vector3.Dot(transform.position-hit.point,hit.normal);
            return clearance>=-.04f && clearance<=.22f;
        }
        static float DistanceToSegment(Vector3 point,Vector3 a,Vector3 b)
        { Vector3 ab=b-a;float t=Mathf.Clamp01(Vector3.Dot(point-a,ab)/Mathf.Max(.0001f,ab.sqrMagnitude));return Vector3.Distance(point,a+ab*t); }
        public bool ReceiveLanceContact(float impact)
        {
            if(!Mounted || impact<3)return false;
            Mounted=false;FlyEscaping=false;verticalSpeed=2;peakFallSpeed=0;groundedRecoveryTimer=-1;UnseatingCount++;
            // Unseating is cumulative trauma, not a life counter. Three otherwise
            // clean unseatings exhaust full health; existing arrow wounds and the
            // severity-based landing damage can make an earlier fall fatal.
            if(health && health.Health>0)health.Hit(health.maximumHealth/3f+.01f);
            if(riderVisual)
            {
                Vector3 fallVelocity=velocity;fallVelocity.y=Mathf.Clamp(fallVelocity.y+1,-2.5f,1.25f);
                riderVisual.EnterRagdoll(fallVelocity);
            }
            if(flyVisual)
            {
                if(mountHealth && mountHealth.Health<=0)
                {
                    if(poseMirror)poseMirror.StopWings();
                    Vector3 corpseVelocity=velocity+flyDeathImpact;
                    if(flyDeathImpact.sqrMagnitude>.0001f)
                    {
                        Vector3 impactAxis=flyDeathImpact.normalized;
                        float along=Vector3.Dot(Vector3.ProjectOnPlane(corpseVelocity,Vector3.up),impactAxis);
                        if(along<.25f)corpseVelocity+=impactAxis*(.25f-along);
                    }
                    LastFlyCorpse=FlyCorpse.Create(flyVisual,corpseVelocity,true);flyDeathImpact=Vector3.zero;
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
        public void DamageHaltere(float damage){HaltereIntegrity=Mathf.Clamp01(HaltereIntegrity-Mathf.Max(0,damage)/60f);}
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
            // Mounted opponents keep their CharacterController disabled. Any route
            // into an unmounted fall must reactivate it before Move is called.
            if(!feet)return;
            if(!feet.enabled)feet.enabled=true;
            // A fallen rider can land on the articulated fly corpse. Its moving
            // Rigidbody is excluded by the static support ray, while the character
            // controller correctly reports contact beneath its feet. Treat that
            // contact as a landing so recovery cannot stall above the arena floor.
            if(verticalSpeed<=0 && (HasStableWalkableSupport() || feet.isGrounded))
            {
                if(groundedRecoveryTimer<0)
                {
                    LastFallDamage=Mathf.Clamp((peakFallSpeed-4)*5,0,30);if(LastFallDamage>0)health.Hit(LastFallDamage);
                    groundedRecoveryTimer=health.Health>0 ? 1.5f : float.PositiveInfinity;
                    verticalSpeed=-2;velocity=Vector3.zero;
                }
                // Leave the articulated body visibly crumpled after impact. A living
                // rider recovers after a readable pause; a dead rider remains a corpse.
                if(health.Health<=0)return;
                groundedRecoveryTimer-=dt;if(groundedRecoveryTimer>0)return;
                if(riderVisual){riderVisual.ExitRagdoll();riderVisual.BeginMountTransition(false);}
                groundAI=gameObject.AddComponent<CombatOpponent>();groundAI.style=CombatOpponent.Style.Swordsman;
                groundAI.speed=Mathf.Lerp(1.2f,2.7f,Competence);groundAI.attackInterval=Mathf.Lerp(1.8f,.75f,Competence);return;
            }
            verticalSpeed+=Physics.gravity.y*dt;peakFallSpeed=Mathf.Max(peakFallSpeed,-verticalSpeed);
            velocity=Vector3.MoveTowards(velocity,Vector3.zero,dt*2);
            feet.Move((Vector3.ProjectOnPlane(velocity,Vector3.up)+Vector3.up*verticalSpeed)*dt);
        }
        MountedJoustOpponent RespawnStronger()
        {
            if(!player)return null;
            Vector3 nextSpawn=player.FindHiddenEnemyRespawn(spawn);
            var replacement=player.SpawnMountedJousterReplacement(transform.parent,nextSpawn,competencyLevel+1);
            replacement.RespawnCount=RespawnCount+1;
            replacement.LastRespawnPosition=nextSpawn;
            // A fallen articulated rider is already detached from this gameplay
            // root. Keep that corpse where it fell while the fresh prefab enters.
            gameObject.SetActive(false);Destroy(gameObject);
            return replacement;
        }
        public MountedJoustOpponent ValidationRespawn(){return RespawnStronger();}
#if UNITY_EDITOR
        IEnumerator CaptureRenderedOpponent()
        {
            while(poseMirror && !poseMirror.PoseCaptured)yield return null;
            yield return new WaitForEndOfFrame();
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
        public bool haltere;
    }

    public sealed class DetachedEnemyFly : MonoBehaviour
    {
        MountedJoustOpponent owner;Vector3 velocity,direction,circleCenter;float age,circleAngle,feedTime;FlyFood food;
        Vector3 scentSearchPoint;float scentMemory;
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
            // Script reloads, editor stalls, and breakpoint-like hitches can produce
            // a very large rendered-frame delta. Never integrate a detached mount
            // through that whole interval in one visible jump.
            float dt=Mathf.Min(Time.deltaTime,.05f);age+=dt;
            scentMemory=Mathf.Max(0,scentMemory-dt);
            foreach(var bait in FindObjectsOfType<ScentedBait>())if(bait.Contains(transform.position)){scentSearchPoint=bait.transform.position;scentMemory=8;}
            Vector3 target;
            if(scentMemory>0)target=scentSearchPoint+Vector3.up*.35f;
            else if(age<2.5f || (owner && !owner.RiderReadyForFlyReturn && age<12))
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
        [System.Serializable] sealed class GeometryPart { public string name;public float[] pivot; }
        [System.Serializable] sealed class GeometryPose { public float[] positions,rotations; }
        [System.Serializable] sealed class GeometryData { public GeometryPart[] geoms;public GeometryPose[] poses; }
        Transform source;
        Transform[] sourceParts,targetParts;
        Quaternion[] previousRotations,restRotations;
        Vector3[] restPositions,restScales;
        Vector3[] wingPivots;
        bool[] restActive;
        bool poseCaptured;
        float poseCaptureDelay;
        Renderer[] hiddenRenderers;
        bool[] hiddenRendererStates;
        WingProfile wingProfile;float wingClock,wingSpeedScale;
        MountedJoustOpponent owner;
        public float MaximumWingMotion { get; private set; }
        public float WingSpeedScale { get { return wingSpeedScale; } }
        public bool PoseCaptured { get { return poseCaptured; } }
        public void Initialize(Transform template)
        {
            source=template;owner=GetComponentInParent<MountedJoustOpponent>();
            var wingAsset=Resources.Load<TextAsset>("FlyWingAnimationProfile");if(wingAsset)wingProfile=JsonUtility.FromJson<WingProfile>(wingAsset.text);
            sourceParts=new Transform[source.childCount];targetParts=new Transform[transform.childCount];previousRotations=new Quaternion[transform.childCount];restRotations=new Quaternion[transform.childCount];
            restPositions=new Vector3[transform.childCount];restScales=new Vector3[transform.childCount];restActive=new bool[transform.childCount];
            wingPivots=new Vector3[transform.childCount];
            for(int i=0;i<sourceParts.Length;i++)sourceParts[i]=source.GetChild(i);
            for(int i=0;i<targetParts.Length;i++)
            {
                targetParts[i]=transform.GetChild(i);previousRotations[i]=restRotations[i]=targetParts[i].localRotation;
                restPositions[i]=targetParts[i].localPosition;restScales[i]=targetParts[i].localScale;restActive[i]=targetParts[i].gameObject.activeSelf;
            }
            LoadNeutralWingPose();
            hiddenRenderers=GetComponentsInChildren<Renderer>(true);hiddenRendererStates=new bool[hiddenRenderers.Length];
            for(int i=0;i<hiddenRenderers.Length;i++){hiddenRendererStates[i]=hiddenRenderers[i].enabled;hiddenRenderers[i].enabled=false;}
        }
        void LoadNeutralWingPose()
        {
            var asset=Resources.Load<TextAsset>("FlyPlayableGeometry");if(!asset)return;
            GeometryData geometry=null;
            using(var stream=new MemoryStream(asset.bytes))using(var gzip=new GZipStream(stream,CompressionMode.Decompress))using(var reader=new StreamReader(gzip))
                geometry=JsonUtility.FromJson<GeometryData>(reader.ReadToEnd());
            if(geometry==null || geometry.geoms==null || geometry.poses==null || geometry.poses.Length==0)return;
            var pose=geometry.poses[0];
            for(int i=0;i<targetParts.Length;i++)
            {
                if(!targetParts[i] || !targetParts[i].name.Contains("Wing"))continue;
                int part=-1;for(int j=0;j<geometry.geoms.Length;j++)if(geometry.geoms[j].name==targetParts[i].name){part=j;break;}
                if(part<0)continue;int p=part*3,q=part*4;
                if(p+2<pose.positions.Length)restPositions[i]=ResearchViewer.Position(pose.positions[p],pose.positions[p+1],pose.positions[p+2])*500;
                if(q+3<pose.rotations.Length)restRotations[i]=ResearchViewer.Rotation(pose.rotations[q],pose.rotations[q+1],pose.rotations[q+2],pose.rotations[q+3]);
                var pivot=geometry.geoms[part].pivot;if(pivot!=null && pivot.Length>=3)wingPivots[i]=ResearchViewer.Position(pivot[0],pivot[1],pivot[2])*500;
            }
        }
        void LateUpdate(){CopyPose();}
        void CaptureAssembledPose()
        {
            // MountedJoustOpponent is built during Start, before DetailedFlyVisual's
            // first LateUpdate has placed the 69 biological parts. Capture here
            // (execution order 100) after that placement; capturing in Initialize
            // froze unassembled abdomen transforms and visibly detached the body.
            int count=Mathf.Min(sourceParts.Length,targetParts.Length);
            for(int i=0;i<count;i++)
            {
                if(!sourceParts[i] || !targetParts[i])continue;
                bool wing=targetParts[i].name.Contains("Wing");
                if(!wing){restPositions[i]=sourceParts[i].localPosition;restRotations[i]=sourceParts[i].localRotation;}
                previousRotations[i]=restRotations[i];restScales[i]=sourceParts[i].localScale;
                restActive[i]=sourceParts[i].gameObject.activeSelf;
                targetParts[i].localPosition=restPositions[i];targetParts[i].localRotation=restRotations[i];targetParts[i].localScale=restScales[i];
                targetParts[i].gameObject.SetActive(restActive[i]);
            }
            // FlyTackFitter.OnEnable runs while the cloned thorax is still at its
            // pre-assembly transform, so armour fitted at that moment appears as a
            // detached abdominal shell. Refit after all biological parts are placed.
            var tack=GetComponent<FlyTackFitter>();if(tack)tack.Rebuild();
            for(int i=0;i<hiddenRenderers.Length;i++)if(hiddenRenderers[i])hiddenRenderers[i].enabled=hiddenRendererStates[i];
            poseCaptured=true;
        }
        void CopyPose()
        {
            if(!source || sourceParts==null)return;int count=Mathf.Min(sourceParts.Length,targetParts.Length);
            if(!poseCaptured)
            {
                poseCaptureDelay+=Time.deltaTime;
                // DetailedFlyVisual exponentially settles non-animated body parts.
                // Waiting 0.35 s leaves less than 0.2% of the initial transform error.
                if(poseCaptureDelay<.35f)return;
                CaptureAssembledPose();
            }
            float speed=owner ? owner.CurrentVelocity.magnitude : 0;
            bool alive=owner && (owner.Mounted || owner.FlyEscaping) && owner.FlyHealth && owner.FlyHealth.Health>0;
            float profileRate=wingProfile!=null ? wingProfile.display_frequency_hz : 18;
            float flapRate=alive ? Mathf.Lerp(12,38,Mathf.InverseLerp(.5f,8f,speed)) : 0;
            wingSpeedScale=flapRate/Mathf.Max(.01f,profileRate);
            if(wingSpeedScale>0)wingClock=Mathf.Repeat(wingClock+Time.deltaTime*flapRate,1);
            Vector3 measured=SampleWing(wingClock)*Mathf.Lerp(.45f,1f,Mathf.InverseLerp(.5f,7f,speed));
            for(int i=0;i<count;i++)
            {
                if(!sourceParts[i] || !targetParts[i])continue;
                // The clone starts from the biological model's measured pose, but
                // must not copy the player's live animation each frame. Its root,
                // wings and combat state are independently simulated.
                targetParts[i].localPosition=restPositions[i];
                bool wing=targetParts[i].name.Contains("Wing");
                if(wing)
                {
                    float side=targetParts[i].name.Contains("LWing") ? 1 : -1;
                    float stroke=(measured.x-15.2831f)*.78f;
                    float deviation=measured.y+4.566f;
                    float feather=(measured.z-40.6689f)*.30f;
                    Quaternion movement=Quaternion.AngleAxis(side*(50+stroke),Vector3.up)*
                        Quaternion.AngleAxis(deviation,Vector3.forward)*Quaternion.AngleAxis(side*feather,Vector3.right);
                    targetParts[i].localPosition=wingSpeedScale<=0 ? restPositions[i] : wingPivots[i]+movement*(restPositions[i]-wingPivots[i]);
                    targetParts[i].localRotation=wingSpeedScale<=0 ? restRotations[i] : movement*restRotations[i];
                }
                else targetParts[i].localRotation=restRotations[i];
                targetParts[i].localScale=restScales[i];
                targetParts[i].gameObject.SetActive(restActive[i]);
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


