using UnityEngine;

namespace FruitFlyJoust
{
    public sealed class FlyFood : MonoBehaviour
    {
        public float nutrition=.65f;
        public float respawnDelay=35;
        float respawnAt;
        Renderer appearance;
        Collider volume;
        void Awake(){appearance=GetComponent<Renderer>();volume=GetComponent<Collider>();if(volume)volume.isTrigger=true;}
        public bool Available { get { return appearance && appearance.enabled; } }
        public void Eat()
        {
            if(!Available)return;appearance.enabled=false;if(volume)volume.enabled=false;respawnAt=Time.time+respawnDelay;
        }
        void Update()
        {
            if(!Available && Time.time>=respawnAt){appearance.enabled=true;if(volume)volume.enabled=true;}
        }
    }
}
