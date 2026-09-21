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
        public bool HasGroundContact { get; private set; }
        public static FlyCorpse Create(Transform source,Vector3 inheritedVelocity,bool clone)
        {
            if(!source)return null;
            Transform corpse=clone ? Instantiate(source.gameObject,source.position,source.rotation).transform : source;
            corpse.name="Dead fly corpse";corpse.SetParent(null,true);corpse.gameObject.SetActive(true);
            foreach(var behaviour in corpse.GetComponentsInChildren<MonoBehaviour>())if(!(behaviour is FlyCorpse))behaviour.enabled=false;
            // A cloned fly can contain an active rider ragdoll. Joints must be
            // removed before their required Rigidbody components.
            foreach(var joint in corpse.GetComponentsInChildren<Joint>())
                {if(clone)DestroyImmediate(joint);else Destroy(joint);}
            foreach(var rigid in corpse.GetComponentsInChildren<Rigidbody>())
                {if(clone)DestroyImmediate(rigid);else Destroy(rigid);}
            foreach(var collider in corpse.GetComponentsInChildren<Collider>())
                {if(clone)DestroyImmediate(collider);else Destroy(collider);}
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
            collider.material=new PhysicMaterial("Dead fly") { dynamicFriction=.6f,staticFriction=.75f,bounciness=.03f,frictionCombine=PhysicMaterialCombine.Maximum,bounceCombine=PhysicMaterialCombine.Minimum };
            physicsBody=gameObject.AddComponent<Rigidbody>();physicsBody.mass=1.2f;physicsBody.interpolation=RigidbodyInterpolation.Interpolate;
            physicsBody.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;physicsBody.drag=.08f;physicsBody.angularDrag=1.2f;
            // A flying mount can have a strong climb component at the instant it dies.
            // Keep its planar momentum, but death removes lift immediately so the corpse
            // starts descending instead of continuing upward like a powered aircraft.
            Vector3 planar=Vector3.ProjectOnPlane(inheritedVelocity,Vector3.up);
            if(planar.magnitude>7)planar=planar.normalized*7;
            physicsBody.velocity=planar+Vector3.down*1.5f;
            physicsBody.angularVelocity=planar.sqrMagnitude>.01f ? Vector3.Cross(transform.up,planar.normalized)*1.2f+transform.forward*.35f : transform.forward*.35f;
        }
        void OnCollisionStay(Collision collision)
        {
            foreach(var contact in collision.contacts)if(Vector3.Dot(contact.normal,Vector3.up)>.55f){HasGroundContact=true;break;}
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
