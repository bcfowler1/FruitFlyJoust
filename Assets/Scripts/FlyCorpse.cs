using UnityEngine;

namespace FruitFlyJoust
{
    public sealed class FlyCorpse : MonoBehaviour
    {
        public float minimumLifetime=15;
        float age;
        Rigidbody physicsBody;
        Renderer[] renderers;
        public Vector3 Velocity { get { return physicsBody ? physicsBody.velocity : Vector3.zero; } }
        public static FlyCorpse Create(Transform source,Vector3 inheritedVelocity,bool clone)
        {
            if(!source)return null;
            Transform corpse=clone ? Instantiate(source.gameObject,source.position,source.rotation).transform : source;
            corpse.name="Dead fly corpse";corpse.SetParent(null,true);corpse.gameObject.SetActive(true);
            foreach(var behaviour in corpse.GetComponentsInChildren<MonoBehaviour>())if(!(behaviour is FlyCorpse))behaviour.enabled=false;
            foreach(var rigid in corpse.GetComponentsInChildren<Rigidbody>())Destroy(rigid);
            foreach(var collider in corpse.GetComponentsInChildren<Collider>())Destroy(collider);
            var result=corpse.gameObject.AddComponent<FlyCorpse>();result.Build(inheritedVelocity);return result;
        }
        void Build(Vector3 inheritedVelocity)
        {
            renderers=GetComponentsInChildren<Renderer>();
            Bounds bounds=new Bounds(transform.position,Vector3.one);
            if(renderers.Length>0){bounds=renderers[0].bounds;foreach(var renderer in renderers)bounds.Encapsulate(renderer.bounds);}
            var collider=gameObject.AddComponent<BoxCollider>();
            Vector3 scale=transform.lossyScale;collider.center=transform.InverseTransformPoint(bounds.center);
            collider.size=new Vector3(bounds.size.x/Mathf.Max(.001f,Mathf.Abs(scale.x)),bounds.size.y/Mathf.Max(.001f,Mathf.Abs(scale.y)),bounds.size.z/Mathf.Max(.001f,Mathf.Abs(scale.z)))*.72f;
            physicsBody=gameObject.AddComponent<Rigidbody>();physicsBody.mass=1.2f;physicsBody.interpolation=RigidbodyInterpolation.Interpolate;
            physicsBody.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;physicsBody.velocity=inheritedVelocity;
            physicsBody.angularVelocity=Vector3.Cross(transform.up,inheritedVelocity.normalized)*2.2f+transform.forward*.7f;
        }
        bool VisibleToMainCamera()
        {
            Camera camera=Camera.main;if(!camera)return false;
            var planes=GeometryUtility.CalculateFrustumPlanes(camera);
            foreach(var renderer in renderers)if(renderer && GeometryUtility.TestPlanesAABB(planes,renderer.bounds))return true;
            return false;
        }
        void FixedUpdate()
        {
            if(!physicsBody || renderers==null || renderers.Length==0)return;
            Bounds bounds=renderers[0].bounds;foreach(var renderer in renderers)if(renderer)bounds.Encapsulate(renderer.bounds);
            if(!Physics.Raycast(bounds.center+Vector3.up*2,Vector3.down,out var ground,6,1,QueryTriggerInteraction.Ignore) || Vector3.Dot(ground.normal,Vector3.up)<.65f)return;
            float penetration=Vector3.Dot(ground.point-bounds.min,ground.normal);
            if(penetration<=.02f)return;
            physicsBody.position+=ground.normal*(penetration+.025f);
            float intoGround=Vector3.Dot(physicsBody.velocity,ground.normal);
            if(intoGround<0)physicsBody.velocity-=ground.normal*intoGround;
        }
        void Update(){age+=Time.deltaTime;if(age>=minimumLifetime&&!VisibleToMainCamera())Destroy(gameObject);}
    }
}
