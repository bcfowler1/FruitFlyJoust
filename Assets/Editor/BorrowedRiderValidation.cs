using System;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class BorrowedRiderValidation
{
    [MenuItem("Fruit Fly/Validate Borrowed Rider Animations")]
    public static void Validate()
    {
        var prefab = Resources.Load<GameObject>("BorrowedRider");
        var actor = UnityEngine.Object.Instantiate(prefab);
        var baseline = UnityEngine.Object.Instantiate(prefab);
        try
        {
            var animator = actor.GetComponent<Animator>(); var reference = baseline.GetComponent<Animator>();
            animator.fireEvents=reference.fireEvents=false;
            if (!animator.isHuman || !animator.avatar.isValid || animator.applyRootMotion) throw new Exception("Invalid humanoid/root-motion configuration");
            animator.cullingMode = reference.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.Rebind(); reference.Rebind(); animator.Play("Ride",0,0); reference.Play("Ride",0,0);
            animator.Update(.1f); reference.Update(.1f);
            var arm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            Quaternion before = arm.localRotation;
            animator.Play("Sword",1,0); animator.Update(.2f); reference.Update(.2f);
            float swordMovement = Quaternion.Angle(before,arm.localRotation);
            float legDifference = Quaternion.Angle(animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg).localRotation,
                reference.GetBoneTransform(HumanBodyBones.LeftUpperLeg).localRotation);
            if (swordMovement < 1 || legDifference > .1f) throw new Exception("Sword animation missing or mounted leg mask failed: " + swordMovement + "/" + legDifference);
            before = arm.localRotation; animator.Play("Bow",1,0); animator.Update(.3f);
            float bowMovement = Quaternion.Angle(before,arm.localRotation);
            if (bowMovement < 1) throw new Exception("Bow animation did not move humanoid arm");
            animator.Play("Ready",1,0); animator.Play("Walk",0,0); animator.Update(.1f);
            before = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg).localRotation;
            animator.Update(.3f); float walkMovement = Quaternion.Angle(before,animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg).localRotation);
            if (walkMovement < 1 || actor.transform.position.sqrMagnitude > .0001f) throw new Exception("Walk retarget/root motion check failed");
            int transitions=0;
            foreach(string state in new[] {"Mount","Dismount"})
            {
                animator.Play("Ready",1,0);animator.Play(state,0,0);animator.Update(.05f);
                var leg=animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);before=leg.localRotation;
                animator.Update(.3f);
                if(Quaternion.Angle(before,leg.localRotation)<1 || actor.transform.position.sqrMagnitude>.0001f)
                    throw new Exception("Mount transition retarget/root motion check failed: "+state);
                transitions++;
            }
            File.WriteAllText(Path.Combine(Application.dataPath,"../Research/rider-animation-evaluation.json"),
                "{\"status\":\"passed\",\"humanoid_retarget\":true,\"root_motion_disabled\":true,\"sword_arm_degrees\":" + swordMovement.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                ",\"bow_arm_degrees\":" + bowMovement.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                ",\"walk_leg_degrees\":" + walkMovement.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                ",\"mounted_leg_difference_degrees\":" + legDifference.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"mount_transition_clips\":"+transitions+",\"limits\":\"Pose sampling; IK requires separate Play-mode checks. Mount transitions are an artistic horse-to-fly transfer\"}");
            Debug.Log("RIDER_ANIMATIONS_PASSED: humanoid sword/bow/walk motion, mounted leg preservation and stationary root");
        }
        finally { UnityEngine.Object.DestroyImmediate(actor); UnityEngine.Object.DestroyImmediate(baseline); }
    }
}
