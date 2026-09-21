using UnityEngine;

namespace FruitFlyJoust
{
    public sealed class RiderCombat : MonoBehaviour
    {
        public const float LegacyRiderVisualScale=.78f;
        public const float LegacyMountedRiderScale=.6421116f;
        public const float CanonicalRiderVisualScale=1f;
        public const float RiderBodyScale=CanonicalRiderVisualScale/LegacyRiderVisualScale;
        public const float FlyAssemblyScale=CanonicalRiderVisualScale/LegacyMountedRiderScale;
        public FlyMotor fly;
        public ResearchViewer research;
        public RiderAnimationVisual existingAnimation;
        public float CombatDeltaTime { get { return research ? research.BodyDeltaTime : Time.deltaTime; } }
        public bool CombatPaused { get { return research && (!research.Connected || research.CurrentFrame.paused || research.CurrentFrame.episode_ended); } }
        private RiderInput RideInput { get { return research ? research.riderAnchor.GetComponent<RiderInput>() : fly.rider; } }
        private Transform RideRoot { get { return research ? research.riderAnchor : fly.transform; } }
        private bool Perched { get { return research ? research.IsPerched : fly.Phase == RidePhase.Perched; } }
        private Vector3 RideVelocity { get { return research ? research.BodyVelocity : fly.GetComponent<Rigidbody>().velocity; } }
        private void ResetBody() { if (research) research.RequestReset(); else if (course) course.ResetCourse(); else fly.ResetRide(); }
        public Transform mountedVisual;
        public RiderCamera view;
        public PracticeCourse course;
        public Material riderMaterial, weaponMaterial;
        public enum Weapon { Sword, Bow, Lance }
        public enum SwordAttack { LeftToRight, RightToLeft, Thrust }
        public Weapon weapon = Weapon.Lance;
        public bool Mounted { get; private set; } = true;
        public float Health { get; private set; } = 100;
        public bool Defeated { get { return Health <= 0; } }
        public Vector3 RiderPosition { get { return Mounted ? saddle.position : avatar.position; } }
        public Transform BowVisual { get { return bowVisual; } }
        public string message = "Practice combat targets; research brain is separate.";
        private RiderInput footInput;
        private CharacterController feet;
        private Transform avatar, weaponVisual, bowVisual, swordVisual,lanceModel,stowedLance;
        public Rigidbody LastDroppedLance { get; private set; }
        public bool StowedLanceVisible { get { return stowedLance && stowedLance.gameObject.activeInHierarchy; } }
        [System.Serializable] public sealed class SwordHandPose { public Vector3 position=new Vector3(0,.02f,.03f);public Vector3 euler;public float scale=1; }
        public SwordHandPose swordHandPose=new SwordHandPose();
        public Transform SwordVisual { get { return swordVisual; } }
        private float falling, cooldown, charge;
        private Vector3 unseatVelocity;
        private bool unseatedFall;
        private float peakUnseatedFallSpeed;
        public float LastFallDamage { get; private set; }
        private bool attackBefore, bowBefore;
        private bool swordXBefore, swordBBefore;
        private float swordGesture;
        const float SwordGestureDuration=.55f;
        public SwordAttack LastSwordAttack { get; private set; }
        public float SwordThrustExtension { get; private set; }
        public float SwordForwardAlignment { get { return swordVisual && avatar ? Vector3.Dot(swordVisual.forward,(Mounted ? RideRoot : avatar).forward) : 0; } }
        public float SwordTipLateral { get { Transform frame=Mounted ? RideRoot : avatar;return frame ? frame.InverseTransformPoint(Tip()).x : 0; } }
        public float SwordGripError { get { return weaponVisual ? Vector3.Distance(weaponVisual.localPosition,swordHandPose.position) : float.PositiveInfinity; } }
        public float LanceGripError { get { return weapon==Weapon.Lance && lanceModel && animationVisual && animationVisual.Hand(false) ? LanceGeometry.GripError(lanceModel,animationVisual.Hand(false).position) : float.PositiveInfinity; } }
        public float LanceReach { get { return weapon==Weapon.Lance && animationVisual && animationVisual.Hand(false) ? Vector3.Distance(animationVisual.Hand(false).position,Tip()) : 0; } }
        public int HeldLanceColliderCount
        {
            get
            {
                if(!lanceModel)return 0;int count=0;
                foreach(var collider in lanceModel.GetComponentsInChildren<Collider>())if(collider && collider.enabled)count++;
                return count;
            }
        }
        private Vector3 lastTip;
        private float padInteractBefore, padWeaponBefore;
        private bool callBefore;
        private float tipSpeed;
        private Transform saddle;
        private RiderAnimationVisual animationVisual;
        private Vector3 animationPreviousPosition;
        private float animationSpeed;
        public float swordWindup = .18f;
        private float strikeDelay;
        private bool strikePending;
        private GameObject practice;
        private bool practiceEnemies;
        private bool standaloneJousterSpawned;
        private GameObject standaloneJouster;
        private CombatTarget playerFlyHealth;
        public float defeatRespawnDelay = 3;
        private float defeatRespawnTimer = -1;
        private Vector3 combatSpawnCenter;
        public int PlayerRespawnCount { get; private set; }
        public Vector3 LastPlayerRespawnPosition { get; private set; }
        public Vector3 LastEnemyRespawnPosition { get; private set; }
        public bool LastEnemyRespawnUsesCornerEntry { get; private set; }
        private int enemyRespawnSequence;
        public bool RiderRagdolled { get { return animationVisual && animationVisual.Ragdolled; } }
        public int RiderRagdollBodyCount { get { return animationVisual ? animationVisual.RagdollBodyCount : 0; } }
        public bool RiderTransitioning { get { return animationVisual && animationVisual.Transitioning; } }
        public Vector3 RenderedRiderPosition { get { return animationVisual ? animationVisual.RagdollCenter : RiderPosition; } }
        public float LastDismountLateral { get; private set; }
        public float RiderVisualHeight { get { return animationVisual ? animationVisual.VisualHeight : 0; } }
        public Vector3 RiderVisualWorldScale { get { return animationVisual ? animationVisual.VisualWorldScale : Vector3.zero; } }
        public float OnFootVisualScale { get { return animationVisual ? animationVisual.visualScale : CanonicalRiderVisualScale; } }
        public CombatTarget PlayerFlyHealth { get { return playerFlyHealth; } }
        public Vector3 FlyHitPosition { get { return playerFlyHealth ? playerFlyHealth.transform.position : RideRoot.position; } }

