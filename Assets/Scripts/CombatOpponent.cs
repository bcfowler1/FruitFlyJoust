using UnityEngine;

namespace FruitFlyJoust
{
    [RequireComponent(typeof(CombatTarget), typeof(CharacterController))]
    public sealed class CombatOpponent : MonoBehaviour
    {
        public enum Style { Swordsman, Archer }
        public Style style;
        public float sightRange = 18, speed = 2, attackInterval = 1.2f;
        public float spyglassRange=24,communicationRange=18;
        private CombatTarget target;
        private CharacterController feet;
        private RiderCombat rider;
        private RiderAnimationVisual visual;
        private Transform weaponVisual;
        private Transform spyglassVisual;
        private float cooldown, falling, attackGesture, strikeDelay, supportCheckTimer, unsupportedTime;
        private float spyglassObservation,sharedAwarenessTime,evadeTime;
        private Vector3 sharedTarget,evadeDirection;
        private bool strikePending, alternateCut;
        private bool ownsVisual;
        private Vector3 spawn;
        public bool Defeated { get { return !target || target.Health<=0; } }
        public bool Ragdolled { get { return visual && visual.Ragdolled; } }
        public bool WeaponVisible { get { return weaponVisual && weaponVisual.gameObject.activeInHierarchy; } }
        public float VisualHeight { get { return visual ? visual.VisualHeight : 0; } }
        public Vector3 VisualWorldScale { get { return visual ? visual.VisualWorldScale : Vector3.zero; } }
        public float SightRange { get { return sightRange; } }
        public bool RiderVisible { get; private set; }
        public bool HasSpyglass { get; private set; }
        public bool SharedAwareness { get { return sharedAwarenessTime>0; } }
        public bool EvadingIncomingFire { get { return evadeTime>0; } }
        public bool SpyglassVisible { get { return spyglassVisual && spyglassVisual.gameObject.activeInHierarchy; } }
        public float GroundFootError { get; private set; }
        public bool SupportedByWalkableGround { get; private set; }
        public float UnsupportedTime { get { return unsupportedTime; } }
        void Start()
        {
            target = GetComponent<CombatTarget>(); feet = GetComponent<CharacterController>();
            rider = FindObjectOfType<RiderCombat>(); spawn = transform.position;
            // Match the player's on-foot body and collision dimensions. Perception
            // remains expressed in world metres and is intentionally not scaled.
            feet.height=1.2f;feet.radius=.2f;feet.center=new Vector3(0,.6f,0);
            // CharacterController provides collision; remove the primitive's duplicate capsule.
            var primitive = GetComponent<CapsuleCollider>(); if (primitive) Destroy(primitive);
            var renderer=GetComponent<Renderer>();Material material=renderer ? renderer.sharedMaterial : null;if(renderer)renderer.enabled=false;
            visual=GetComponent<RiderAnimationVisual>();
            if(!visual){visual=gameObject.AddComponent<RiderAnimationVisual>();ownsVisual=true;visual.Create(transform,material);}
            visual.visualScale=rider ? rider.OnFootVisualScale : .6f;visual.Pose(transform,false,0);
            float groundY=transform.TransformPoint(feet.center).y-feet.height*.5f;
            GroundFootError=visual.AlignFeetToWorldY(groundY+.01f);BuildWeapon(material);BuildSpyglass(material);
            RefreshGroundSupport();
        }
        void BuildSpyglass(Material material)
        {
            var glass=GameObject.CreatePrimitive(PrimitiveType.Cylinder);glass.name="Enemy spyglass";
            Destroy(glass.GetComponent<Collider>());glass.GetComponent<Renderer>().sharedMaterial=material;spyglassVisual=glass.transform;
            Transform hand=visual ? visual.Hand(false) : null;spyglassVisual.SetParent(hand ? hand : transform,false);
            spyglassVisual.localPosition=new Vector3(0,.02f,.16f);spyglassVisual.localRotation=Quaternion.Euler(90,0,0);
            spyglassVisual.localScale=new Vector3(.055f,.24f,.055f);spyglassVisual.gameObject.SetActive(false);
        }
        void BuildWeapon(Material material)
        {
            var weapon=GameObject.CreatePrimitive(PrimitiveType.Cube);weapon.name=style==Style.Swordsman ? "Enemy sword" : "Enemy bow";
            Destroy(weapon.GetComponent<Collider>());weapon.GetComponent<Renderer>().sharedMaterial=material;weaponVisual=weapon.transform;
            Transform hand=visual ? visual.Hand(style==Style.Archer) : null;weaponVisual.SetParent(hand ? hand : transform,false);
            weaponVisual.localPosition=style==Style.Swordsman ? new Vector3(0,0,.28f) : new Vector3(0,0,.18f);
            weaponVisual.localScale=style==Style.Swordsman ? new Vector3(.045f,.045f,.65f) : new Vector3(.04f,.55f,.04f);
        }
        public void ResetOpponent()
        { if(visual)visual.ExitRagdoll();feet.enabled = false; transform.position = spawn; feet.enabled = true; falling = 0; cooldown = attackGesture = spyglassObservation = sharedAwarenessTime = evadeTime = 0;strikePending=false;target.ResetTarget(); }
        void Update()
        {
            if(target && target.Health<=0){HasSpyglass=false;if(spyglassVisual)spyglassVisual.gameObject.SetActive(false);if(feet.enabled)feet.enabled=false;if(visual&&!visual.Ragdolled)visual.EnterRagdoll(Vector3.up*.5f+transform.forward);return;}
            if (!rider || rider.Defeated || rider.CombatPaused) return;
            float dt = rider.CombatDeltaTime;
            if (dt <= 0) return;
            supportCheckTimer-=dt;
            if(supportCheckTimer<=0){RefreshGroundSupport();supportCheckTimer=.08f;}
            unsupportedTime=SupportedByWalkableGround ? 0 : unsupportedTime+dt;
            cooldown = Mathf.Max(0, cooldown-dt);
            attackGesture=Mathf.Max(0,attackGesture-dt);
            sharedAwarenessTime=Mathf.Max(0,sharedAwarenessTime-dt);evadeTime=Mathf.Max(0,evadeTime-dt);
            if(strikePending)
            {
                strikeDelay-=dt;
                if(strikeDelay<=0){strikePending=false;if(Vector3.Distance(rider.RiderPosition,transform.position)<2)rider.TakeDamage(12);}
            }
            Vector3 difference = rider.RiderPosition-transform.position;
            Vector3 horizontal = Vector3.ProjectOnPlane(difference, Vector3.up);
            UpdateSpyglassAssignment();
            bool visible = difference.magnitude <= (HasSpyglass ? spyglassRange : sightRange) && HasLineOfSight(
                transform.position+Vector3.up*.6f,rider.RiderPosition+Vector3.up*.6f);
            RiderVisible=visible;
            bool spyglassContact=HasSpyglass && visible && difference.magnitude>sightRange;
            spyglassObservation=spyglassContact ? spyglassObservation+dt : 0;
            if(spyglassObservation>=2){BroadcastSight(rider.RiderPosition);spyglassObservation=0;}
            Vector3 motion = Vector3.zero;
            if(evadeTime>0)motion=evadeDirection*speed*1.35f;
            else if(spyglassContact)motion=Vector3.zero;
            else if ((visible || sharedAwarenessTime>0) && horizontal.sqrMagnitude > .01f)
            {
                Vector3 pursuit=visible ? horizontal : Vector3.ProjectOnPlane(sharedTarget-transform.position,Vector3.up);
                if(pursuit.sqrMagnitude>.01f)transform.rotation = Quaternion.LookRotation(pursuit);
                float desired = style == Style.Archer ? 7 : 1.3f;
                if (pursuit.magnitude > desired) motion = pursuit.normalized*speed;
                else if (style == Style.Archer && pursuit.magnitude < 4) motion = -pursuit.normalized*speed;
                if (visible && cooldown == 0 && (style == Style.Archer || difference.magnitude < 1.8f))
                {
                    if (style == Style.Archer)
                    {
                        Vector3 origin = transform.position+Vector3.up*.7f;
                        var arrow = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        arrow.name = "Opponent arrow"; Destroy(arrow.GetComponent<Collider>());
                        arrow.transform.position = origin; arrow.transform.localScale = new Vector3(.04f,.04f,.5f);
                        var projectile = arrow.AddComponent<OpponentArrow>(); projectile.rider = rider;
                        Vector3 aim = rider.RiderPosition+Vector3.up*.6f-origin;
                        Vector3 planar = Vector3.ProjectOnPlane(aim,Vector3.up);
                        float distance = planar.magnitude, velocitySquared = 16*16;
                        float gravity = Mathf.Abs(Physics.gravity.y);
                        float discriminant = velocitySquared*velocitySquared-gravity*(gravity*distance*distance+2*aim.y*velocitySquared);
                        if (discriminant >= 0 && distance > .01f)
                        {
                            float angle = Mathf.Atan((velocitySquared-Mathf.Sqrt(discriminant))/(gravity*distance));
                            projectile.velocity = planar.normalized*(16*Mathf.Cos(angle))+Vector3.up*(16*Mathf.Sin(angle));
                        }
                        else { Destroy(arrow); cooldown = attackInterval; return; }
                    }
                    else {attackGesture=.55f;strikeDelay=.2f;strikePending=true;alternateCut=!alternateCut;}
                    cooldown = attackInterval;
                }
            }
            // CharacterController.isGrounded can briefly be true against a fly,
            // weapon, or another fighter. Only a nearby static, upward-facing
            // surface may promote this actor into its walking state.
            if(!SupportedByWalkableGround)motion=Vector3.zero;
            if (feet.isGrounded && SupportedByWalkableGround) falling = -2;
            falling += Physics.gravity.y*dt;
            feet.Move((motion+Vector3.up*falling)*dt);
        }
        void RefreshGroundSupport()
        {
            SupportedByWalkableGround=false;
            Vector3 origin=transform.position+Vector3.up*.18f;
            float nearest=float.PositiveInfinity;
            foreach(var hit in Physics.RaycastAll(origin,Vector3.down,.5f,1,QueryTriggerInteraction.Ignore))
            {
                if(!hit.collider || hit.distance>=nearest || Vector3.Dot(hit.normal,Vector3.up)<.65f)continue;
                Transform candidate=hit.collider.transform;
                if(candidate==transform || candidate.IsChildOf(transform))continue;
                // Characters, mounts, loose weapons, and corpses are transient
                // contacts; accepting one is what left recovered riders in midair.
                if(hit.collider.GetComponentInParent<CharacterController>() ||
                   hit.collider.GetComponentInParent<MountedJoustOpponent>() ||
                   hit.collider.attachedRigidbody)continue;
                float clearance=Vector3.Dot(transform.position-hit.point,hit.normal);
                if(clearance<-.04f || clearance>.22f)continue;
                nearest=hit.distance;SupportedByWalkableGround=true;
            }
        }
        void UpdateSpyglassAssignment()
        {
            var units=FindObjectsOfType<CombatOpponent>();bool anyoneInNormalRange=false;CombatOpponent selected=null;
            foreach(var unit in units)
            {
                if(!unit || unit.Defeated)continue;
                if(!selected || unit.GetInstanceID()<selected.GetInstanceID())selected=unit;
                if(unit.rider && Vector3.Distance(unit.transform.position,unit.rider.RiderPosition)<=unit.sightRange)anyoneInNormalRange=true;
            }
            HasSpyglass=!anyoneInNormalRange && selected==this;
            if(spyglassVisual)spyglassVisual.gameObject.SetActive(HasSpyglass);
            if(!HasSpyglass)spyglassObservation=0;
        }
        void BroadcastSight(Vector3 playerPosition)
        {
            foreach(var ally in FindObjectsOfType<CombatOpponent>())
                if(ally && ally!=this && !ally.Defeated && Vector3.Distance(transform.position,ally.transform.position)<=communicationRange)
                    ally.ReceiveSharedSight(playerPosition);
        }
        void ReceiveSharedSight(Vector3 playerPosition){sharedTarget=playerPosition;sharedAwarenessTime=5;}
        public void ReactToArrow(Vector3 incomingDirection)
        {
            Vector3 travel=Vector3.ProjectOnPlane(incomingDirection,Vector3.up).normalized;if(travel.sqrMagnitude<.01f)travel=transform.forward;
            float side=(GetInstanceID()&1)==0 ? 1 : -1;
            evadeDirection=(travel+Vector3.Cross(Vector3.up,travel)*side*.55f).normalized;evadeTime=3;cooldown=Mathf.Max(cooldown,.35f);
            foreach(var ally in FindObjectsOfType<CombatOpponent>())
                if(ally && ally!=this && !ally.Defeated && Vector3.Distance(transform.position,ally.transform.position)<=communicationRange)
                    ally.ReceiveIncomingWarning(travel);
        }
        void ReceiveIncomingWarning(Vector3 travel)
        {
            float side=(GetInstanceID()&1)==0 ? 1 : -1;
            evadeDirection=(travel+Vector3.Cross(Vector3.up,travel)*side).normalized;evadeTime=Mathf.Max(evadeTime,2);
        }
        bool HasLineOfSight(Vector3 start,Vector3 end)
        {
            Vector3 delta=end-start;float distance=delta.magnitude;if(distance<.01f)return true;
            foreach(var hit in Physics.RaycastAll(start,delta/distance,distance,1,QueryTriggerInteraction.Ignore))
            {
                Transform candidate=hit.collider.transform;
                if(candidate==transform || candidate.IsChildOf(transform))continue;
                // A collider reached only at the destination belongs to the rider or
                // their mount and confirms sight rather than blocking it.
                if(hit.distance>=distance-.5f)continue;
                return false;
            }
            return true;
        }
        void LateUpdate()
        {
            if(!visual || visual.Ragdolled)return;
            float speed=feet && feet.enabled && SupportedByWalkableGround ? Vector3.ProjectOnPlane(feet.velocity,Vector3.up).magnitude : 0;
            visual.Pose(transform,false,speed,target && target.Health<=0);
            if(style==Style.Swordsman && attackGesture>0)visual.PoseSwordAttack(alternateCut ? RiderCombat.SwordAttack.LeftToRight : RiderCombat.SwordAttack.RightToLeft,attackGesture/.55f);
        }
        void OnDestroy(){if(weaponVisual)Destroy(weaponVisual.gameObject);if(spyglassVisual)Destroy(spyglassVisual.gameObject);if(ownsVisual && visual)Destroy(visual);}
    }
    public sealed class OpponentArrow : MonoBehaviour
    {
        public Vector3 velocity;
        public RiderCombat rider;
        private float age;
        void Update()
        {
            float dt = rider ? rider.CombatDeltaTime : Time.deltaTime;
            if (rider && rider.CombatPaused || dt <= 0) return;
            Vector3 movement = velocity*dt+Physics.gravity*(.5f*dt*dt);
            // Query the rider mathematically as its render/body colliders are intentionally ignored.
            Vector3 center = rider.RiderPosition+Vector3.up*.6f;
            float projection = Mathf.Clamp01(Vector3.Dot(center-transform.position,movement)/Mathf.Max(1e-8f,movement.sqrMagnitude));
            float distance = Vector3.Distance(center,transform.position+movement*projection);
            bool blocked = Physics.SphereCast(transform.position,.035f,movement.normalized,out var hit,movement.magnitude,1,
                QueryTriggerInteraction.Ignore);
            if (distance < .45f && (!blocked || hit.distance > movement.magnitude*projection))
            { rider.TakeDamage(10); Destroy(gameObject); return; }
            if (blocked) { Destroy(gameObject); return; }
            transform.position += movement; velocity += Physics.gravity*dt;
            if (velocity.sqrMagnitude > .001f) transform.rotation = Quaternion.LookRotation(velocity);
            age += dt; if (age > 5) Destroy(gameObject);
        }
    }
}
