using UnityEngine;

namespace FruitFlyJoust
{
    [RequireComponent(typeof(CombatTarget), typeof(CharacterController))]
    public sealed class CombatOpponent : MonoBehaviour
    {
        public enum Style { Swordsman, Archer }
        public Style style;
        public float sightRange = 18, speed = 2, attackInterval = 1.2f;
        private CombatTarget target;
        private CharacterController feet;
        private RiderCombat rider;
        private RiderAnimationVisual visual;
        private Transform weaponVisual;
        private float cooldown, falling, attackGesture, strikeDelay;
        private bool strikePending, alternateCut;
        private bool ownsVisual;
        private Vector3 spawn;
        public bool Defeated { get { return !target || target.Health<=0; } }
        public bool Ragdolled { get { return visual && visual.Ragdolled; } }
        public bool WeaponVisible { get { return weaponVisual && weaponVisual.gameObject.activeInHierarchy; } }
        public float VisualHeight { get { return visual ? visual.VisualHeight : 0; } }
        public float GroundFootError { get; private set; }
        void Start()
        {
            target = GetComponent<CombatTarget>(); feet = GetComponent<CharacterController>();
            rider = FindObjectOfType<RiderCombat>(); spawn = transform.position;
            // CharacterController provides collision; remove the primitive's duplicate capsule.
            var primitive = GetComponent<CapsuleCollider>(); if (primitive) Destroy(primitive);
            var renderer=GetComponent<Renderer>();Material material=renderer ? renderer.sharedMaterial : null;if(renderer)renderer.enabled=false;
            visual=GetComponent<RiderAnimationVisual>();
            if(!visual){visual=gameObject.AddComponent<RiderAnimationVisual>();ownsVisual=true;visual.Create(transform,material);}
            visual.visualScale=.48f;visual.Pose(transform,false,0);
            float groundY=transform.TransformPoint(feet.center).y-feet.height*.5f;
            GroundFootError=visual.AlignFeetToWorldY(groundY+.01f);BuildWeapon(material);
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
        { if(visual)visual.ExitRagdoll();feet.enabled = false; transform.position = spawn; feet.enabled = true; falling = 0; cooldown = attackGesture = 0;strikePending=false;target.ResetTarget(); }
        void Update()
        {
            if(target && target.Health<=0){if(feet.enabled)feet.enabled=false;if(visual&&!visual.Ragdolled)visual.EnterRagdoll(Vector3.up*.5f+transform.forward);return;}
            if (!rider || rider.Defeated || rider.CombatPaused) return;
            float dt = rider.CombatDeltaTime;
            if (dt <= 0) return;
            cooldown = Mathf.Max(0, cooldown-dt);
            attackGesture=Mathf.Max(0,attackGesture-dt);
            if(strikePending)
            {
                strikeDelay-=dt;
                if(strikeDelay<=0){strikePending=false;if(Vector3.Distance(rider.RiderPosition,transform.position)<2)rider.TakeDamage(12);}
            }
            Vector3 difference = rider.RiderPosition-transform.position;
            Vector3 horizontal = Vector3.ProjectOnPlane(difference, Vector3.up);
            bool visible = difference.magnitude <= sightRange &&
                !Physics.Linecast(transform.position+Vector3.up*.6f, rider.RiderPosition+Vector3.up*.6f,
                    1, QueryTriggerInteraction.Ignore);
            Vector3 motion = Vector3.zero;
            if (visible && horizontal.sqrMagnitude > .01f)
            {
                transform.rotation = Quaternion.LookRotation(horizontal);
                float desired = style == Style.Archer ? 7 : 1.3f;
                if (horizontal.magnitude > desired) motion = horizontal.normalized*speed;
                else if (style == Style.Archer && horizontal.magnitude < 4) motion = -horizontal.normalized*speed;
                if (cooldown == 0 && (style == Style.Archer || difference.magnitude < 1.8f))
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
            if (feet.isGrounded) falling = -2;
            falling += Physics.gravity.y*dt;
            feet.Move((motion+Vector3.up*falling)*dt);
        }
        void LateUpdate()
        {
            if(!visual || visual.Ragdolled)return;
            float speed=feet && feet.enabled ? feet.velocity.magnitude : 0;visual.Pose(transform,false,speed,target && target.Health<=0);
            if(style==Style.Swordsman && attackGesture>0)visual.PoseSwordAttack(alternateCut ? RiderCombat.SwordAttack.LeftToRight : RiderCombat.SwordAttack.RightToLeft,attackGesture/.55f);
        }
        void OnDestroy(){if(weaponVisual)Destroy(weaponVisual.gameObject);if(ownsVisual && visual)Destroy(visual);}
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
