#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using UnityEngine;
namespace FruitFlyJoust
{
    public sealed class RiderPoseCheck : MonoBehaviour
    {
        GameObject actor,anchor,footAnchor;int checks,transitionChecks;
        void TransitionRequire(bool ok,string label)
        {
            if(!ok) { Write("failed: "+label);UnityEditor.EditorApplication.isPlaying=false;throw new Exception(label); }
            transitionChecks++;Debug.Log("RIDER_TRANSITION_CHECK: "+label);
        }
        IEnumerator Start()
        {
            anchor=new GameObject("Pose check thorax anchor");anchor.transform.position=Vector3.one*30;
            actor=Instantiate(Resources.Load<GameObject>("BorrowedRider"),anchor.transform);
            actor.transform.localPosition=new Vector3(0,-.33f,-.16f);actor.transform.localScale=Vector3.one*.78f;
            var animator=actor.GetComponent<Animator>();animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            animator.fireEvents=false;
            var grip=actor.AddComponent<RiderGripIK>();grip.anchor=anchor.transform;
            foreach(float angle in new[] {0f,90f,180f})
            {
                anchor.transform.rotation=Quaternion.Euler(0,0,angle);grip.mounted=false;
                animator.Play("Ready",1,0);animator.Play("Ride",0,0);animator.Update(.1f);animator.speed=0;
                yield return null;yield return null;
                var foot=animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                Vector3 goal=anchor.transform.TransformPoint(new Vector3(-.38f,-.15f,-.02f));
                float before=Vector3.Distance(foot.position,goal);
                grip.mounted=true;yield return null;yield return null;
                float after=Vector3.Distance(foot.position,goal);
                if(float.IsNaN(after) || after>=before*.8f)
                { Write("failed: "+angle+" degrees "+before+"/"+after);UnityEditor.EditorApplication.isPlaying=false;throw new Exception("Runtime thorax IK contact did not improve"); }
                checks++;Debug.Log("RIDER_POSE_CHECK: thorax IK improves foot contact at "+angle+" degrees: "+before+"/"+after);
            }
            var material=actor.GetComponentInChildren<SkinnedMeshRenderer>().sharedMaterial;
            Destroy(actor);yield return null;
            anchor.transform.rotation=Quaternion.identity;
            var visual=anchor.AddComponent<RiderAnimationVisual>();visual.visualScale=RiderCombat.CanonicalRiderVisualScale;
            visual.mountedSeatHeight=-.33f;visual.mountedSeatForward=-.16f;visual.mountTransitions=true;
            TransitionRequire(visual.Create(anchor.transform,material),"transition visual created");
            visual.Pose(anchor.transform,true,0);visual.SetClock(.1f);
            var rendered=anchor.GetComponentInChildren<Animator>();
            TransitionRequire((rendered.transform.localScale-visual.MountedLocalScale).sqrMagnitude<1e-8f,"authored mounted scale applied exactly");
            footAnchor=new GameObject("Transition check foot anchor");footAnchor.transform.position=anchor.transform.position+Vector3.left*2;
            foreach(bool mounted in new[] {false,true})
            {
                Vector3 start=rendered.transform.position;
                visual.BeginMountTransition(mounted);
                Transform destination=mounted ? anchor.transform : footAnchor.transform;
                visual.Pose(destination,mounted,0);visual.SetClock(0);
                TransitionRequire((rendered.transform.position-start).sqrMagnitude<1e-8f,"transition preserves starting visual position");
                string clipName=mounted ? "Rider_Mount_Left" : "Rider_Dismount_Left";float duration=0;
                foreach(var clip in rendered.runtimeAnimatorController.animationClips)if(clip.name==clipName)duration=clip.length;
                visual.AdvanceTransition(duration*.5f);visual.Pose(destination,mounted,0);visual.SetClock(duration*.5f);
                Vector3 midpoint=rendered.transform.position;
                TransitionRequire(duration>0 && (midpoint-start).magnitude>.5f && !float.IsNaN(midpoint.x),"transition advances continuously on simulation clock");
                visual.AdvanceTransition(0);visual.Pose(destination,mounted,0);visual.SetClock(0);
                TransitionRequire((rendered.transform.position-midpoint).sqrMagnitude<1e-8f,"transition holds when simulation clock is paused");
                visual.AdvanceTransition(duration);visual.Pose(destination,mounted,0);visual.SetClock(duration*.5f);
                Vector3 goal=destination.TransformPoint(mounted ? visual.MountedLocalPosition : Vector3.zero);
                Vector3 endpointDelta=rendered.transform.position-goal;
                TransitionRequire(rendered.transform.parent==destination && endpointDelta.sqrMagnitude<1e-8f,"transition ends at intended anchor (delta "+endpointDelta+")");
                TransitionRequire((rendered.transform.localScale-visual.MountedLocalScale).sqrMagnitude<1e-8f,
                    "authored rider scale remains identical while "+(mounted ? "mounted" : "unmounted"));
            }
            Write("passed");UnityEditor.EditorApplication.isPlaying=false;
        }
        void OnDestroy() { if(anchor)Destroy(anchor);else if(actor)Destroy(actor);if(footAnchor)Destroy(footAnchor); }
        void Write(string status) { File.WriteAllText(Path.Combine(Application.dataPath,"../Research/rider-pose-evaluation.json"),"{\"status\":\""+status+"\",\"orientations\":"+checks+",\"transition_checks\":"+transitionChecks+",\"limits\":\"Artistic foot IK and visual horse-to-fly transitions; not anatomical or physical limb handover validation\"}"); }
    }
}
#endif
