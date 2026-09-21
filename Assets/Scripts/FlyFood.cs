using UnityEngine;

namespace FruitFlyJoust
{
    public sealed class FlyFood : MonoBehaviour
    {
        public float nutrition=.65f;
        public float respawnDelay=35;
        [Range(.01f,.95f)] public float remainingConsumedPerSecond=.3f;
        float respawnAt;
        float remaining=1;
        Vector3 fullScale;
        Renderer appearance;
        Collider volume;
        void Awake(){appearance=GetComponent<Renderer>();volume=GetComponent<Collider>();if(volume)volume.isTrigger=true;fullScale=transform.localScale;ApplyScale();}
        public bool Available { get { return appearance && appearance.enabled && remaining>.01f; } }
        public float RemainingFraction { get { return remaining; } }
        public float Consume(float seconds)
        {
            if(!Available || seconds<=0)return 0;
            float fraction=1-Mathf.Pow(1-remainingConsumedPerSecond,seconds);
            float consumed=remaining*fraction;remaining=Mathf.Max(0,remaining-consumed);ApplyScale();
            if(remaining<=.01f)Deplete();return nutrition*consumed;
        }
        public void Eat()
        {
            if(!Available)return;remaining=0;ApplyScale();Deplete();
        }
        void ApplyScale(){if(fullScale==Vector3.zero)fullScale=transform.localScale;transform.localScale=fullScale*remaining;}
        void Deplete(){if(appearance)appearance.enabled=false;if(volume)volume.enabled=false;respawnAt=Time.time+respawnDelay;}
        void Update()
        {
            if(!Available && Time.time>=respawnAt){remaining=1;ApplyScale();appearance.enabled=true;if(volume)volume.enabled=true;}
        }
    }
}
