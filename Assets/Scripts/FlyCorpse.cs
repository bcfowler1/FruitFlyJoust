using UnityEngine;
using System.Collections.Generic;

namespace FruitFlyJoust
{
    public sealed class FlyCorpse : MonoBehaviour
    {
        public float minimumLifetime=15;
        float age,groundContactSeconds;
        Rigidbody physicsBody;
        Renderer[] renderers;
        bool groundImpactDamped;
        readonly List<Rigidbody> articulatedBodies=new List<Rigidbody>();
        public int ArticulatedBodyCount { get { return articulatedBodies.Count; } }
        public int AppendageColliderCount { get; private set; }
        public Vector3 Velocity { get { return physicsBody ? physicsBody.velocity : Vector3.zero; } }
        public bool HasGroundContact { get; private set; }
        public float RestingSeconds { get; private set; }
        public float GroundClearance { get; private set; }=float.PositiveInfinity;
        public int VisibleRendererCount
        {
            get { int count=0;if(renderers!=null)foreach(var renderer in renderers)if(renderer && renderer.enabled && renderer.gameObject.activeInHierarchy)count++;return count; }
        }
        public float VisualBoundsSize
        {
            get { if(renderers==null || renderers.Length==0)return 0;Bounds bounds=renderers[0].bounds;foreach(var renderer in renderers)if(renderer)bounds.Encapsulate(renderer.bounds);return bounds.size.magnitude; }
        }
        public Vector3 VisualCenter
        {
            get{if(renderers==null||renderers.Length==0)return transform.position;Bounds bounds=renderers[0].bounds;foreach(var renderer in renderers)if(renderer)bounds.Encapsulate(renderer.bounds);return bounds.center;}
        }
        Bounds SupportCoreBounds()
        {
            Bounds bounds=new Bounds(transform.position,Vector3.zero);bool found=false;
            if(renderers!=null)foreach(var renderer in renderers)if(renderer)
            {
                string part=renderer.name;
                if(!(part.Contains("Thorax")||part.Contains("A1A2")||part.Contains("A3")||part.Contains("A4")||part.Contains("A5")||part.Contains("A6")))continue;
                if(!found){bounds=renderer.bounds;found=true;}else bounds.Encapsulate(renderer.bounds);
            }
            if(found)return bounds;
            if(renderers!=null&&renderers.Length>0){bounds=renderers[0].bounds;foreach(var renderer in renderers)if(renderer)bounds.Encapsulate(renderer.bounds);}
            return bounds;
        }
        public static FlyCorpse Create(Transform source,Vector3 inheritedVelocity,bool clone)
        {
            if(!source)return null;
            // Instantiate in the authored parent frame first. The position/rotation overload
            // treats the source's local scale as world scale, which can make a mounted
            // biological fly corpse microscopic or otherwise invisible after detaching it.
            Transform corpse;
            if(clone)
            {
                corpse=Instantiate(source.gameObject,source.parent).transform;
                corpse.localPosition=source.localPosition;corpse.localRotation=source.localRotation;corpse.localScale=source.localScale;
            }
            else corpse=source;
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
            var material=new PhysicMaterial("Dead fly") { dynamicFriction=.6f,staticFriction=.75f,bounciness=.03f,frictionCombine=PhysicMaterialCombine.Maximum,bounceCombine=PhysicMaterialCombine.Minimum };
            Bounds coreBounds=new Bounds(transform.position,Vector3.zero);bool hasCore=false;
            foreach(var renderer in renderers)
            {
                string part=renderer.name;
                if(!(part.Contains("Thorax")||part.Contains("Head")||part.Contains("A1A2")||part.Contains("A3")||part.Contains("A4")||part.Contains("A5")||part.Contains("A6")))continue;
                if(!hasCore){coreBounds=renderer.bounds;hasCore=true;}else coreBounds.Encapsulate(renderer.bounds);
            }
            if(!hasCore)coreBounds=new Bounds(bounds.center,bounds.size*.45f);
            var collider=gameObject.AddComponent<BoxCollider>();
            Vector3 scale=transform.lossyScale;collider.center=transform.InverseTransformPoint(coreBounds.center);
            collider.size=new Vector3(coreBounds.size.x/Mathf.Max(.001f,Mathf.Abs(scale.x)),coreBounds.size.y/Mathf.Max(.001f,Mathf.Abs(scale.y)),coreBounds.size.z/Mathf.Max(.001f,Mathf.Abs(scale.z)))*.78f;
            collider.material=material;
            physicsBody=gameObject.AddComponent<Rigidbody>();physicsBody.mass=1.2f;physicsBody.interpolation=RigidbodyInterpolation.Interpolate;
            physicsBody.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;physicsBody.drag=.08f;physicsBody.angularDrag=1.2f;
            physicsBody.solverIterations=18;physicsBody.solverVelocityIterations=8;physicsBody.maxDepenetrationVelocity=1.5f;
            physicsBody.useGravity=true;physicsBody.isKinematic=false;physicsBody.detectCollisions=true;physicsBody.constraints=RigidbodyConstraints.None;
            // A flying mount can have a strong climb component at the instant it dies.
            // Keep its planar momentum, but death removes lift immediately so the corpse
            // starts descending instead of continuing upward like a powered aircraft.
            Vector3 planar=Vector3.ProjectOnPlane(inheritedVelocity,Vector3.up);
            if(planar.magnitude>3.5f)planar=planar.normalized*3.5f;
            physicsBody.velocity=planar+Vector3.down*1.5f;
            physicsBody.angularVelocity=planar.sqrMagnitude>.01f ? Vector3.Cross(transform.up,planar.normalized)*.8f+transform.forward*.25f : transform.forward*.25f;
            BuildAppendageRagdoll(material,physicsBody,inheritedVelocity);
            // Articulated pieces share one corpse. They collide with the world, but
            // never push against sibling segments and hold the body above the floor.
            var corpseColliders=GetComponentsInChildren<Collider>();
            for(int a=0;a<corpseColliders.Length;a++)for(int b=a+1;b<corpseColliders.Length;b++)
                Physics.IgnoreCollision(corpseColliders[a],corpseColliders[b],true);
        }
        static bool Appendage(string name)
        {return name.Contains("Wing")||name.Contains("Coxa")||name.Contains("Femur")||name.Contains("Tibia")||name.Contains("Tarsus");}
        void BuildAppendageRagdoll(PhysicMaterial material,Rigidbody rootBody,Vector3 inheritedVelocity)
        {
            var bodies=new Dictionary<string,Rigidbody>();
            foreach(var renderer in renderers)
            {
                Transform part=renderer.transform;string name=part.name;if(!Appendage(name)||part==transform||bodies.ContainsKey(name))continue;
                Bounds bounds=renderer.bounds;Vector3 scale=part.lossyScale;
                var box=part.gameObject.AddComponent<BoxCollider>();box.center=part.InverseTransformPoint(bounds.center);
                Vector3 size=new Vector3(bounds.size.x/Mathf.Max(.001f,Mathf.Abs(scale.x)),bounds.size.y/Mathf.Max(.001f,Mathf.Abs(scale.y)),bounds.size.z/Mathf.Max(.001f,Mathf.Abs(scale.z)))*.62f;
                float minimum=name.Contains("Wing") ? .018f : .025f;box.size=new Vector3(Mathf.Max(minimum,size.x),Mathf.Max(minimum,size.y),Mathf.Max(minimum,size.z));box.material=material;
                var body=part.gameObject.AddComponent<Rigidbody>();body.mass=name.Contains("Wing") ? .018f : .032f;body.drag=.18f;body.angularDrag=1.4f;
                body.interpolation=RigidbodyInterpolation.Interpolate;body.collisionDetectionMode=CollisionDetectionMode.ContinuousSpeculative;
                body.solverIterations=18;body.solverVelocityIterations=8;body.maxDepenetrationVelocity=1.5f;
                body.velocity=Vector3.ProjectOnPlane(inheritedVelocity,Vector3.up)*.65f+Vector3.down*1.2f;body.angularVelocity=rootBody.angularVelocity;
                bodies.Add(name,body);articulatedBodies.Add(body);AppendageColliderCount++;
            }
            foreach(var pair in bodies)
            {
                string name=pair.Key;Rigidbody connected=rootBody;
                string previous=null;
                if(name.Contains("Femur"))previous=name.Replace("Femur","Coxa");
                else if(name.Contains("Tibia"))previous=name.Replace("Tibia","Femur");
                else if(name.Contains("Tarsus"))
                {
                    int marker=name.IndexOf("Tarsus");int number=0;if(marker>=0&&marker+6<name.Length)int.TryParse(name.Substring(marker+6),out number);
                    previous=number<=1 ? name.Substring(0,marker)+"Tibia" : name.Substring(0,marker)+"Tarsus"+(number-1);
                }
                if(previous!=null&&bodies.TryGetValue(previous,out var parent))connected=parent;
                var joint=pair.Value.gameObject.AddComponent<ConfigurableJoint>();joint.connectedBody=connected;joint.autoConfigureConnectedAnchor=true;
                joint.xMotion=joint.yMotion=joint.zMotion=ConfigurableJointMotion.Locked;
                joint.angularXMotion=joint.angularYMotion=joint.angularZMotion=ConfigurableJointMotion.Limited;
                float limit=name.Contains("Wing")?55:name.Contains("Coxa")?32:42;
                joint.lowAngularXLimit=new SoftJointLimit{limit=-limit};joint.highAngularXLimit=new SoftJointLimit{limit=limit};
                joint.angularYLimit=new SoftJointLimit{limit=limit};joint.angularZLimit=new SoftJointLimit{limit=limit};
                joint.enableCollision=false;joint.enablePreprocessing=true;
                joint.projectionMode=JointProjectionMode.PositionAndRotation;joint.projectionDistance=.012f;joint.projectionAngle=5;
            }
        }
        void OnCollisionEnter(Collision collision){RecordGroundCollision(collision);}
        void OnCollisionStay(Collision collision){RecordGroundCollision(collision);}
        void RecordGroundCollision(Collision collision)
        {
            foreach(var contact in collision.contacts)if(Vector3.Dot(contact.normal,Vector3.up)>.55f)
            {
                Collider external=contact.otherCollider;
                if(external && (external.transform==transform||external.transform.IsChildOf(transform)))external=contact.thisCollider;
                if(!external || external.transform==transform || external.transform.IsChildOf(transform) || external.attachedRigidbody ||
                    external.GetComponentInParent<CharacterController>() || external.GetComponentInParent<CombatTarget>())continue;
                HasGroundContact=true;
                if(!groundImpactDamped && physicsBody)
                {
                    groundImpactDamped=true;
                    Vector3 normal=contact.normal;
                    Vector3 away=Vector3.Project(physicsBody.velocity,normal);
                    if(Vector3.Dot(away,normal)<0)away=Vector3.zero;
                    physicsBody.velocity=Vector3.ProjectOnPlane(physicsBody.velocity,normal)*.18f+away*.15f;
                    physicsBody.angularVelocity*=.35f;
                }
                break;
            }
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
            // Ground the load-bearing thorax/abdomen, not a dangling wing or foot.
            // Begin at the core center so an overhead tabletop cannot be mistaken for
            // a floor and pull a corpse upward against its underside.
            Bounds bounds=SupportCoreBounds();
            RaycastHit ground=default(RaycastHit);float nearest=float.PositiveInfinity;bool found=false;
            foreach(var hit in Physics.RaycastAll(bounds.center+Vector3.up*.03f,Vector3.down,8,1,QueryTriggerInteraction.Ignore))
            {
                if(hit.collider && (hit.collider.transform==transform || hit.collider.transform.IsChildOf(transform)))continue;
                if(hit.collider.attachedRigidbody || hit.collider.GetComponentInParent<CharacterController>())continue;
                if(Vector3.Dot(hit.normal,Vector3.up)<.65f || hit.distance>=nearest)continue;
                ground=hit;nearest=hit.distance;found=true;
            }
            if(!found){GroundClearance=float.PositiveInfinity;return;}
            GroundClearance=Vector3.Dot(bounds.min-ground.point,ground.normal);
            // Collision callbacks are not guaranteed when a continuously detected
            // compound settles with an extremely small separation. The same static
            // ground probe used for penetration correction provides a deterministic
            // resting-contact signal.
            if(GroundClearance<=.08f && GroundClearance>=-.5f && physicsBody.velocity.y<=.5f)HasGroundContact=true;
            float penetration=Vector3.Dot(ground.point-bounds.min,ground.normal);
            if(penetration>.02f)
            {
                physicsBody.position+=ground.normal*(penetration+.025f);
                float intoGround=Vector3.Dot(physicsBody.velocity,ground.normal);
                if(intoGround<0)physicsBody.velocity-=ground.normal*intoGround;
            }
            if(HasGroundContact)
            {
                groundContactSeconds+=Time.fixedDeltaTime;
                physicsBody.drag=2.5f;physicsBody.angularDrag=4;
                // Jointed wings and distal tarsi can feed tiny impulses back into the
                // thorax forever. Dampen the complete corpse after impact, then sleep
                // it as one settled ragdoll instead of leaving it visibly trembling.
                foreach(var body in articulatedBodies)if(body)
                {
                    body.velocity*=.86f;body.angularVelocity*=.8f;
                    if(groundContactSeconds>1.1f){body.velocity=Vector3.zero;body.angularVelocity=Vector3.zero;body.Sleep();}
                }
                if(groundContactSeconds>1.1f)
                {
                    physicsBody.velocity=Vector3.zero;physicsBody.angularVelocity=Vector3.zero;physicsBody.Sleep();
                    RestingSeconds+=Time.fixedDeltaTime;
                }
                else if(physicsBody.velocity.magnitude<.35f && physicsBody.angularVelocity.magnitude<.6f)
                {
                    RestingSeconds+=Time.fixedDeltaTime;
                    physicsBody.velocity=Vector3.MoveTowards(physicsBody.velocity,Vector3.zero,2*Time.fixedDeltaTime);
                    physicsBody.angularVelocity=Vector3.MoveTowards(physicsBody.angularVelocity,Vector3.zero,3*Time.fixedDeltaTime);
                }
                else RestingSeconds=0;
            }
            else groundContactSeconds=RestingSeconds=0;
        }
        void Update(){age+=Time.deltaTime;if(age>=minimumLifetime&&!VisibleToMainCamera())Destroy(gameObject);}
    }
}
