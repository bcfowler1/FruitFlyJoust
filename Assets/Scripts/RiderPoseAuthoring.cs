using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace FruitFlyJoust
{
    [ExecuteAlways]
    public sealed class RiderPoseAuthoring : MonoBehaviour
    {
        [Serializable] public sealed class BonePose
        {
            public string bone;
            public Vector3 localPosition;
            public Quaternion localRotation;
            public Vector3 localScale;
        }
        [Serializable] public sealed class PoseFile
        {
            public string format = "FruitFlyJoust.RiderPose.v2";
            public string savedUtc;
            public Vector3 riderLocalPosition;
            public Quaternion riderLocalRotation;
            public Vector3 riderLocalScale;
            public List<BonePose> bones = new List<BonePose>();
        }

        public Transform riderRoot;
        public Animator animator;
        public string outputFile = "Research/rider-authored-pose.json";
        public HumanBodyBones[] editableBones = {
            HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest,
            HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot,
            HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot
        };

        public Transform Bone(HumanBodyBones bone)
        { return animator && animator.isHuman ? animator.GetBoneTransform(bone) : null; }

        public string AbsoluteOutputPath()
        {
            string project = Path.GetDirectoryName(Application.dataPath);
            return Path.GetFullPath(Path.Combine(project, outputFile));
        }

        public PoseFile Capture()
        {
            var pose = new PoseFile {
                savedUtc = DateTime.UtcNow.ToString("o"),
                riderLocalPosition = riderRoot ? riderRoot.localPosition : Vector3.zero,
                riderLocalRotation = riderRoot ? riderRoot.localRotation : Quaternion.identity,
                riderLocalScale = riderRoot ? riderRoot.localScale : Vector3.one
            };
            foreach (var bone in editableBones)
            {
                Transform t = Bone(bone); if (!t) continue;
                pose.bones.Add(new BonePose { bone=bone.ToString(), localPosition=t.localPosition, localRotation=t.localRotation, localScale=t.localScale });
            }
            return pose;
        }

        public string SavePose()
        {
            string path=AbsoluteOutputPath(); Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path,JsonUtility.ToJson(Capture(),true));
            return path;
        }

        public bool LoadPose()
        {
            string path=AbsoluteOutputPath(); if(!File.Exists(path)) return false;
            PoseFile pose=JsonUtility.FromJson<PoseFile>(File.ReadAllText(path)); if(pose==null) return false;
            if(riderRoot){riderRoot.localPosition=pose.riderLocalPosition;riderRoot.localRotation=pose.riderLocalRotation;if(pose.format=="FruitFlyJoust.RiderPose.v2")riderRoot.localScale=pose.riderLocalScale;}
            foreach(var saved in pose.bones)
                if(Enum.TryParse(saved.bone,out HumanBodyBones bone)) { Transform t=Bone(bone); if(t){t.localPosition=saved.localPosition;t.localRotation=saved.localRotation;if(pose.format=="FruitFlyJoust.RiderPose.v2")t.localScale=saved.localScale;} }
            return true;
        }

        void OnDrawGizmos()
        {
            Gizmos.color=new Color(.1f,.8f,1f,.8f);
            foreach(var bone in editableBones){Transform t=Bone(bone);if(t)Gizmos.DrawWireSphere(t.position,.025f);}
        }
    }
}
