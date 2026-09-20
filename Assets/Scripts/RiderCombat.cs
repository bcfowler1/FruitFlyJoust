using UnityEngine;

namespace FruitFlyJoust
{
    public sealed class RiderCombat : MonoBehaviour
    {
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
        private Transform avatar, weaponVisual, bowVisual, swordVisual;
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
        public bool RiderRagdolled { get { return animationVisual && animationVisual.Ragdolled; } }
        public CombatTarget PlayerFlyHealth { get { return playerFlyHealth; } }
        public Vector3 FlyHitPosition { get { return playerFlyHealth ? playerFlyHealth.transform.position : RideRoot.position; } }

        void Start()
        {
            combatSpawnCenter = RideRoot.position;
            saddle = mountedVisual.parent;
            var walker = new GameObject("Dismounted rider");
            walker.name = "Dismounted rider"; walker.layer = 2;
            avatar = walker.transform; avatar.localScale = Vector3.one;
            var figure = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Destroy(figure.GetComponent<Collider>()); figure.transform.SetParent(avatar, false);
            figure.transform.localPosition = new Vector3(0, .6f, 0);
            figure.transform.localScale = new Vector3(.4f, .6f, .4f);
            figure.GetComponent<Renderer>().sharedMaterial = riderMaterial;
            feet = walker.AddComponent<CharacterController>(); feet.height = 1.2f; feet.radius = .2f;
            feet.center = new Vector3(0, .6f, 0);
            footInput = walker.AddComponent<RiderInput>(); walker.SetActive(false);
            var pole = GameObject.CreatePrimitive(PrimitiveType.Cube); pole.name = "Rider weapon";
            Destroy(pole.GetComponent<Collider>());
            pole.GetComponent<Renderer>().sharedMaterial = weaponMaterial;
            weaponVisual = pole.transform;
            BuildBowVisual();
            BuildSwordVisual();
            var swordPoseAsset=Resources.Load<TextAsset>("SwordHandPose");
            if(swordPoseAsset)swordHandPose=JsonUtility.FromJson<SwordHandPose>(swordPoseAsset.text);
            SetWeapon(); lastTip = Tip();
            animationVisual = existingAnimation ? existingAnimation : gameObject.AddComponent<RiderAnimationVisual>();
            if (existingAnimation || animationVisual.Create(saddle, riderMaterial))
            {
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
            var collider=zone.AddComponent<BoxCollider>();collider.size=new Vector3(1.15f,.8f,1.65f);collider.isTrigger=true;
            playerFlyHealth=zone.AddComponent<CombatTarget>();playerFlyHealth.maximumHealth=120;playerFlyHealth.ResetTarget();
        }
        public bool TakeFlyDamage(float damage)
        {
            if(!Mounted || !playerFlyHealth || !playerFlyHealth.Hit(damage))return false;
            message="Your fly took damage! "+Mathf.CeilToInt(playerFlyHealth.Health)+" HP";
            if(playerFlyHealth.Health<=0){ForceUnseat(RideVelocity.normalized*2+Vector3.up*1.5f);message="Your fly was brought down!";}
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
                animationVisual.AimUpperBody(Mounted ? RideRoot : avatar,view.transform.forward,aimWeight);
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
                weaponVisual.rotation = RideRoot.rotation;
                weaponVisual.position = animationVisual.Hand(false).position + RideRoot.forward * 1.05f;
            }
        }
        void SetWeapon()
        {
            var placeholder=weaponVisual.GetComponent<Renderer>();
            if(placeholder)placeholder.enabled=weapon==Weapon.Lance;
            if(bowVisual)bowVisual.gameObject.SetActive(weapon==Weapon.Bow);
            if(swordVisual)swordVisual.gameObject.SetActive(weapon==Weapon.Sword);
            weaponVisual.SetParent(Mounted ? saddle : avatar, false);
            weaponVisual.localPosition = Mounted ? new Vector3(.35f, .9f, 1.2f) : new Vector3(.3f, .8f, .6f);
            weaponVisual.localRotation = Quaternion.identity;
            weaponVisual.localScale = weapon == Weapon.Lance ? new Vector3(.055f, .055f, 2.5f) :
                weapon == Weapon.Sword ? Vector3.one : Vector3.one;
            var hand = animationVisual ? animationVisual.Hand(weapon == Weapon.Bow) : null;
            if (hand)
            {
                Vector3 size = weaponVisual.localScale;
                weaponVisual.SetParent(hand, false);
                weaponVisual.localPosition = weapon == Weapon.Lance ? new Vector3(0, 0, 1.05f) : Vector3.zero;
                weaponVisual.rotation = (Mounted ? saddle : avatar).rotation;
                Vector3 scale = hand.lossyScale;
                weaponVisual.localScale = new Vector3(size.x/Mathf.Max(.001f,scale.x),size.y/Mathf.Max(.001f,scale.y),size.z/Mathf.Max(.001f,scale.z));
                if(weapon==Weapon.Sword)ApplySwordHandPose();
            }
            lastTip = Tip();
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
        Vector3 Tip() { return weaponVisual.position + weaponVisual.forward * (weaponVisual.lossyScale.z / 2); }
        public bool TryDismount()
        {
            if (!Perched) { message = "Perch before dismounting."; return false; }
            Vector3 origin = RideRoot.position + Vector3.up * 2;
            // Choose a safe world-upright foothold; walls do not automatically dismount the rider.
            foreach (Vector3 offset in new[] { Vector3.right * 1.4f, Vector3.left * 1.4f, Vector3.back * 1.4f })
            {
                if (!Physics.Raycast(origin + offset, Vector3.down, out var hit, 5, 1,
                    QueryTriggerInteraction.Ignore) || Vector3.Dot(hit.normal, Vector3.up) < .75f) continue;
                Vector3 position = hit.point + Vector3.up * .06f;
                if (Physics.CheckCapsule(position + Vector3.up * .21f, position + Vector3.up * .99f, .2f,
                    1, QueryTriggerInteraction.Ignore)) continue;
                Mounted = false; RideInput.enabled = false; mountedVisual.gameObject.SetActive(false);
                var head = saddle.Find("Rider head"); if (head) head.gameObject.SetActive(false);
                avatar.position = position; avatar.rotation = Quaternion.Euler(0, view.transform.eulerAngles.y, 0);
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
            if (!Perched || Vector3.Distance(avatar.position, RideRoot.position) > 2.8f)
            { message = "Approach the perched fly to mount."; return false; }
            if (Physics.Linecast(avatar.position + Vector3.up * .6f, RideRoot.position, 1,
                QueryTriggerInteraction.Ignore)) { message = "Mounting path is blocked."; return false; }
            if(animationVisual)animationVisual.BeginMountTransition(true);
            Mounted = true; avatar.gameObject.SetActive(false); RideInput.enabled = true;
            mountedVisual.gameObject.SetActive(true);
            var head = saddle.Find("Rider head"); if (head) head.gameObject.SetActive(true);
            view.fly = RideRoot; view.rider = RideInput; view.followAnchorRotation=true; weapon = Weapon.Lance;
            view.SetOrientationSource(animationVisual ? animationVisual.Head : null,RideRoot);
            SetWeapon(); message = "Mounted; grip retained at every orientation."; return true;
        }
        public bool ForceUnseat(Vector3 impulse)
        {
            if(!Mounted || Defeated || !avatar || !feet)return false;
            Mounted=false;RideInput.enabled=false;RideInput.ResetCues();mountedVisual.gameObject.SetActive(false);
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
            Vector3 origin = animationVisual && animationVisual.Hand(true) ? weaponVisual.position : (Mounted ? saddle.position + saddle.up * .9f : avatar.position + Vector3.up * .9f);
            var camera = view.GetComponent<Camera>(); Ray aim = camera.ViewportPointToRay(new Vector3(.5f, .5f, 0));
            Vector3 point = Physics.Raycast(aim, out var hit, 100, 1, QueryTriggerInteraction.Ignore) ? hit.point : aim.GetPoint(100);
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
                    if(zone.fly)message=zone.owner.ReceiveFlyLanceContact(impact) ? (zone.owner.Mounted ? "Lance hit — enemy fly damaged!" : "Enemy fly disabled — opponent unseated!") : "Joust hit.";
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
            if(playerFlyHealth)playerFlyHealth.ResetTarget();
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
            GUI.color = new Color(.04f, .08f, .12f, .95f);
            GUI.DrawTexture(new Rect(16, Screen.height-198, 740, 182), Texture2D.whiteTexture); GUI.color = Color.white;
            if(animationVisual) animationVisual.mountTransitions=GUI.Toggle(new Rect(24,Screen.height-192,380,24),animationVisual.mountTransitions,"Borrowed mount transitions (experimental)");
            GUI.Label(new Rect(30, Screen.height-122, 720, 24), (research ? "SCIENTIFIC BODY / GAMEPLAY COMBAT — " : "COMBAT PROTOTYPE — ") + (Mounted ? "Mounted" : "On foot") + "   Weapon: " + weapon + "   Rider HP: " + Mathf.CeilToInt(Health)+(Mounted && playerFlyHealth ? "   Fly HP: "+Mathf.CeilToInt(playerFlyHealth.Health) : ""));
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
        {
            var mounted=GameObject.CreatePrimitive(PrimitiveType.Capsule);mounted.name="Mounted enemy jouster";
            if(parent)mounted.transform.SetParent(parent);mounted.GetComponent<Renderer>().sharedMaterial=weaponMaterial;
            mounted.AddComponent<CombatTarget>();var jouster=mounted.AddComponent<MountedJoustOpponent>();
            var biological=RideRoot.Find("Detailed NeuroMechFly appearance");
            jouster.Initialize(this,biological ? biological.gameObject : null,saddle,RideRoot,riderMaterial,weaponMaterial,center+RideRoot.right*10+Vector3.up*4);
            return mounted;
        }
        public void SetPractice(bool active,bool enemies)
        {
            practiceEnemies=enemies;
            if (practice) { practice.SetActive(false); Destroy(practice);practice=null; }
            if (active) CreatePractice();
        }
        void OnDestroy() { if (practice) Destroy(practice);if(standaloneJouster)Destroy(standaloneJouster); if (avatar) Destroy(avatar.gameObject); if (weaponVisual) Destroy(weaponVisual.gameObject); }
    }
}
