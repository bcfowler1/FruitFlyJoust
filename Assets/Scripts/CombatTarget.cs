using UnityEngine;

namespace FruitFlyJoust
{
    public sealed class CombatTarget : MonoBehaviour
    {
        public float maximumHealth = 100;
        public float Health { get; private set; }
        public float LastDamage { get; private set; }
        private Renderer appearance;
        private Color initialColor;
        void Awake()
        {
            appearance = GetComponent<Renderer>();
            initialColor = appearance && appearance.sharedMaterial ? appearance.sharedMaterial.color : Color.white;
            ResetTarget();
        }
        public void ResetTarget()
        { Health = maximumHealth;LastDamage=0;if (appearance) appearance.material.color = initialColor; }
        public bool Hit(float damage)
        { return Hit(damage,Vector3.zero); }
        public bool Hit(float damage,Vector3 incomingDirection)
        {
            if (Health <= 0 || float.IsNaN(damage) || float.IsInfinity(damage) || damage <= 0) return false;
            float before=Health;Health = Mathf.Max(0, Health - damage);LastDamage=before-Health;
            if(appearance)appearance.material.color = Health <= 0 ? Color.gray : Color.Lerp(Color.red, initialColor, Health / maximumHealth);
            FloatingDamageNumber.Create(transform,LastDamage,Health);
            var opponent=GetComponentInParent<CombatOpponent>();
            if(opponent && incomingDirection.sqrMagnitude>.0001f)opponent.ReactToArrow(incomingDirection);
            return true;
        }
        void OnDestroy() { if (appearance) Destroy(appearance.material); }
    }

    public sealed class FloatingDamageNumber : MonoBehaviour
    {
        private float age;
        public static void Create(Transform target,float damage,float remainingHealth)
        {
            var go=new GameObject("Damage "+Mathf.RoundToInt(damage));
            go.transform.position=target.position+Vector3.up*1.35f;
            var text=go.AddComponent<TextMesh>();text.text="-"+Mathf.RoundToInt(damage)+"\n"+Mathf.RoundToInt(remainingHealth)+" HP";
            text.anchor=TextAnchor.MiddleCenter;text.alignment=TextAlignment.Center;text.fontSize=64;
            text.characterSize=.06f;text.color=new Color(1,.2f,.08f,1);
            go.AddComponent<FloatingDamageNumber>();
        }
        void LateUpdate()
        {
            age+=Time.deltaTime;transform.position+=Vector3.up*.65f*Time.deltaTime;
            var camera=Camera.main;if(camera)transform.rotation=camera.transform.rotation;
            var text=GetComponent<TextMesh>();if(text)text.color=new Color(text.color.r,text.color.g,text.color.b,Mathf.Clamp01(1-age/1.2f));
            if(age>=1.2f)Destroy(gameObject);
        }
    }

    public sealed class FloatingNutritionNumber : MonoBehaviour
    {
        Transform target;TextMesh text;float amount,age;
        public float Amount { get { return amount; } }
        public static FloatingNutritionNumber Create(Transform target)
        {
            var go=new GameObject("Fly feeding nutrition");
            var display=go.AddComponent<FloatingNutritionNumber>();display.target=target;
            display.text=go.AddComponent<TextMesh>();display.text.anchor=TextAnchor.MiddleCenter;
            display.text.alignment=TextAlignment.Center;display.text.fontSize=64;
            display.text.characterSize=.06f;display.text.color=new Color(.25f,1,.25f,1);
            display.Place();return display;
        }
        public void Add(float nutrition)
        {
            if(float.IsNaN(nutrition)||float.IsInfinity(nutrition)||nutrition<=0)return;
            amount+=nutrition*100;age=0;
            text.text="+"+Mathf.Max(1,Mathf.RoundToInt(amount));text.color=new Color(.25f,1,.25f,1);Place();
        }
        void Place(){if(target)transform.position=target.position+Vector3.up*(1.35f+age*.35f);}
        void LateUpdate()
        {
            if(!target){Destroy(gameObject);return;}
            age+=Time.deltaTime;Place();var camera=Camera.main;if(camera)transform.rotation=camera.transform.rotation;
            if(text)text.color=new Color(text.color.r,text.color.g,text.color.b,Mathf.Clamp01(1-age/1.2f));
            if(age>=1.2f)Destroy(gameObject);
        }
    }