        void Start()
        {
            combatSpawnCenter = RideRoot.position;
            saddle = mountedVisual.parent;
            // The scientific fly and saddle were fitted around the saved .642 rider. Convert
            // their shared spatial frame once when moving the rider convention to 1.0.
            saddle.localPosition*=FlyAssemblyScale;
            var walker = new GameObject("Dismounted rider");
            walker.name = "Dismounted rider"; walker.layer = 2;
            avatar = walker.transform; avatar.localScale = Vector3.one;
            var figure = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Destroy(figure.GetComponent<Collider>()); figure.transform.SetParent(avatar, false);
            figure.transform.localPosition = new Vector3(0, .6f, 0);
            figure.transform.localScale = new Vector3(.4f, .6f, .4f);
            figure.GetComponent<Renderer>().sharedMaterial = riderMaterial;
            feet = walker.AddComponent<CharacterController>(); feet.height = 1.2f*RiderBodyScale; feet.radius = .2f*RiderBodyScale;
            feet.center = new Vector3(0, .6f*RiderBodyScale, 0);
            footInput = walker.AddComponent<RiderInput>(); walker.SetActive(false);
            var pole = GameObject.CreatePrimitive(PrimitiveType.Cube); pole.name = "Rider weapon";
            Destroy(pole.GetComponent<Collider>());
            pole.GetComponent<Renderer>().sharedMaterial = weaponMaterial;
            weaponVisual = pole.transform;
            BuildLanceVisual();
            BuildBowVisual();
            BuildSwordVisual();
            var swordPoseAsset=Resources.Load<TextAsset>("SwordHandPose");
            if(swordPoseAsset)swordHandPose=JsonUtility.FromJson<SwordHandPose>(swordPoseAsset.text);
            SetWeapon(); lastTip = Tip();
            animationVisual = existingAnimation ? existingAnimation : gameObject.AddComponent<RiderAnimationVisual>();
            if (existingAnimation || animationVisual.Create(saddle, riderMaterial))
            {
                animationVisual.mountTransitions=true;
                figure.GetComponent<Renderer>().enabled = false;
                var renderer = mountedVisual.GetComponent<Renderer>(); if (renderer) renderer.enabled = false;
                var head = saddle.Find("Rider head"); if (head && head.GetComponent<Renderer>()) head.GetComponent<Renderer>().enabled = false;
                animationVisual.Pose(saddle, true, 0);
            }
            SetWeapon();BuildPlayerFlyHealth();
        }
        void BuildPlayerFlyHealth()
        {
            var zone=new GameObject("Player fly hitbox");zone.transform.SetParent(RideRoot,false);zone.transform.localPosition=new Vector3(0,-.05f,0);
            var collider=zone.AddComponent<BoxCollider>();collider.size=new Vector3(1.15f,.8f,1.65f)*FlyAssemblyScale;collider.isTrigger=true;
            playerFlyHealth=zone.AddComponent<CombatTarget>();playerFlyHealth.maximumHealth=120;playerFlyHealth.ResetTarget();
        }
        public bool TakeFlyDamage(float damage)
        {
            if(!Mounted || !playerFlyHealth || !playerFlyHealth.Hit(damage))return false;
            message="Your fly took damage! "+Mathf.CeilToInt(playerFlyHealth.Health)+" HP";
            if(playerFlyHealth.Health<=0){fly.SpawnCorpse(RideVelocity);ForceUnseat(RideVelocity.normalized*2+Vector3.up*1.5f);message="Your fly was brought down!";}
            return true;
        }
        void LateUpdate()
        {
            if (!animationVisual || !avatar) return;
            if (CombatDeltaTime > 0) animationSpeed = (avatar.position - animationPreviousPosition).magnitude / Mathf.Max(.001f, CombatDeltaTime);
            animationPreviousPosition = avatar.position;
            animationVisual.AdvanceTransition(CombatDeltaTime);
            animationVisual.Pose(Mounted ? saddle : avatar, Mounted, animationSpeed, Defeated);
            if (research) animationVisual.SetClock(CombatPaused ? 0 : CombatDeltaTime);
            if(Mounted)animationVisual.StabilizeTorsoAgainstGravity(RideRoot);
            if(weapon==Weapon.Bow && view)
            {
                float aimWeight=charge>0 || bowBefore || cooldown>0 ? 1 : .35f;
                Transform frame=Mounted ? RideRoot : avatar;Vector3 aimDirection=view.transform.forward;
                animationVisual.AimUpperBody(frame,aimDirection,aimWeight);
                animationVisual.PoseBowAim(frame,aimDirection,aimWeight,charge);
                Transform leftHand=animationVisual.Hand(true);
                if(weaponVisual && leftHand)
                {
                    weaponVisual.position=leftHand.position+aimDirection.normalized*.08f;
                    weaponVisual.rotation=Quaternion.LookRotation(aimDirection,frame.up);
                }
            }
            SwordThrustExtension=0;
            if(weapon==Weapon.Sword)
            {
                // Reset the hand-authored grip every frame so a completed thrust cannot
                // leave the weapon displaced from the hand.
                ApplySwordHandPose();
                Quaternion guardWeaponRotation=weaponVisual ? weaponVisual.rotation : Quaternion.identity;
                if(swordGesture>0)
                {
                    float phase=1-Mathf.Clamp01(swordGesture/SwordGestureDuration);
                    float transition=AttackTransition(phase);
                    animationVisual.PoseSwordAttack(LastSwordAttack,swordGesture/SwordGestureDuration);
                    if(LastSwordAttack==SwordAttack.Thrust && weaponVisual)
                    {
                        float extension=Mathf.Sin(phase*Mathf.PI)*.52f*transition;
                        Transform frame=Mounted ? RideRoot : avatar;
                        Transform hand=animationVisual.Hand(false);
                        SwordThrustExtension=extension;
                        // The thrust blade follows the attack axis directly instead of
                        // inheriting the lateral arc used by the two cut animations.
                        weaponVisual.rotation=Quaternion.Slerp(guardWeaponRotation,Quaternion.LookRotation(frame.forward,frame.up),transition);
                        // Keep the hilt on the hand. RiderAnimationVisual advances the
                        // complete body/arm chain so the weapon never outruns its grip.
                    }
                    else if(weaponVisual)
                    {
                        float direction=LastSwordAttack==SwordAttack.LeftToRight ? 1 : -1;
                        float sweep=CutSweep(phase,direction);
                        Transform frame=Mounted ? RideRoot : avatar;
                        // Give each cut an explicit opposite blade path. The hilt remains
                        // attached to the posed hand while the blade travels across the body.
                        Vector3 bladeDirection=(frame.forward*.68f+frame.right*sweep*.66f+frame.up*.45f).normalized;
                        weaponVisual.rotation=Quaternion.Slerp(guardWeaponRotation,Quaternion.LookRotation(bladeDirection,frame.up),transition);
                    }
                }
            }
            if(view)
            {
                // In flight, follow the final animated head so banking feels embodied.
                // On a surface, use the rigid fly frame: feeding the looping Ride pose and
                // gravity-corrected spine back into the camera caused visible wall oscillation.
                if(Mounted && !Perched)view.SetStableOrientation(animationVisual.StableCameraRotation);
                else view.SetOrientationSource(Mounted ? RideRoot : null,RideRoot);
            }
            if (weapon == Weapon.Lance && Mounted && weaponVisual && animationVisual.Hand(false))
            {
                Vector3 hand=animationVisual.Hand(false).position;
                Quaternion rotation=LanceGeometry.RaisedForWall(lanceModel,weaponVisual,hand,RideRoot.rotation,transform);
                LanceGeometry.AlignGrip(lanceModel,weaponVisual,hand,rotation);
            }
        }
        void SetWeapon()
        {
            var placeholder=weaponVisual.GetComponent<Renderer>();
            if(placeholder)placeholder.enabled=false;
            if(lanceModel)lanceModel.gameObject.SetActive(weapon==Weapon.Lance);
            if(bowVisual)bowVisual.gameObject.SetActive(weapon==Weapon.Bow);
            if(swordVisual)swordVisual.gameObject.SetActive(weapon==Weapon.Sword);
            weaponVisual.SetParent(Mounted ? saddle : avatar, false);
            weaponVisual.localPosition = Mounted ? new Vector3(.35f, .9f, 1.2f) : new Vector3(.3f, .8f, .6f);
            weaponVisual.localRotation = Quaternion.identity;
            weaponVisual.localScale = Vector3.one;
            var hand = animationVisual ? animationVisual.Hand(weapon == Weapon.Bow) : null;
            if (hand)
            {
                Vector3 size = weaponVisual.localScale;
                weaponVisual.SetParent(hand, false);
                weaponVisual.localPosition = Vector3.zero;
                weaponVisual.rotation = (Mounted ? saddle : avatar).rotation;
                Vector3 scale = hand.lossyScale;
                weaponVisual.localScale = new Vector3(size.x/Mathf.Max(.001f,scale.x),size.y/Mathf.Max(.001f,scale.y),size.z/Mathf.Max(.001f,scale.z));
                if(weapon==Weapon.Lance)LanceGeometry.AlignGrip(lanceModel,weaponVisual,hand.position,(Mounted ? saddle : avatar).rotation);
                if(weapon==Weapon.Sword)ApplySwordHandPose();
            }
            UpdateStowedLance();
            lastTip = Tip();
        }
        void UpdateStowedLance()
        {
            bool shouldStow=Mounted && weapon!=Weapon.Lance;
            if(shouldStow && !stowedLance)
            {
                stowedLance=CreateLanceObject("Stowed rider lance");
                stowedLance.SetParent(saddle,false);stowedLance.localPosition=new Vector3(-.2f,-.12f,-.28f);
                stowedLance.localRotation=Quaternion.identity;stowedLance.localScale=Vector3.one;
            }
            if(stowedLance)stowedLance.gameObject.SetActive(shouldStow);
        }
        void DropLance(Vector3 inheritedVelocity)
        {
            if(!stowedLance)
            {
                stowedLance=CreateLanceObject("Dropped rider lance");
                stowedLance.position=saddle.position;stowedLance.rotation=RideRoot.rotation;stowedLance.localScale=Vector3.one;
            }
            else
            {
                stowedLance.gameObject.SetActive(true);stowedLance.SetParent(null,true);
                if(!stowedLance.GetComponent<Collider>())stowedLance.gameObject.AddComponent<BoxCollider>();
            }
            var body=stowedLance.gameObject.AddComponent<Rigidbody>();body.mass=.7f;body.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;LastDroppedLance=body;
            body.velocity=inheritedVelocity;body.angularVelocity=RideRoot.right*1.2f;Destroy(stowedLance.gameObject,15);stowedLance=null;
        }
        Transform CreateLanceObject(string objectName)
        {
            var prefab=Resources.Load<GameObject>("Weapons/Fly Lance Source") ?? Resources.Load<GameObject>("Weapons/Fly Lance");
            GameObject lance=prefab ? Instantiate(prefab) : GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            lance.name=objectName;if(!prefab){lance.transform.localScale=new Vector3(.05f,1.15f,.05f);lance.transform.localRotation=Quaternion.Euler(90,0,0);lance.GetComponent<Renderer>().sharedMaterial=weaponMaterial;}
            foreach(var collider in lance.GetComponentsInChildren<Collider>())Destroy(collider);return lance.transform;
        }
        void BuildLanceVisual()
        {
            lanceModel=CreateLanceObject("Held lance model");lanceModel.SetParent(weaponVisual,false);
            lanceModel.localPosition=Vector3.zero;lanceModel.localRotation=Quaternion.identity;lanceModel.localScale=Vector3.one;
        }
        void BuildBowVisual()
        {
            var root=new GameObject("Recurve bow visual");
            root.transform.SetParent(weaponVisual,false); bowVisual=root.transform;
            AddBowLine("Bow limbs",new[]{
                new Vector3(-.28f,-.72f,0),new Vector3(-.12f,-.58f,0),new Vector3(-.02f,-.32f,0),
                new Vector3(0,-.12f,0),new Vector3(0,.12f,0),new Vector3(-.02f,.32f,0),
                new Vector3(-.12f,.58f,0),new Vector3(-.28f,.72f,0)},.045f,weaponMaterial);
            AddBowLine("Bow string",new[]{new Vector3(-.28f,-.72f,0),new Vector3(.08f,0,0),new Vector3(-.28f,.72f,0)},.012f,weaponMaterial);
            var grip=GameObject.CreatePrimitive(PrimitiveType.Cube); grip.name="Bow grip";
            Destroy(grip.GetComponent<Collider>()); grip.transform.SetParent(root.transform,false);
            grip.transform.localPosition=Vector3.zero; grip.transform.localScale=new Vector3(.09f,.24f,.09f);
            grip.GetComponent<Renderer>().sharedMaterial=weaponMaterial;
            root.SetActive(false);
        }
        void AddBowLine(string name,Vector3[] points,float width,Material material)
        {
            var go=new GameObject(name); go.transform.SetParent(bowVisual,false);
            var line=go.AddComponent<LineRenderer>(); line.useWorldSpace=false;
            line.positionCount=points.Length; line.SetPositions(points); line.startWidth=width; line.endWidth=width;
            line.numCornerVertices=4; line.numCapVertices=4; line.sharedMaterial=material;
        }
        void BuildSwordVisual()
        {
            var root=new GameObject("Sword visual");root.transform.SetParent(weaponVisual,false);swordVisual=root.transform;
            SwordPart("Blade",PrimitiveType.Cube,new Vector3(0,0,.52f),new Vector3(.065f,.025f,.9f));
            SwordPart("Point",PrimitiveType.Cube,new Vector3(0,0,1f),new Vector3(.055f,.022f,.18f)).localRotation=Quaternion.Euler(0,45,0);
            SwordPart("Crossguard",PrimitiveType.Cube,new Vector3(0,0,.02f),new Vector3(.46f,.07f,.07f));
            SwordPart("Grip",PrimitiveType.Cylinder,new Vector3(0,0,-.18f),new Vector3(.07f,.18f,.07f)).localRotation=Quaternion.Euler(90,0,0);
            SwordPart("Pommel",PrimitiveType.Sphere,new Vector3(0,0,-.39f),Vector3.one*.11f);
            root.SetActive(false);
        }
        Transform SwordPart(string name,PrimitiveType primitive,Vector3 position,Vector3 scale)
        {
            var go=GameObject.CreatePrimitive(primitive);go.name=name;Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(swordVisual,false);go.transform.localPosition=position;go.transform.localScale=scale;
            go.GetComponent<Renderer>().sharedMaterial=weaponMaterial;return go.transform;
        }
        public void ApplySwordHandPose()
        {
            if(!weaponVisual || weapon!=Weapon.Sword)return;
            weaponVisual.localPosition=swordHandPose.position;
            weaponVisual.localRotation=Quaternion.Euler(swordHandPose.euler);
            Vector3 handScale=weaponVisual.parent ? weaponVisual.parent.lossyScale : Vector3.one;
            float s=Mathf.Max(.1f,swordHandPose.scale);
            weaponVisual.localScale=new Vector3(s/Mathf.Max(.001f,handScale.x),s/Mathf.Max(.001f,handScale.y),s/Mathf.Max(.001f,handScale.z));
        }
        public void SelectWeapon(Weapon selected) { weapon = selected; if (weaponVisual) SetWeapon(); }
        Vector3 Tip()
        {
            if(weapon==Weapon.Lance && lanceModel)
            {
                float farthest=0;Vector3 forward=weaponVisual.forward;
                foreach(var renderer in lanceModel.GetComponentsInChildren<Renderer>())
                {Bounds b=renderer.bounds;float projection=Vector3.Dot(b.center-weaponVisual.position,forward)+Vector3.Dot(b.extents,new Vector3(Mathf.Abs(forward.x),Mathf.Abs(forward.y),Mathf.Abs(forward.z)));farthest=Mathf.Max(farthest,projection);}
                return weaponVisual.position+forward*farthest;
            }
            return weaponVisual.position + weaponVisual.forward * (weaponVisual.lossyScale.z / 2);
        }
        public bool TryDismount()
        {
            if (!Perched) { message = "Perch before dismounting."; return false; }
            Vector3 origin = RideRoot.position + Vector3.up * 2;
            Vector3 flyForward=Vector3.ProjectOnPlane(RideRoot.forward,Vector3.up).normalized;
            if(flyForward.sqrMagnitude<.01f)flyForward=Vector3.forward;
            Vector3 flyLeft=Vector3.Cross(flyForward,Vector3.up).normalized;
            // A horse-style dismount goes to the mount's left. Only use rear/right
            // fallbacks when that side has no safe, world-upright foothold.
            foreach (Vector3 offset in new[] { flyLeft*1.4f,(flyLeft-flyForward*.35f).normalized*1.4f,-flyForward*1.4f,-flyLeft*1.4f })
            {
                if (!Physics.Raycast(origin + offset, Vector3.down, out var hit, 5, 1,
                    QueryTriggerInteraction.Ignore) || Vector3.Dot(hit.normal, Vector3.up) < .75f) continue;
                Vector3 position = hit.point + Vector3.up * .06f;
                if (Physics.CheckCapsule(position + Vector3.up * .21f, position + Vector3.up * .99f, .2f,
                    1, QueryTriggerInteraction.Ignore)) continue;
                DropLance(RideVelocity);Mounted = false; RideInput.enabled = false; mountedVisual.gameObject.SetActive(false);
                var head = saddle.Find("Rider head"); if (head) head.gameObject.SetActive(false);
                avatar.position=position;avatar.rotation=Quaternion.LookRotation(flyForward,Vector3.up);
                LastDismountLateral=Vector3.Dot(position-RideRoot.position,RideRoot.right);
                avatar.gameObject.SetActive(true); falling = 0; RideInput.ResetCues();
                if(animationVisual)animationVisual.BeginMountTransition(false);
                view.fly = avatar; view.rider = footInput; view.followAnchorRotation=false;
                view.SetOrientationSource(null,avatar);
                if (weapon == Weapon.Lance) weapon = Weapon.Sword;
                SetWeapon(); message = "Dismounted. F / Y changes weapon."; return true;
            }
            message = "No safe foothold nearby; move to a lower perch."; return false;
        }
        public bool TryMount()
        {
            if(fly && fly.Dead){message="The fly is dead and cannot be mounted.";return false;}
            float mountDistance=Vector3.Distance(avatar.position,RideRoot.position);
            bool slowHover=!research && fly && fly.Phase!=RidePhase.Perched &&
                (fly.RecallActive || fly.Phase==RidePhase.Landing) && fly.FlightSpeed<2.5f;
            if ((!Perched && !slowHover) || mountDistance > 2.8f)
            { message = "Approach the perched or slowly hovering fly to mount."; return false; }
            if (MountingPathBlocked(avatar.position + Vector3.up * .6f, RideRoot.position))
            { message = "Mounting path is blocked."; return false; }
            if(animationVisual)animationVisual.BeginMountTransition(true);
            if(fly)fly.AcceptMountedRider();
            Mounted = true; avatar.gameObject.SetActive(false); RideInput.enabled = true;
            mountedVisual.gameObject.SetActive(true);
            var head = saddle.Find("Rider head"); if (head) head.gameObject.SetActive(true);
            view.fly = RideRoot; view.rider = RideInput; view.followAnchorRotation=true; weapon = Weapon.Lance;
            view.SetOrientationSource(animationVisual ? animationVisual.Head : null,RideRoot);
            SetWeapon(); message = slowHover ? "Mounted from hover; grip secured." : "Mounted; grip retained at every orientation."; return true;
        }
        bool MountingPathBlocked(Vector3 start,Vector3 end)
        {
            Vector3 delta=end-start;float distance=delta.magnitude;if(distance<.01f)return false;
            foreach(var hit in Physics.RaycastAll(start,delta/distance,distance,1,QueryTriggerInteraction.Ignore))
            {
                Transform target=hit.collider.transform;
                // The destination ray necessarily enters the fly's own capsule.
                // That is not an obstruction to mounting it.
                if(target==RideRoot || target.IsChildOf(RideRoot) || target==avatar || target.IsChildOf(avatar))continue;
                return true;
            }
            return false;
        }
        public bool ForceUnseat(Vector3 impulse)
        {
            if(!Mounted || Defeated || !avatar || !feet)return false;
            DropLance(RideVelocity);Mounted=false;RideInput.enabled=false;RideInput.ResetCues();mountedVisual.gameObject.SetActive(false);
            var head=saddle.Find("Rider head");if(head)head.gameObject.SetActive(false);
            avatar.gameObject.SetActive(true);feet.enabled=false;avatar.position=saddle.position+Vector3.up*.15f;
            Vector3 heading=Vector3.ProjectOnPlane(RideRoot.forward,Vector3.up);
            avatar.rotation=heading.sqrMagnitude>.01f ? Quaternion.LookRotation(heading) : Quaternion.identity;feet.enabled=true;
            falling=impulse.y;unseatVelocity=Vector3.ProjectOnPlane(impulse,Vector3.up);
            unseatedFall=true;peakUnseatedFallSpeed=0;LastFallDamage=0;
            if(animationVisual)animationVisual.EnterRagdoll(impulse);
            view.fly=avatar;view.rider=footInput;view.followAnchorRotation=false;view.SetOrientationSource(null,avatar);
            weapon=Weapon.Sword;SetWeapon();message="Unseated! Recover and continue on foot.";return true;
        }
        void Update()
        {
            if (!avatar || !Application.isFocused) return;
#if UNITY_EDITOR
            bool automated=FindObjectOfType<CombatPlayCheck>();
#else
            bool automated=false;
#endif
            if(!research && !standaloneJousterSpawned && !automated)
            { standaloneJouster=CreateMountedJouster(null,RideRoot.position);standaloneJousterSpawned=true; }
            if (CombatPaused && !Input.GetKeyDown(KeyCode.Backspace) && !RideInput.resetRide && !footInput.resetRide) return;
            if (Defeated && !Input.GetKeyDown(KeyCode.Backspace) && !RideInput.resetRide && !footInput.resetRide)
            {
                if(defeatRespawnTimer<0)defeatRespawnTimer=defeatRespawnDelay;
                defeatRespawnTimer-=Mathf.Max(0,CombatDeltaTime);
                if(defeatRespawnTimer<=0)RespawnFarthestFromOpponents();
                return;
            }
            var input = Mounted ? RideInput : footInput;
            bool pad = WindowsGamepad.Read(out var sample);
            float interact = pad && sample.Held(WindowsGamepad.X) ? 1 : 0;
            float swap = pad && sample.Held(WindowsGamepad.Y) ? 1 : 0;
            bool swordContext=!Mounted && weapon==Weapon.Sword && !Defeated;
            bool swordX=pad && sample.Held(WindowsGamepad.X);
            bool swordB=pad && sample.Held(WindowsGamepad.B);
            bool call=pad && sample.Held(WindowsGamepad.DPadDown);
            if(!Mounted && (Input.GetKeyDown(KeyCode.H) || call && !callBefore))CallFlyNear();
            callBefore=call;
            if (Input.GetKeyDown(KeyCode.C) || (!swordContext && interact > padInteractBefore))
            { if (Mounted) TryDismount(); else TryMount(); input = Mounted ? RideInput : footInput; }
            if (Input.GetKeyDown(KeyCode.F) || swap > padWeaponBefore)
            {
                weapon = Mounted ? (weapon == Weapon.Lance ? Weapon.Bow : Weapon.Lance) :
                    (weapon == Weapon.Sword ? Weapon.Bow : Weapon.Sword); SetWeapon();
            }
            padInteractBefore = interact; padWeaponBefore = swap;
            if (!Mounted)
            {
                Vector3 forward = Vector3.ProjectOnPlane(view.transform.forward, Vector3.up).normalized;
                Vector3 right = Vector3.Cross(Vector3.up, forward);
                Vector3 move = Vector3.ClampMagnitude(right * input.reins.x + forward * input.reins.y, 1) * 3;
                if (feet.isGrounded)
                {
                    if(unseatedFall && falling<=0)
                    {
                        LastFallDamage=Mathf.Clamp((peakUnseatedFallSpeed-4)*5,0,30);
                        if(LastFallDamage>0)TakeDamage(LastFallDamage);
                        unseatedFall=false;unseatVelocity=Vector3.zero;if(!Defeated && animationVisual)animationVisual.ExitRagdoll();
                    }
                    falling = -2; if (input.ConsumeSpur()) falling = 4;
                }
                falling += Physics.gravity.y * CombatDeltaTime;
                if(unseatedFall)peakUnseatedFallSpeed=Mathf.Max(peakUnseatedFallSpeed,-falling);
                unseatVelocity=Vector3.MoveTowards(unseatVelocity,Vector3.zero,CombatDeltaTime*2);
                feet.Move((move+unseatVelocity+Vector3.up*falling)*CombatDeltaTime);
                if (move.sqrMagnitude > .01f) avatar.rotation = Quaternion.LookRotation(move);
                if (input.resetRide)
                {
                    // Reset deliberately remounts; clear projectiles and target damage too.
                    if(animationVisual)animationVisual.ExitRagdoll();
                    Mounted = true; avatar.gameObject.SetActive(false); mountedVisual.gameObject.SetActive(true);
                    var head = saddle.Find("Rider head"); if (head) head.gameObject.SetActive(true);
                    RideInput.enabled = true; ResetBody(); strikePending = false; cooldown = charge = 0;
                    view.fly = RideRoot; view.rider = RideInput; view.followAnchorRotation=true;
                    view.SetOrientationSource(animationVisual ? animationVisual.Head : null,RideRoot);
                    weapon = Weapon.Lance; SetWeapon();
                }
            }
            if (input.resetRide)
            {
                if(animationVisual)animationVisual.CancelMountTransition();
                Health = 100;
                if(playerFlyHealth)playerFlyHealth.ResetTarget();
                defeatRespawnTimer=-1;
                foreach (var target in FindObjectsOfType<CombatTarget>()) target.ResetTarget();
                foreach (var arrow in FindObjectsOfType<CombatArrow>()) Destroy(arrow.gameObject);
                foreach (var arrow in FindObjectsOfType<OpponentArrow>()) Destroy(arrow.gameObject);
                foreach (var opponent in FindObjectsOfType<CombatOpponent>()) opponent.ResetOpponent();
            }
            cooldown = Mathf.Max(0, cooldown - CombatDeltaTime);
            swordGesture=Mathf.Max(0,swordGesture-CombatDeltaTime);
            if(!Mounted && weapon==Weapon.Sword && LastSwordAttack==SwordAttack.Thrust && swordGesture>0 && feet.isGrounded)
            {
                float phase=1-swordGesture/SwordGestureDuration;
                feet.Move(avatar.forward*(Mathf.Sin(Mathf.Clamp01(phase)*Mathf.PI)*AttackTransition(phase)*1.8f*CombatDeltaTime));
            }
            if (strikePending)
            {
                strikeDelay -= CombatDeltaTime;
                if (strikeDelay <= 0)
                {
                    strikePending = false;
                    if (!Mounted && weapon == Weapon.Sword && !Defeated) ResolveSwordStrike();
                }
            }
            bool attack = input.primaryAction > .5f || Input.GetMouseButton(0);
            bool draw = input.secondaryAction > .5f || Input.GetMouseButton(1);
            if (weapon == Weapon.Bow)
            {
                if (draw) charge = Mathf.Min(1, charge + CombatDeltaTime);
                if (!draw && bowBefore && charge > .05f && cooldown == 0) FireArrow();
                if (!draw) charge = 0;
            }
            else if (swordContext && cooldown==0)
            {
                if(swordX && !swordXBefore)SwordStrike(SwordAttack.LeftToRight);
                else if(swordB && !swordBBefore)SwordStrike(SwordAttack.RightToLeft);
                else if(attack && !attackBefore)SwordStrike(SwordAttack.Thrust);
            }
            Vector3 tip = Tip(); tipSpeed = (tip-lastTip).magnitude / Mathf.Max(.001f, CombatDeltaTime);
            float chargeSpeed = RideVelocity.magnitude;
            if (Mounted && weapon == Weapon.Lance && cooldown == 0 && Mathf.Max(chargeSpeed,tipSpeed)>.5f)
                ResolveLanceHit(tip,chargeSpeed);
            lastTip = tip; attackBefore = attack; bowBefore = draw;
            swordXBefore=swordX;swordBBefore=swordB;
        }
        void FireArrow()
        {
            if (animationVisual) animationVisual.Attack("Bow");
            var camera = view.GetComponent<Camera>(); Ray aim = camera.ViewportPointToRay(new Vector3(.5f, .5f, 0));
            Vector3 origin = animationVisual && animationVisual.Hand(true) ? animationVisual.Hand(true).position+aim.direction*.38f : (Mounted ? saddle.position + saddle.up * .9f : avatar.position + Vector3.up * .9f);
            Vector3 point = Physics.Raycast(origin,aim.direction,out var hit,100,1,QueryTriggerInteraction.Ignore) ? hit.point : origin+aim.direction*100;
            Vector3 direction = (point-origin).normalized;
            var obj = GameObject.CreatePrimitive(PrimitiveType.Cube); obj.name = "Arrow";
            Destroy(obj.GetComponent<Collider>()); obj.transform.position = origin;
            obj.transform.rotation = Quaternion.LookRotation(direction); obj.transform.localScale = new Vector3(.04f, .04f, .5f);
            obj.GetComponent<Renderer>().sharedMaterial = weaponMaterial;
            var arrow = obj.AddComponent<CombatArrow>(); arrow.velocity = direction * Mathf.Lerp(12, 30, charge);
            arrow.clock = this; arrow.damage = Mathf.Lerp(15, 45, charge); cooldown = .35f; message = "Arrow released.";
        }
        public void SwordStrike() { SwordStrike(SwordAttack.Thrust); }
        static float CutSweep(float phase,float direction)
        {
            if(phase<.28f)return Mathf.Lerp(0,-direction,Mathf.SmoothStep(0,1,phase/.28f));
            return Mathf.Lerp(-direction,direction,Mathf.SmoothStep(0,1,(phase-.28f)/.72f));
        }
        static float AttackTransition(float phase)
        {
            float enter=Mathf.SmoothStep(0,1,Mathf.Clamp01(phase/.18f));
            float recover=1-Mathf.SmoothStep(0,1,Mathf.Clamp01((phase-.82f)/.18f));
            return enter*recover;
        }
        public void SwordStrike(SwordAttack style)
        {
            if (Mounted || weapon != Weapon.Sword || cooldown > 0) return;
            // Sword motion is fully procedural. The borrowed generic Sword clip fought
            // the directional pose during its crossfade and caused a visible entry snap.
            LastSwordAttack=style;swordGesture=SwordGestureDuration;
            strikePending = true; strikeDelay = swordWindup;
            cooldown = SwordGestureDuration;
        }
        void ResolveSwordStrike()
        {
            Vector3 origin = avatar.position + Vector3.up * .7f;
            if (Physics.SphereCast(origin, .35f, view.transform.forward, out var hit, 1.8f, 1,
                QueryTriggerInteraction.Ignore))
            { var target = hit.collider.GetComponentInParent<CombatTarget>(); if (target && target.Hit(25)) message = "Sword hit."; }
        }
        void ResolveLanceHit(Vector3 tip,float chargeSpeed)
        {
            CombatTarget target=null;
            // Test the whole couched weapon so a capsule cannot slip between the hand and tip.
            float nearest=float.PositiveInfinity;
            foreach(var collider in Physics.OverlapCapsule(weaponVisual.position,tip,.16f,1,QueryTriggerInteraction.Ignore))
            {
                var candidate=collider.GetComponentInParent<CombatTarget>();if(!candidate)continue;
                float distance=(collider.ClosestPoint(tip)-tip).sqrMagnitude;
                if(distance<nearest){nearest=distance;target=candidate;}
            }
            // Also sweep the tip to cover high-speed travel between rendered frames.
            Vector3 delta=tip-lastTip;
            if(!target && delta.sqrMagnitude>.000001f && Physics.SphereCast(lastTip,.16f,delta.normalized,out var hit,
                delta.magnitude+.2f,1,QueryTriggerInteraction.Ignore))target=hit.collider.GetComponentInParent<CombatTarget>();
            float impact=Mathf.Max(chargeSpeed,tipSpeed);
            if(target)ApplyLanceHit(target,impact);
        }
        bool ApplyLanceHit(CombatTarget target,float impact)
        {
            if(target && target.Hit(Mathf.Clamp(impact*6,20,100)))
            {
                var mountedOpponent=target.GetComponentInParent<MountedJoustOpponent>();
                var zone=target.GetComponent<MountedHitZone>();
                if(zone && zone.owner)
                {
                    if(zone.fly)message=zone.owner.ReceiveFlyLanceContact(impact,weaponVisual.forward) ? (zone.owner.Mounted ? "Lance hit — enemy fly damaged!" : "Enemy fly disabled — opponent unseated!") : "Joust hit.";
                    else message=zone.owner.ReceiveLanceContact(impact) ? "Solid lance hit — opponent unseated!" : "Rider hit!";
                }
                else message=mountedOpponent && mountedOpponent.ReceiveLanceContact(impact) ? "Solid lance hit — opponent unseated!" : "Joust hit.";
                cooldown=.5f;
                return true;
            }
            return false;
        }
#if UNITY_EDITOR
        public bool TestLanceHit(CombatTarget target,float impact){return ApplyLanceHit(target,impact);}
#endif
        public void ReleaseArrow(float draw)
        { if (weapon != Weapon.Bow || cooldown > 0 || float.IsNaN(draw) || float.IsInfinity(draw)) return; charge = Mathf.Clamp01(draw); FireArrow(); charge = 0; }
        public Transform FootAvatar { get { return avatar; } }
        public bool CallFlyNear()
        {
            if(Mounted || !fly)return false;
            if(fly.Dead){message="The fly is dead and cannot answer the call.";return false;}
            Vector3 forward=Vector3.ProjectOnPlane(view.transform.forward,Vector3.up).normalized;
            if(forward.sqrMagnitude<.01f)forward=avatar.forward;
            Vector3[] offsets={-forward*1.5f,avatar.right*1.5f,-avatar.right*1.5f};
            foreach(Vector3 offset in offsets)
            {
                Vector3 origin=avatar.position+offset+Vector3.up*3;
                if(!Physics.Raycast(origin,Vector3.down,out var hit,8,1,QueryTriggerInteraction.Ignore) || Vector3.Dot(hit.normal,Vector3.up)<.75f)continue;
                if(fly.RequestRecall(hit.point+hit.normal*.525f)){message="Whistled for the fly — it is approaching.";return true;}
            }
            message="No safe nearby landing point for the fly.";return false;
        }
        public bool TakeDamage(float damage)
        {
            if (Defeated || damage <= 0 || float.IsNaN(damage) || float.IsInfinity(damage)) return false;
            Health = Mathf.Max(0, Health-damage);
            if(Defeated){defeatRespawnTimer=defeatRespawnDelay;if(animationVisual)animationVisual.EnterRagdoll((Mounted ? RideVelocity : unseatVelocity)+Vector3.up*.5f);}
            message = Defeated ? "Rider defeated. Respawning away from opponents..." : "Rider hit.";
            return true;
        }
        public void ResetHealth() { Health = 100; defeatRespawnTimer=-1; }
        public void RespawnFarthestFromOpponents()
        {
            Vector3 position=FindFarthestOpponentSpawn();
            if(animationVisual)animationVisual.ExitRagdoll();
            Mounted=false;RideInput.enabled=false;RideInput.ResetCues();mountedVisual.gameObject.SetActive(false);
            var head=saddle.Find("Rider head");if(head)head.gameObject.SetActive(false);
            avatar.gameObject.SetActive(true);feet.enabled=false;avatar.position=position;
            Vector3 toward=OpponentCentroid()-position;toward=Vector3.ProjectOnPlane(toward,Vector3.up);
            avatar.rotation=toward.sqrMagnitude>.01f ? Quaternion.LookRotation(toward) : Quaternion.identity;
            feet.enabled=true;footInput.enabled=true;falling=-2;unseatedFall=false;unseatVelocity=Vector3.zero;
            Health=100;defeatRespawnTimer=-1;PlayerRespawnCount++;LastPlayerRespawnPosition=position;
            if(playerFlyHealth)playerFlyHealth.ResetTarget();if(fly)fly.ReviveAfterDeath();
            foreach(var arrow in FindObjectsOfType<OpponentArrow>())Destroy(arrow.gameObject);
            view.fly=avatar;view.rider=footInput;view.followAnchorRotation=false;view.SetOrientationSource(null,avatar);
            weapon=Weapon.Sword;SetWeapon();message="Respawned at the safest point, farthest from opponents.";
        }
        Vector3 OpponentCentroid()
        {
            Vector3 sum=Vector3.zero;int count=0;
            foreach(var opponent in FindObjectsOfType<CombatOpponent>())
                if(opponent.gameObject.activeInHierarchy && !opponent.Defeated){sum+=opponent.transform.position;count++;}
            foreach(var opponent in FindObjectsOfType<MountedJoustOpponent>())
                if(opponent.gameObject.activeInHierarchy && opponent.RiderHealth && opponent.RiderHealth.Health>0){sum+=opponent.transform.position;count++;}
            return count>0 ? sum/count : combatSpawnCenter;
        }
        float NearestOpponentDistanceSquared(Vector3 point)
        {
            float nearest=float.PositiveInfinity;
            foreach(var opponent in FindObjectsOfType<CombatOpponent>())
                if(opponent.gameObject.activeInHierarchy && !opponent.Defeated)nearest=Mathf.Min(nearest,(opponent.transform.position-point).sqrMagnitude);
            foreach(var opponent in FindObjectsOfType<MountedJoustOpponent>())
                if(opponent.gameObject.activeInHierarchy && opponent.RiderHealth && opponent.RiderHealth.Health>0)nearest=Mathf.Min(nearest,(opponent.transform.position-point).sqrMagnitude);
            return nearest;
        }
        bool TryGroundSpawn(Vector3 planar,out Vector3 spawn)
        {
            var hits=Physics.RaycastAll(planar+Vector3.up*8,Vector3.down,20,1,QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits,(a,b)=>a.distance.CompareTo(b.distance));
            foreach(var hit in hits)
            {
                if(Vector3.Dot(hit.normal,Vector3.up)<.75f)continue;
                if(hit.collider.GetComponentInParent<CombatOpponent>() || hit.collider.GetComponentInParent<MountedJoustOpponent>() || hit.collider.GetComponent<CombatTarget>())continue;
                spawn=hit.point+Vector3.up*.08f;return true;
            }
            spawn=default(Vector3);return false;
        }
        Vector3 FindFarthestOpponentSpawn()
        {
            Vector3 best=avatar && avatar.gameObject.activeInHierarchy ? avatar.position : combatSpawnCenter;
            float bestScore=NearestOpponentDistanceSquared(best);
            for(int ring=1;ring<=3;ring++)for(int step=0;step<24;step++)
            {
                float angle=step*Mathf.PI*2/24;
                Vector3 planar=combatSpawnCenter+new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*(ring*4);
                if(!TryGroundSpawn(planar,out var candidate))continue;
                float score=NearestOpponentDistanceSquared(candidate);
                if(score>bestScore){best=candidate;bestScore=score;}
            }
            return best;
        }
        public Vector3 LanceTip { get { return Tip(); } }
        void OnGUI()
        {
            DrawHungerHud();
            GUI.color = new Color(.04f, .08f, .12f, .95f);
            GUI.DrawTexture(new Rect(16, Screen.height-198, 740, 182), Texture2D.whiteTexture); GUI.color = Color.white;
            if(animationVisual) animationVisual.mountTransitions=GUI.Toggle(new Rect(24,Screen.height-192,400,24),animationVisual.mountTransitions,"Horse-style mount and left dismount transitions");
            string flyStatus=fly && fly.Dead ? "DEAD" : fly ? fly.Phase.ToString() : "Unavailable";
            GUI.Label(new Rect(30, Screen.height-122, 720, 24), (research ? "SCIENTIFIC BODY / GAMEPLAY COMBAT — " : "COMBAT PROTOTYPE — ") + (Mounted ? "Mounted" : "On foot") + "   Weapon: " + weapon + "   Rider HP: " + Mathf.CeilToInt(Health)+(playerFlyHealth ? "   Fly HP: "+Mathf.CeilToInt(playerFlyHealth.Health)+" ("+flyStatus+")" : ""));
            GUI.Label(new Rect(30, Screen.height-97, 720, 24), "C / X: mount   F / Y: weapon   Sword: X cut L-R, B cut R-L, RT thrust");
            GUI.Label(new Rect(30, Screen.height-72, 720, 24), "Bow: hold LT to draw/release   D-pad Down / H: call fly   Draw: " + Mathf.RoundToInt(charge*100) + "%");
            GUI.Label(new Rect(30, Screen.height-47, 720, 24), message);
            if (research)
            {
                research.autonomousIdle=GUI.Toggle(new Rect(420,Screen.height-192,300,24),research.autonomousIdle,"Fly walks while dismounted (floor)");
                if (GUI.Button(new Rect(16, Screen.height-165, 180, 28), practice ? "Remove combat practice" : "Add combat practice"))
                { SetPractice(!practice,practiceEnemies); }
                bool enabledEnemies = GUI.Toggle(new Rect(210, Screen.height-161, 180, 24), practiceEnemies, "Enemy combat");
                if (enabledEnemies != practiceEnemies)
                { SetPractice(practice,enabledEnemies); }
                if (animationVisual) animationVisual.legGrip = GUI.Toggle(new Rect(400,Screen.height-161,180,24),animationVisual.legGrip,"Thorax leg grip");
            }
        }
        void DrawHungerHud()
        {
            if(!fly)return;
            float value=Mathf.Clamp01(fly.Hunger),authority=Mathf.Clamp01(fly.RiderAuthority);
            Rect panel=new Rect(Screen.width-294,18,276,78),bar=new Rect(Screen.width-280,46,248,16);
            Color previous=GUI.color;GUI.color=new Color(.035f,.055f,.075f,.9f);GUI.DrawTexture(panel,Texture2D.whiteTexture);
            GUI.color=new Color(.12f,.15f,.17f,1);GUI.DrawTexture(bar,Texture2D.whiteTexture);
            GUI.color=Color.Lerp(new Color(.25f,.8f,.3f),new Color(1f,.28f,.08f),value);
            GUI.DrawTexture(new Rect(bar.x,bar.y,bar.width*value,bar.height),Texture2D.whiteTexture);
            GUI.color=Color.white;
            string state=fly.Feeding ? "feeding" : fly.SeekingFood ? "seeking food / resisting reins" : authority<.65f ? "becoming unruly" : "responsive";
            GUI.Label(new Rect(panel.x+12,panel.y+6,panel.width-24,22),"FLY HUNGER  "+Mathf.RoundToInt(value*100)+"%");
            GUI.Label(new Rect(panel.x+12,panel.y+48,panel.width-24,22),state+"   control "+Mathf.RoundToInt(authority*100)+"%");
            GUI.color=previous;
        }
        void CreatePractice()
        {
            if (!research || research.CurrentFrame == null || research.CurrentFrame.walking_surface != "floor")
            { message = "Combat practice needs the floor surface."; return; }
            practice = new GameObject("Scientific body gameplay practice");
            Vector3 center = RideRoot.position;
            for (int i=0;i<4;i++)
            {
                Vector3 origin = center + Vector3.up*3 + new Vector3((i-1.5f)*3,0,7);
                if (!Physics.Raycast(origin,Vector3.down,out var ground,8,1,QueryTriggerInteraction.Ignore)) continue;
                var dummy = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                dummy.name = practiceEnemies ? (i%2==0 ? "Enemy swordsman" : "Enemy archer") : "Combat dummy";
                dummy.transform.SetParent(practice.transform);
                dummy.transform.position = ground.point+Vector3.up;
                dummy.GetComponent<Renderer>().sharedMaterial = weaponMaterial;
                dummy.AddComponent<CombatTarget>();
                if (practiceEnemies)
                {
                    var controller = dummy.AddComponent<CharacterController>(); controller.height=2;controller.radius=.35f;
                    dummy.AddComponent<CombatOpponent>().style = i%2==0 ? CombatOpponent.Style.Swordsman : CombatOpponent.Style.Archer;
                }
            }
            if(practiceEnemies)
                CreateMountedJouster(practice.transform,center);
            message = "Gameplay targets use the body clock; target collisions are outside MuJoCo.";
        }
        GameObject CreateMountedJouster(Transform parent,Vector3 center)
        { return CreateMountedJousterAt(parent,center+RideRoot.right*10+Vector3.up*4,0); }
        GameObject CreateMountedJousterAt(Transform parent,Vector3 position,int competency)
        {
            var mounted=GameObject.CreatePrimitive(PrimitiveType.Capsule);mounted.name="Mounted enemy jouster";
            if(parent)mounted.transform.SetParent(parent);mounted.GetComponent<Renderer>().sharedMaterial=weaponMaterial;
            mounted.AddComponent<CombatTarget>();var jouster=mounted.AddComponent<MountedJoustOpponent>();
            jouster.competencyLevel=competency;
            var biological=RideRoot.Find("Detailed NeuroMechFly appearance");
            jouster.Initialize(this,biological ? biological.gameObject : null,saddle,RideRoot,riderMaterial,weaponMaterial,position);
            return mounted;
        }
        public bool PlayerCameraCanSee(Vector3 worldPosition,float radius=1f)
        {
            Camera camera=view ? view.GetComponent<Camera>() : null;
            if(!camera || !camera.isActiveAndEnabled)return false;
            if(!GeometryUtility.TestPlanesAABB(GeometryUtility.CalculateFrustumPlanes(camera),new Bounds(worldPosition,Vector3.one*radius*2)))return false;
            Vector3 origin=camera.transform.position,delta=worldPosition-origin;float distance=delta.magnitude;
            if(distance<.01f)return true;
            foreach(var hit in Physics.RaycastAll(origin,delta/distance,distance,1,QueryTriggerInteraction.Ignore))
            {
                Transform candidate=hit.collider.transform;
                if(candidate==transform || candidate.IsChildOf(transform) || candidate==RideRoot || candidate.IsChildOf(RideRoot) ||
                   candidate==avatar || candidate.IsChildOf(avatar))continue;
                if(hit.distance<distance-radius)return false;
            }
            return true;
        }
        public Vector3 FindHiddenEnemyRespawn(Vector3 preferred)
        {
            Vector3 playerPosition=RiderPosition;
            bool Valid(Vector3 candidate)
            {
                if(Vector3.Distance(candidate,playerPosition)<8 || PlayerCameraCanSee(candidate,1.25f))return false;
                foreach(var collider in Physics.OverlapSphere(candidate,.7f,1,QueryTriggerInteraction.Ignore))
                {
                    Transform obstacle=collider.transform;
                    if(obstacle==RideRoot || obstacle.IsChildOf(RideRoot) || obstacle==avatar || obstacle.IsChildOf(avatar))continue;
                    if(collider.GetComponentInParent<CombatTarget>() || collider.GetComponentInParent<CharacterController>())continue;
                    return false;
                }
                return true;
            }
            Camera camera=view ? view.GetComponent<Camera>() : null;
            Vector3 cameraForward=camera ? Vector3.ProjectOnPlane(camera.transform.forward,Vector3.up).normalized : Vector3.forward;
            if(cameraForward.sqrMagnitude<.01f)cameraForward=Vector3.forward;
            float altitude=Mathf.Max(preferred.y,playerPosition.y+3);
            LastEnemyRespawnUsesCornerEntry=false;
            if(camera)
            {
                // Enter beyond the upper corners at the nearer distance, or beyond
                // the lower corners farther away. Cycling prevents every replacement
                // from arriving through the same lane.
                Vector3[] viewportEntries={
                    new Vector3(-.14f,1.14f,14),new Vector3(1.14f,1.14f,14),
                    new Vector3(-.2f,-.2f,22),new Vector3(1.2f,-.2f,22)};
                int start=enemyRespawnSequence++%viewportEntries.Length;
                for(int offset=0;offset<viewportEntries.Length;offset++)
                {
                    Vector3 entry=viewportEntries[(start+offset)%viewportEntries.Length];
                    Ray ray=camera.ViewportPointToRay(new Vector3(entry.x,entry.y,0));
                    Vector3 candidate=ray.GetPoint(entry.z);
                    // Never place a lower-corner arrival beneath the floor or table.
                    var floors=Physics.RaycastAll(new Vector3(candidate.x,playerPosition.y+20,candidate.z),Vector3.down,50,1,QueryTriggerInteraction.Ignore);
                    float floor=float.NegativeInfinity;
                    foreach(var hit in floors)
                        if(!hit.collider.attachedRigidbody && Vector3.Dot(hit.normal,Vector3.up)>.65f)floor=Mathf.Max(floor,hit.point.y);
                    if(floor>float.NegativeInfinity)candidate.y=Mathf.Max(candidate.y,floor+2);
                    if(!Valid(candidate))continue;
                    LastEnemyRespawnPosition=candidate;LastEnemyRespawnUsesCornerEntry=true;return candidate;
                }
            }
            Vector3 best=preferred;float bestScore=float.NegativeInfinity;
            for(int ring=0;ring<3;ring++)for(int step=0;step<32;step++)
            {
                float angle=step*Mathf.PI*2/32;float range=12+ring*4;
                Vector3 direction=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle));
                Vector3 candidate=playerPosition+direction*range;candidate.y=altitude;
                if(!Valid(candidate))continue;
                // Prefer behind the camera, then preserve proximity to the intended arena spawn.
                float score=-Vector3.Dot(direction,cameraForward)*12-Vector3.Distance(candidate,preferred)*.08f;
                if(score>bestScore){best=candidate;bestScore=score;}
            }
            // If every sampled point is occupied, keep the fallback behind and above
            // the camera rather than ever permitting an on-screen pop-in.
            Vector3 hiddenFallback=playerPosition-cameraForward*20+Vector3.up*(altitude-playerPosition.y+5);
            LastEnemyRespawnPosition=bestScore>float.NegativeInfinity ? best : hiddenFallback;
            return LastEnemyRespawnPosition;
        }
        public void EnsureEnemyMountReplacement(Transform parent,Vector3 position,int competency)
        {
            foreach(var opponent in FindObjectsOfType<MountedJoustOpponent>())
                if(opponent && (opponent.Mounted || opponent.FlyEscaping))return;
            CreateMountedJousterAt(parent,FindHiddenEnemyRespawn(position),competency);
        }
        public void SetPractice(bool active,bool enemies)
        {
            practiceEnemies=enemies;
            if (practice) { practice.SetActive(false); Destroy(practice);practice=null; }
            if (active) CreatePractice();
        }
        void OnDestroy() { if (practice) Destroy(practice);if(standaloneJouster)Destroy(standaloneJouster); if (avatar) Destroy(avatar.gameObject); if (weaponVisual) Destroy(weaponVisual.gameObject); }
    }

    // Both jousters use the model's authored handle as the grip point. This keeps the
    // same physical model length ahead of either hand, regardless of skeleton scaling.
    static class LanceGeometry
    {
        static Renderer HandleRenderer(Transform model)
        {
            if(!model)return null;
            foreach(var renderer in model.GetComponentsInChildren<Renderer>())
            {
                string part=renderer.name.ToLowerInvariant();
                if(part.Contains("handle") || part.Contains("grip"))return renderer;
            }
            return null;
        }
        public static Vector3 GripPoint(Transform model)
        {
            var handle=HandleRenderer(model);return handle ? handle.bounds.center : (model ? model.position : Vector3.zero);
        }
        public static float GripError(Transform model,Vector3 handPosition){return Vector3.Distance(GripPoint(model),handPosition);}
        public static Quaternion RaisedForWall(Transform model,Transform root,Vector3 handPosition,Quaternion forwardRotation,Transform owner)
        {
            if(!model || !root)return forwardRotation;
            // Measure the authored weapon after restoring its ordinary couch pose.
            AlignGrip(model,root,handPosition,forwardRotation);
            float reach=0;Vector3 forward=forwardRotation*Vector3.forward;
            foreach(var renderer in model.GetComponentsInChildren<Renderer>())
            {
                Bounds bounds=renderer.bounds;
                float projection=Vector3.Dot(bounds.center-handPosition,forward)+Vector3.Dot(bounds.extents,
                    new Vector3(Mathf.Abs(forward.x),Mathf.Abs(forward.y),Mathf.Abs(forward.z)));
                reach=Mathf.Max(reach,projection);
            }
            RaycastHit nearest=default(RaycastHit);float distance=float.PositiveInfinity;
            foreach(var hit in Physics.SphereCastAll(handPosition,.09f,forward,reach+.15f,1,QueryTriggerInteraction.Ignore))
            {
                Transform candidate=hit.collider.transform;
                if(owner && (candidate==owner || candidate.IsChildOf(owner)))continue;
                if(hit.collider.attachedRigidbody || hit.collider.GetComponentInParent<CharacterController>() ||
                   hit.collider.GetComponentInParent<CombatTarget>())continue;
                if(hit.distance<distance){nearest=hit;distance=hit.distance;}
            }
            if(float.IsPositiveInfinity(distance))return forwardRotation;
            float proximity=1-Mathf.Clamp01((distance-.12f)/Mathf.Max(.1f,reach));
            float raise=Mathf.SmoothStep(0,58,proximity);
            return Quaternion.AngleAxis(-raise,forwardRotation*Vector3.right)*forwardRotation;
        }
        public static void AlignGrip(Transform model,Transform root,Vector3 handPosition,Quaternion rotation)
        {
            if(!model || !root)return;
            root.rotation=rotation;
            root.position+=handPosition-GripPoint(model);
        }
    }
}
