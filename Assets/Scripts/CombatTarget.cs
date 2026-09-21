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
        {
            if (Health <= 0 || float.IsNaN(damage) || float.IsInfinity(damage) || damage <= 0) return false;
            float before=Health;Health = Mathf.Max(0, Health - damage);LastDamage=before-Health;
            if(appearance)appearance.material.color = Health <= 0 ? Color.gray : Color.Lerp(Color.red, initialColor, Health / maximumHealth);
            FloatingDamageNumber.Create(transform,LastDamage,Health);
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
        private float age;
        void Update()
        {
            float dt = clock ? clock.CombatDeltaTime : Time.deltaTime;
            if (clock && clock.CombatPaused || dt <= 0) return;
            Vector3 movement = velocity * dt + Physics.gravity * (.5f * dt * dt);
            if (Physics.SphereCast(transform.position, .035f, movement.normalized, out var hit, movement.magnitude,
                1, QueryTriggerInteraction.Ignore))
            {
                var target = hit.collider.GetComponentInParent<CombatTarget>();
                if (target) target.Hit(damage);
                Destroy(gameObject); return;
            }
            transform.position += movement; velocity += Physics.gravity * dt;
            if (velocity.sqrMagnitude > .001f) transform.rotation = Quaternion.LookRotation(velocity);
            age += dt; if (age > 8) Destroy(gameObject);
        }
    }
}
