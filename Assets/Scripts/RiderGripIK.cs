using UnityEngine;

namespace FruitFlyJoust
{
    // Artistic leg placement on the thorax; does not change rider inertia or simulate muscles.
    public sealed class RiderGripIK : MonoBehaviour
    {
        public Transform anchor;
        public bool mounted;
        public float weight = .7f;
        private Animator animator;
        void Awake() { animator=GetComponent<Animator>(); }
        void OnAnimatorIK(int layer)
        {
            if (!animator) animator=GetComponent<Animator>();
            if (!animator || !animator.isHuman || !anchor) return;
            float grip=mounted ? weight : 0;
            Place(AvatarIKGoal.LeftFoot,AvatarIKHint.LeftKnee,-1,grip);
            Place(AvatarIKGoal.RightFoot,AvatarIKHint.RightKnee,1,grip);
        }
        void Place(AvatarIKGoal foot,AvatarIKHint knee,float side,float grip)
        {
            animator.SetIKPositionWeight(foot,grip);
            animator.SetIKRotationWeight(foot,grip*.5f);
            animator.SetIKHintPositionWeight(knee,grip);
            animator.SetIKPosition(foot,anchor.TransformPoint(new Vector3(side*.43f,-.13f,-.02f)));
            animator.SetIKRotation(foot,anchor.rotation);
            animator.SetIKHintPosition(knee,anchor.TransformPoint(new Vector3(side*.62f,.16f,.08f)));
        }
    }
}
