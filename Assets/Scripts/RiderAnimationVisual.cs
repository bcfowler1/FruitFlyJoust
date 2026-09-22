using System;
using System.Collections.Generic;
using UnityEngine;

namespace FruitFlyJoust
{
    public sealed class RiderAnimationVisual : MonoBehaviour
    {
        [Serializable] sealed class AuthoredBonePose
        {
            public string bone;
            public Vector3 localPosition;
            public Quaternion localRotation;
            public Vector3 localScale;
        }
        [Serializable] sealed class AuthoredPose
        {
            public string format;
            public Vector3 riderLocalPosition;
            public Quaternion riderLocalRotation;
            public Vector3 riderLocalScale;
            public AuthoredBonePose[] bones;
        }
        private Animator animator;
        private Transform body;
        private string current;
        private RiderGripIK grip;
        public bool legGrip = true;
        public bool mountTransitions;
        private float transitionRemaining,transitionDuration;
        private float unmountedVerticalOffset;
        private Vector3 transitionPosition;
        private Quaternion transitionRotation;
        private string transitionState;
        private float continuityGrace;
        private Vector3 continuityReference;
        private AuthoredPose authoredPose;
        private readonly List<Rigidbody> ragdollBodies=new List<Rigidbody>();
        private readonly List<Collider> ragdollColliders=new List<Collider>();
        private readonly List<CharacterJoint> ragdollJoints=new List<CharacterJoint>();
        public bool Ragdolled { get; private set; }
        public bool Transitioning { get { return transitionRemaining>0; } }
        public int RagdollBodyCount { get { return ragdollBodies.Count; } }
        public Vector3 RagdollCenter { get { var hips=Bone(HumanBodyBones.Hips);return hips ? hips.position : VisualRootPosition; } }
        public float LastWaistAimDegrees { get; private set; }
        public float LeftArmAimAlignment { get; private set; }
        public float BowDrawHandDistance { get; private set; }
        public float LastTorsoStabilizationDegrees { get; private set; }
        public Quaternion StableCameraRotation { get; private set; }=Quaternion.identity;
        public float mountedSeatHeight = .2f;
        public float mountedSeatForward;
        public float visualScale = RiderCombat.CanonicalRiderVisualScale;
        public Vector3 MountedLocalPosition { get { return (authoredPose!=null && authoredPose.format=="FruitFlyJoust.RiderPose.v2" ? authoredPose.riderLocalPosition : new Vector3(0,mountedSeatHeight,mountedSeatForward))*RiderCombat.FlyAssemblyScale; } }
        public Vector3 MountedLocalScale { get { return CanonicalLocalScale(body && body.parent ? body.parent : null); } }
        public Transform Hand(bool left) { return animator && animator.isHuman ? animator.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand) : null; }
        public Transform Head { get { return animator && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null; } }
        public Vector3 VisualRootPosition { get { return body ? body.position : new Vector3(float.PositiveInfinity,float.PositiveInfinity,float.PositiveInfinity); } }
        public Vector3 VisualWorldScale { get { return body ? body.lossyScale : Vector3.zero; } }
        public Vector3 VisualLocalScale { get { return body ? body.localScale : Vector3.zero; } }
        Vector3 CanonicalLocalScale(Transform anchor)
        {
            Vector3 parentScale=anchor ? anchor.lossyScale : Vector3.one;
            return new Vector3(visualScale/Mathf.Max(.0001f,Mathf.Abs(parentScale.x)),
                visualScale/Mathf.Max(.0001f,Mathf.Abs(parentScale.y)),
                visualScale/Mathf.Max(.0001f,Mathf.Abs(parentScale.z)));
        }
        public void SetClock(float simulationDelta)
        {
            if (!animator) return;
            animator.speed=1;animator.Update(Mathf.Max(0,simulationDelta));animator.speed=0;
        }
        public void CancelMountTransition() { transitionRemaining=0; }
        public void AdvanceTransition(float delta) { transitionRemaining=mountTransitions ? Mathf.Max(0,transitionRemaining-delta) : 0;continuityGrace=Mathf.Max(0,continuityGrace-delta); }
        public void BeginMountTransition(bool mounted)
        {
            if(!mountTransitions || !animator || !body) return;
            continuityReference=RagdollCenter;
            transitionDuration=0;
            string name=mounted ? "Rider_Mount_Left" : "Rider_Dismount_Left";
            foreach(var clip in animator.runtimeAnimatorController.animationClips)
                if(clip.name==name) { transitionDuration=clip.length;break; }
            continuityGrace=Mathf.Max(.9f,transitionDuration+.25f);
            if(transitionDuration<=0) return;
            transitionRemaining=transitionDuration;transitionState=mounted ? "Mount" : "Dismount";
            transitionPosition=body.position;transitionRotation=body.rotation;
        }
        public bool Create(Transform anchor, Material material)
        {
            var prefab = Resources.Load<GameObject>("BorrowedRider"); if (!prefab) return false;
            body = Instantiate(prefab, anchor, false).transform; body.name = "Animated rider";
            body.localScale = CanonicalLocalScale(anchor);
            animator = body.GetComponentInChildren<Animator>(); animator.applyRootMotion = false;
            animator.fireEvents=false; // Silverspur audio callbacks have no receiver in this project.
            var poseAsset=Resources.Load<TextAsset>("RiderAuthoredPose");
            if(poseAsset)authoredPose=JsonUtility.FromJson<AuthoredPose>(poseAsset.text);
            grip=animator.gameObject.AddComponent<RiderGripIK>();
            foreach (var renderer in body.GetComponentsInChildren<SkinnedMeshRenderer>())
            { var materials = renderer.sharedMaterials; for (int i = 0; i < materials.Length; i++) materials[i] = material; renderer.sharedMaterials = materials; }
            return true;
        }
        public bool RecreateFromPrefab(Transform anchor,Material material)
        {
            // Respawns never reuse a hierarchy that has been parented through a
            // saddle, detached as a ragdoll, or rescaled by physics recovery.
            if(body){body.gameObject.SetActive(false);Destroy(body.gameObject);}
            body=null;animator=null;grip=null;current=null;Ragdolled=false;
            transitionRemaining=transitionDuration=0;unmountedVerticalOffset=0;
            ragdollBodies.Clear();ragdollColliders.Clear();ragdollJoints.Clear();
            return Create(anchor,material);
        }
        public void Pose(Transform anchor, bool mounted, float speed, bool defeated = false)
        {
            if (!body) return;
            if(Ragdolled)return;
            bool useAuthored=mounted && transitionRemaining<=0 && authoredPose!=null && authoredPose.format=="FruitFlyJoust.RiderPose.v2";
            if (body.parent != anchor) body.SetParent(anchor, false);
            body.localScale=CanonicalLocalScale(anchor);
            body.localPosition = mounted ? (useAuthored ? authoredPose.riderLocalPosition : new Vector3(0,mountedSeatHeight,mountedSeatForward))*RiderCombat.FlyAssemblyScale : Vector3.up*unmountedVerticalOffset;
            body.localRotation = useAuthored ? authoredPose.riderLocalRotation : Quaternion.identity; body.gameObject.SetActive(true);
            if(transitionRemaining>0)
            {
                float progress=1-transitionRemaining/transitionDuration;
                Vector3 destination=body.position;Quaternion rotation=body.rotation;
                body.position=Vector3.Lerp(transitionPosition,destination,progress)+Vector3.up*(.1f*Mathf.Sin(progress*Mathf.PI));
                body.rotation=Quaternion.Slerp(transitionRotation,rotation,progress);
            }
            grip.anchor=anchor;grip.mounted=mounted && legGrip && transitionRemaining<=0;
            string state = transitionRemaining>0 ? transitionState : mounted ? "Ride" : speed > .1f ? "Walk" : "Idle";
            if (current != state) { animator.CrossFade(state, .12f, 0); current = state; }
            if(useAuthored && animator.isHuman && authoredPose.bones!=null)
                foreach(var saved in authoredPose.bones)
                    if(Enum.TryParse(saved.bone,out HumanBodyBones bone))
                    {
                        var target=animator.GetBoneTransform(bone);if(!target)continue;
                        target.localPosition=saved.localPosition;target.localRotation=saved.localRotation;target.localScale=saved.localScale;
                    }
            ConstrainTransitionStep();
        }
        void ConstrainTransitionStep()
        {
            if(continuityGrace<=0 || !body)return;
            Vector3 center=RagdollCenter,delta=center-continuityReference;const float maximumStep=.26f;
            if(delta.magnitude>maximumStep)
            {
                body.position-=delta.normalized*(delta.magnitude-maximumStep);
                continuityGrace=Mathf.Max(continuityGrace,.12f);
            }
            continuityReference=RagdollCenter;
        }
        Transform Bone(HumanBodyBones bone) { return animator && animator.isHuman ? animator.GetBoneTransform(bone) : null; }
        public void EnterRagdoll(Vector3 inheritedVelocity)
        {
            if(Ragdolled || !body || !animator || !animator.isHuman)return;
            Ragdolled=true;transitionRemaining=0;body.gameObject.SetActive(true);body.SetParent(null,true);
            // Detachment must not preserve an accidental tack/arena scale. Every
            // player and enemy humanoid is authored at the canonical world scale.
            body.localScale=Vector3.one*visualScale;
            if(grip)grip.enabled=false;
            HumanBodyBones[] bones={HumanBodyBones.Hips,HumanBodyBones.Spine,HumanBodyBones.Chest,HumanBodyBones.Head,
                HumanBodyBones.LeftUpperArm,HumanBodyBones.LeftLowerArm,HumanBodyBones.RightUpperArm,HumanBodyBones.RightLowerArm,
                HumanBodyBones.LeftUpperLeg,HumanBodyBones.LeftLowerLeg,HumanBodyBones.RightUpperLeg,HumanBodyBones.RightLowerLeg};
            var map=new Dictionary<Transform,Rigidbody>();
            foreach(var id in bones)
            {
                Transform bone=Bone(id);if(!bone || map.ContainsKey(bone))continue;
                // A recovered rider can be unseated again before Unity has applied
                // deferred component destruction. Reuse that bone's physics body
                // instead of requesting a second Rigidbody (which Unity rejects and
                // returns null while a CharacterJoint still depends on the first).
                var rigid=bone.GetComponent<Rigidbody>();
                if(!rigid)rigid=bone.gameObject.AddComponent<Rigidbody>();
                if(!rigid)continue;
                rigid.isKinematic=false;rigid.detectCollisions=true;
                rigid.mass=id==HumanBodyBones.Hips ? 4 : id==HumanBodyBones.Spine || id==HumanBodyBones.Chest ? 2 : .65f;
                rigid.velocity=inheritedVelocity*(id==HumanBodyBones.Hips ? .85f : .7f);
                rigid.angularVelocity=Vector3.Cross(Vector3.up,inheritedVelocity)*.08f;rigid.angularDrag=1.5f;rigid.maxAngularVelocity=4;
                rigid.interpolation=RigidbodyInterpolation.Interpolate;rigid.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;
                Collider collider;
                if(id==HumanBodyBones.Head){var sphere=bone.GetComponent<SphereCollider>();if(!sphere)sphere=bone.gameObject.AddComponent<SphereCollider>();sphere.enabled=true;sphere.radius=.12f;collider=sphere;}
                else {var capsule=bone.GetComponent<CapsuleCollider>();if(!capsule)capsule=bone.gameObject.AddComponent<CapsuleCollider>();capsule.enabled=true;capsule.direction=1;capsule.radius=id==HumanBodyBones.Hips || id==HumanBodyBones.Spine || id==HumanBodyBones.Chest ? .1f : .055f;capsule.height=capsule.radius*3.2f;collider=capsule;}
                map.Add(bone,rigid);ragdollBodies.Add(rigid);ragdollColliders.Add(collider);
            }
            // GetBoneTransform requires the humanoid animator to remain live while
            // the ragdoll map is assembled. Disable animation only after caching it.
            animator.enabled=false;
            foreach(var pair in map)
            {
                Transform parent=pair.Key.parent;Rigidbody connected=null;
                while(parent && !map.TryGetValue(parent,out connected))parent=parent.parent;
                if(!connected)continue;
                var joint=pair.Key.GetComponent<CharacterJoint>();if(!joint)joint=pair.Key.gameObject.AddComponent<CharacterJoint>();if(!joint)continue;joint.connectedBody=connected;joint.enableProjection=true;
                joint.lowTwistLimit=new SoftJointLimit{limit=-35};joint.highTwistLimit=new SoftJointLimit{limit=35};joint.swing1Limit=new SoftJointLimit{limit=50};joint.swing2Limit=new SoftJointLimit{limit=50};ragdollJoints.Add(joint);
            }
        }
        public void ExitRagdoll()
        {
            if(!Ragdolled)return;
            Transform hips=Bone(HumanBodyBones.Hips);
            Vector3 preservedHips=hips ? hips.position : body.position;
            // Keep the ragdoll components for reuse. Unity does not guarantee that
            // deferred CharacterJoint destruction completes before a dependent
            // Rigidbody removal, which produced one error per articulated bone.
            // Kinematic bodies with disabled colliders are inert under animation and
            // can be safely reactivated by the next EnterRagdoll call.
            foreach(var joint in ragdollJoints)if(joint)joint.connectedBody=null;
            foreach(var collider in ragdollColliders)if(collider)collider.enabled=false;
            foreach(var rigid in ragdollBodies)if(rigid){rigid.velocity=Vector3.zero;rigid.angularVelocity=Vector3.zero;rigid.isKinematic=true;rigid.detectCollisions=false;}
            ragdollBodies.Clear();ragdollJoints.Clear();ragdollColliders.Clear();Ragdolled=false;
            animator.enabled=true;animator.Rebind();animator.Update(0);
            // Rebind restores the animated skeleton around its old visual root while
            // the physics hips may have landed elsewhere. Shift the visual root so
            // the first recovered animation frame begins at the last rendered hips.
            hips=Bone(HumanBodyBones.Hips);
            if(hips)body.position+=preservedHips-hips.position;
            continuityReference=hips ? hips.position : body.position;continuityGrace=.9f;
            if(grip)grip.enabled=true;current=null;
        }
        public void KeepRagdollAboveGround()
        {
            if(!Ragdolled || !body)return;
            // Also repairs ragdolls created before a tack-scale change or script
            // reload, so existing fallen riders converge to the canonical size.
            body.localScale=Vector3.one*visualScale;
            var renderers=body.GetComponentsInChildren<Renderer>();if(renderers.Length==0)return;
            Bounds bounds=renderers[0].bounds;foreach(var renderer in renderers)bounds.Encapsulate(renderer.bounds);
            if(FlyMotor.TryArenaCeiling(out var innerCeiling) && bounds.max.y>innerCeiling)
            {
                body.position+=Vector3.down*(bounds.max.y-innerCeiling+.02f);
                foreach(var rigid in ragdollBodies)if(rigid && rigid.velocity.y>0)
                    rigid.velocity=new Vector3(rigid.velocity.x,Mathf.Min(-.5f,rigid.velocity.y),rigid.velocity.z);
                bounds=renderers[0].bounds;foreach(var renderer in renderers)bounds.Encapsulate(renderer.bounds);
            }
            RaycastHit ground=default(RaycastHit);float nearest=float.PositiveInfinity;bool found=false;
            // Start at the body center. Starting above it can cross a ceiling and
            // misclassify the roof's top face as ground below the ragdoll.
            Vector3 origin=bounds.center+Vector3.up*.03f;
            foreach(var hit in Physics.RaycastAll(origin,Vector3.down,8,1,QueryTriggerInteraction.Ignore))
            {
                if(!hit.collider || hit.distance>=nearest || Vector3.Dot(hit.normal,Vector3.up)<.65f ||
                   hit.collider.attachedRigidbody || hit.collider.GetComponentInParent<CharacterController>() ||
                   hit.collider.GetComponentInParent<CombatTarget>())continue;
                Transform candidate=hit.collider.transform;if(candidate==body || candidate.IsChildOf(body))continue;
                nearest=hit.distance;ground=hit;found=true;
            }
            if(!found)return;float correction=ground.point.y+.015f-bounds.min.y;
            if(correction<=0 || correction>2)return;
            body.position+=Vector3.up*correction;
            foreach(var rigid in ragdollBodies)if(rigid && rigid.velocity.y<0)
                rigid.velocity=new Vector3(rigid.velocity.x,0,rigid.velocity.z);
        }
        public void Attack(string state) { if (animator) animator.CrossFade(state, .06f, 1, 0); }
        public void AimUpperBody(Transform frame,Vector3 worldDirection,float weight)
        {
            LastWaistAimDegrees=0;
            if(!animator || !animator.isHuman || !frame || weight<=0)return;
            Vector3 planar=Vector3.ProjectOnPlane(worldDirection,frame.up);
            if(planar.sqrMagnitude<.001f)return;
            float aimYaw=Vector3.SignedAngle(frame.forward,planar.normalized,frame.up);
            // Turn partially side-on to the shot. Sixty degrees leaves enough reach for
            // the draw arm to cross the chest while the bow arm and projectile stay on
            // the world-space firing line established below.
            float yaw=Mathf.Clamp(Mathf.DeltaAngle(0,aimYaw+60),-105,105);
            LastWaistAimDegrees=yaw*weight;
            Twist(HumanBodyBones.Spine,frame.up,yaw*.22f*weight);
            Twist(HumanBodyBones.Chest,frame.up,yaw*.33f*weight);
            Twist(HumanBodyBones.UpperChest,frame.up,yaw*.45f*weight);
        }
        public void PoseBowAim(Transform frame,Vector3 worldDirection,float weight,float draw)
        {
            LeftArmAimAlignment=BowDrawHandDistance=0;
            if(!animator || !animator.isHuman || !frame || weight<=0)return;
            Vector3 aim=worldDirection.normalized;
            var upper=Bone(HumanBodyBones.LeftUpperArm);var lower=Bone(HumanBodyBones.LeftLowerArm);var hand=Bone(HumanBodyBones.LeftHand);
            if(!upper || !lower || !hand)return;
            Vector3 current=hand.position-upper.position;
            if(current.sqrMagnitude>.0001f)
                upper.rotation=Quaternion.Slerp(upper.rotation,Quaternion.FromToRotation(current.normalized,aim)*upper.rotation,weight);
            Vector3 target=upper.position+aim*(Vector3.Distance(upper.position,lower.position)+Vector3.Distance(lower.position,hand.position));
            Vector3 fore=hand.position-lower.position,wanted=target-lower.position;
            if(fore.sqrMagnitude>.0001f && wanted.sqrMagnitude>.0001f)
                lower.rotation=Quaternion.Slerp(lower.rotation,Quaternion.FromToRotation(fore.normalized,wanted.normalized)*lower.rotation,weight);
            Vector3 reach=hand.position-upper.position;if(reach.sqrMagnitude>.0001f)LeftArmAimAlignment=Vector3.Dot(reach.normalized,aim);
            var rightUpper=Bone(HumanBodyBones.RightUpperArm);var rightLower=Bone(HumanBodyBones.RightLowerArm);var rightHand=Bone(HumanBodyBones.RightHand);
            if(!rightUpper || !rightLower || !rightHand)return;
            Vector3 drawTarget=hand.position-aim*Mathf.Lerp(.34f,.74f,Mathf.Clamp01(draw))+frame.up*.035f;
            Vector3 rightReach=rightHand.position-rightUpper.position,wantedReach=drawTarget-rightUpper.position;
            if(rightReach.sqrMagnitude>.0001f && wantedReach.sqrMagnitude>.0001f)
                rightUpper.rotation=Quaternion.Slerp(rightUpper.rotation,Quaternion.FromToRotation(rightReach.normalized,wantedReach.normalized)*rightUpper.rotation,weight);
            Vector3 rightFore=rightHand.position-rightLower.position,wantedFore=drawTarget-rightLower.position;
            if(rightFore.sqrMagnitude>.0001f && wantedFore.sqrMagnitude>.0001f)
                rightLower.rotation=Quaternion.Slerp(rightLower.rotation,Quaternion.FromToRotation(rightFore.normalized,wantedFore.normalized)*rightLower.rotation,weight);
            BowDrawHandDistance=Mathf.Max(0,Vector3.Dot(hand.position-rightHand.position,aim));
        }
        public float VisualHeight
        {
            get
            {
                if(!body)return 0;var renderers=body.GetComponentsInChildren<Renderer>();if(renderers.Length==0)return 0;
                Bounds bounds=renderers[0].bounds;foreach(var renderer in renderers)bounds.Encapsulate(renderer.bounds);return bounds.size.y;
            }
        }
        public float AlignFeetToWorldY(float worldY)
        {
            if(!body)return float.PositiveInfinity;var renderers=body.GetComponentsInChildren<Renderer>();if(renderers.Length==0)return float.PositiveInfinity;
            Bounds bounds=renderers[0].bounds;foreach(var renderer in renderers)bounds.Encapsulate(renderer.bounds);
            float correction=worldY-bounds.min.y;unmountedVerticalOffset+=correction;body.position+=Vector3.up*correction;
            return Mathf.Abs((bounds.min.y+correction)-worldY);
        }
        public void StabilizeTorsoAgainstGravity(Transform frame,float amount=.6f,float maximumDegrees=55)
        {
            LastTorsoStabilizationDegrees=0;
            if(!frame)return;
            StableCameraRotation=frame.rotation;
            if(!animator || !animator.isHuman || amount<=0 || Physics.gravity.sqrMagnitude<.001f)return;
            Vector3 gravityUp=-Physics.gravity.normalized;
            Quaternion full=Quaternion.FromToRotation(frame.up,gravityUp);
            full.ToAngleAxis(out float angle,out Vector3 axis);
            if(angle>180)angle-=360;
            float correction=Mathf.Clamp(angle*amount,-maximumDegrees,maximumDegrees);
            LastTorsoStabilizationDegrees=Mathf.Abs(correction);
            // Camera follows this deterministic gravity-corrected frame. It matches the
            // total torso correction without inheriting looped animation/head-bone jitter.
            StableCameraRotation=Quaternion.AngleAxis(correction,axis)*frame.rotation;
            Twist(HumanBodyBones.Spine,axis,correction*.3f);
            Twist(HumanBodyBones.Chest,axis,correction*.4f);
            Twist(HumanBodyBones.UpperChest,axis,correction*.3f);
        }
        void Twist(HumanBodyBones bone,Vector3 axis,float degrees)
        {
            var target=animator.GetBoneTransform(bone);if(target)target.rotation=Quaternion.AngleAxis(degrees,axis)*target.rotation;
        }
        public void PoseSwordAttack(RiderCombat.SwordAttack style,float remaining)
        {
            if(!animator || !animator.isHuman)return;
            float phase=1-Mathf.Clamp01(remaining);
            float transition=Mathf.SmoothStep(0,1,Mathf.Clamp01(phase/.18f))*
                (1-Mathf.SmoothStep(0,1,Mathf.Clamp01((phase-.82f)/.18f)));
            float stroke=Mathf.Sin(phase*Mathf.PI)*transition;
            float side=style==RiderCombat.SwordAttack.LeftToRight ? 1 : style==RiderCombat.SwordAttack.RightToLeft ? -1 : 0;
            float sweep=(phase<.28f ? Mathf.Lerp(0,-side,Mathf.SmoothStep(0,1,phase/.28f)) :
                Mathf.Lerp(-side,side,Mathf.SmoothStep(0,1,(phase-.28f)/.72f)))*transition;
            if(style==RiderCombat.SwordAttack.Thrust)
            {
                // Advance the complete rider and extend the shoulder/elbow so the hand
                // remains attached to the hilt throughout the thrust.
                body.position+=body.forward*(.24f*stroke);
                Twist(HumanBodyBones.Spine,body.right,-12*stroke);
                Twist(HumanBodyBones.Chest,body.right,-18*stroke);
                Twist(HumanBodyBones.UpperChest,body.right,-13*stroke);
            }
            else
            {
                Twist(HumanBodyBones.Spine,body.up,sweep*18);
                Twist(HumanBodyBones.Chest,body.up,sweep*28);
            }
            var arm=animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            var forearm=animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            var hand=animator.GetBoneTransform(HumanBodyBones.RightHand);
            if(arm)
            {
                if(style==RiderCombat.SwordAttack.Thrust)
                {
                    arm.rotation=Quaternion.AngleAxis(-42*stroke,body.right)*arm.rotation;
                    if(forearm)forearm.rotation=Quaternion.AngleAxis(52*stroke,body.right)*forearm.rotation;
                    if(hand)hand.rotation=Quaternion.AngleAxis(-18*stroke,body.right)*hand.rotation;
                }
                else
                {
                    arm.rotation=Quaternion.AngleAxis(sweep*72,body.up)*Quaternion.AngleAxis(-38*transition,body.right)*arm.rotation;
                    if(forearm)forearm.rotation=Quaternion.AngleAxis(sweep*38,body.up)*forearm.rotation;
                    if(hand)hand.rotation=Quaternion.AngleAxis(-sweep*22,body.forward)*hand.rotation;
                }
            }
        }
    }
}
