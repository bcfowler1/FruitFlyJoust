using System;
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
        private Vector3 transitionPosition;
        private Quaternion transitionRotation;
        private string transitionState;
        private AuthoredPose authoredPose;
        public float LastWaistAimDegrees { get; private set; }
        public float LastTorsoStabilizationDegrees { get; private set; }
        public Quaternion StableCameraRotation { get; private set; }=Quaternion.identity;
        public float mountedSeatHeight = .2f;
        public float mountedSeatForward;
        public float visualScale = .6f;
        public Vector3 MountedLocalPosition { get { return authoredPose!=null && authoredPose.format=="FruitFlyJoust.RiderPose.v2" ? authoredPose.riderLocalPosition : new Vector3(0,mountedSeatHeight,mountedSeatForward); } }
        public Vector3 MountedLocalScale { get { return authoredPose!=null && authoredPose.format=="FruitFlyJoust.RiderPose.v2" ? authoredPose.riderLocalScale : Vector3.one*visualScale; } }
        public Transform Hand(bool left) { return animator && animator.isHuman ? animator.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand) : null; }
        public Transform Head { get { return animator && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null; } }
        public Vector3 VisualRootPosition { get { return body ? body.position : new Vector3(float.PositiveInfinity,float.PositiveInfinity,float.PositiveInfinity); } }
        public void SetClock(float simulationDelta)
        {
            if (!animator) return;
            animator.speed=1;animator.Update(Mathf.Max(0,simulationDelta));animator.speed=0;
        }
        public void CancelMountTransition() { transitionRemaining=0; }
        public void AdvanceTransition(float delta) { transitionRemaining=mountTransitions ? Mathf.Max(0,transitionRemaining-delta) : 0; }
        public void BeginMountTransition(bool mounted)
        {
            if(!mountTransitions || !animator || !body) return;
            transitionDuration=0;
            string name=mounted ? "Rider_Mount_Left" : "Rider_Dismount_Left";
            foreach(var clip in animator.runtimeAnimatorController.animationClips)
                if(clip.name==name) { transitionDuration=clip.length;break; }
            if(transitionDuration<=0) return;
            transitionRemaining=transitionDuration;transitionState=mounted ? "Mount" : "Dismount";
            transitionPosition=body.position;transitionRotation=body.rotation;
        }
        public bool Create(Transform anchor, Material material)
        {
            var prefab = Resources.Load<GameObject>("BorrowedRider"); if (!prefab) return false;
            body = Instantiate(prefab, anchor, false).transform; body.name = "Animated rider";
            body.localScale = Vector3.one * visualScale;
            animator = body.GetComponentInChildren<Animator>(); animator.applyRootMotion = false;
            animator.fireEvents=false; // Silverspur audio callbacks have no receiver in this project.
            var poseAsset=Resources.Load<TextAsset>("RiderAuthoredPose");
            if(poseAsset)authoredPose=JsonUtility.FromJson<AuthoredPose>(poseAsset.text);
            grip=animator.gameObject.AddComponent<RiderGripIK>();
            foreach (var renderer in body.GetComponentsInChildren<SkinnedMeshRenderer>())
            { var materials = renderer.sharedMaterials; for (int i = 0; i < materials.Length; i++) materials[i] = material; renderer.sharedMaterials = materials; }
            return true;
        }
        public void Pose(Transform anchor, bool mounted, float speed, bool defeated = false)
        {
            if (!body) return;
            bool useAuthored=mounted && transitionRemaining<=0 && authoredPose!=null && authoredPose.format=="FruitFlyJoust.RiderPose.v2";
            bool hasAuthoredScale=authoredPose!=null && authoredPose.format=="FruitFlyJoust.RiderPose.v2";
            body.localScale=hasAuthoredScale ? authoredPose.riderLocalScale : Vector3.one*visualScale;
            if (body.parent != anchor) body.SetParent(anchor, false);
            body.localPosition = useAuthored ? authoredPose.riderLocalPosition : mounted ? new Vector3(0, mountedSeatHeight, mountedSeatForward) : Vector3.zero;
            body.localRotation = useAuthored ? authoredPose.riderLocalRotation : Quaternion.identity; body.gameObject.SetActive(!defeated);
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
        }
        public void Attack(string state) { if (animator) animator.CrossFade(state, .06f, 1, 0); }
        public void AimUpperBody(Transform frame,Vector3 worldDirection,float weight)
        {
            LastWaistAimDegrees=0;
            if(!animator || !animator.isHuman || !frame || weight<=0)return;
            Vector3 planar=Vector3.ProjectOnPlane(worldDirection,frame.up);
            if(planar.sqrMagnitude<.001f)return;
            float yaw=Mathf.Clamp(Vector3.SignedAngle(frame.forward,planar.normalized,frame.up),-105,105);
            LastWaistAimDegrees=yaw;
            Twist(HumanBodyBones.Spine,frame.up,yaw*.22f*weight);
            Twist(HumanBodyBones.Chest,frame.up,yaw*.33f*weight);
            Twist(HumanBodyBones.UpperChest,frame.up,yaw*.45f*weight);
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