    public sealed class CombatArrow : MonoBehaviour
    {
        public RiderCombat clock;
        public Vector3 velocity;
        public float damage = 35;
        public bool scentedBait;
        private float age;
        void DeployBait(Vector3 point,Vector3 normal)
        {
            var bait=new GameObject("Scented arrow bait");bait.transform.position=point+normal*.05f;
            bait.AddComponent<ScentedBait>();
        }
        void Update()
        {
            float dt = clock ? clock.CombatDeltaTime : Time.deltaTime;
            if (clock && clock.CombatPaused || dt <= 0) return;
            Vector3 movement = velocity * dt + Physics.gravity * (.5f * dt * dt);
            RaycastHit selected=default(RaycastHit);bool collided=false;float nearest=float.PositiveInfinity;
            foreach(var candidateHit in Physics.SphereCastAll(transform.position,.035f,movement.normalized,movement.magnitude,
                1,QueryTriggerInteraction.Collide))
            {
                var candidate=candidateHit.collider.GetComponentInParent<CombatTarget>();
                if(clock && clock.IsOwnTarget(candidate))continue;
                // The broad fly-body box overlaps the lower saddle. Give the much
                // tighter rider volume a small selection preference so an arrow
                // visibly aimed at the rider does not kill the healthy mount first.
                var mountedZone=candidateHit.collider.GetComponentInParent<MountedHitZone>();
                float selectionDistance=candidateHit.distance+(mountedZone && mountedZone.fly ? .12f : 0);
                if(selectionDistance<nearest){nearest=selectionDistance;selected=candidateHit;collided=true;}
            }
            if(collided)
            {
                var target = selected.collider.GetComponentInParent<CombatTarget>();
                if(clock)clock.RecordArrowImpact(selected.collider,target);
                if(scentedBait)DeployBait(selected.point,selected.normal);
                else if (target) target.Hit(damage,velocity.normalized);
                Destroy(gameObject); return;
            }
            transform.position += movement; velocity += Physics.gravity * dt;
            if (velocity.sqrMagnitude > .001f) transform.rotation = Quaternion.LookRotation(velocity);
            age += dt;if(age>8){if(scentedBait)DeployBait(transform.position,Vector3.up);Destroy(gameObject);}
        }
    }

    // Each haltere has its own small health pool. Damage is relayed to the flight
    // controller as sensory loss rather than being counted as body damage twice.
    public sealed class HaltereHitZone : MonoBehaviour
    {
        public FlyMotor playerFly;public MountedJoustOpponent enemyFly;
        CombatTarget health;float previous;
        void Awake(){health=GetComponent<CombatTarget>();previous=health ? health.Health : 0;}
        void Update()
        {
            if(!health)return;float damage=Mathf.Max(0,previous-health.Health);previous=health.Health;
            if(damage<=0)return;if(playerFly)playerFly.DamageHaltere(damage);if(enemyFly)enemyFly.DamageHaltere(damage);
        }
    }

    // A wind-borne cone, not a global attractor. Enemy flies only acquire the bait
    // after entering this plume, then retain its last known source while searching.
    public sealed class ScentedBait : MonoBehaviour
    {
        public float lifetime=24,plumeLength=14,plumeWidth=3.5f;
        float age;Transform[] smokePuffs;
        public Vector3 WindDirection { get { return Vector3.right; } }
        public bool Contains(Vector3 point)
        {
            Vector3 offset=point-transform.position;float downwind=Vector3.Dot(offset,WindDirection);
            float width=Mathf.Lerp(.45f,plumeWidth,Mathf.Clamp01(downwind/plumeLength));
            return downwind>=0 && downwind<=plumeLength && Vector3.ProjectOnPlane(offset,WindDirection).magnitude<=width;
        }
        void Awake()
        {
            smokePuffs=new Transform[18];var shader=Shader.Find("Standard");var material=shader ? new Material(shader) : null;
            if(material){material.color=new Color(.72f,.92f,.18f,.22f);material.SetFloat("_Mode",3);material.SetInt("_SrcBlend",(int)UnityEngine.Rendering.BlendMode.SrcAlpha);material.SetInt("_DstBlend",(int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);material.SetInt("_ZWrite",0);material.EnableKeyword("_ALPHABLEND_ON");material.renderQueue=3000;}
            for(int i=0;i<smokePuffs.Length;i++){var puff=GameObject.CreatePrimitive(PrimitiveType.Sphere);puff.name="Scent plume";Destroy(puff.GetComponent<Collider>());puff.transform.SetParent(transform,false);puff.GetComponent<Renderer>().sharedMaterial=material;smokePuffs[i]=puff.transform;}
        }
        void Update()
        {
            age+=Time.deltaTime;
            for(int i=0;i<smokePuffs.Length;i++){float phase=Mathf.Repeat(age*.18f+i/(float)smokePuffs.Length,1);float distance=phase*plumeLength,width=Mathf.Lerp(.12f,plumeWidth,phase);Vector3 side=Vector3.up*Mathf.Sin(i*2.13f+age*.7f)*width*.34f+Vector3.forward*Mathf.Cos(i*1.71f+age*.5f)*width*.34f;smokePuffs[i].localPosition=WindDirection*distance+side;smokePuffs[i].localScale=Vector3.one*Mathf.Lerp(.12f,.72f,phase);}
            if(age>=lifetime)Destroy(gameObject);
        }
        void OnDrawGizmos(){Gizmos.color=new Color(.6f,1,.25f,.22f);Gizmos.DrawLine(transform.position,transform.position+WindDirection*plumeLength);}
    }
}
