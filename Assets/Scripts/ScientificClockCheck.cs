#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using UnityEngine;
namespace FruitFlyJoust
{
    public sealed class ScientificClockCheck : MonoBehaviour
    {
        ResearchViewer body; RiderCombat combat; int checks; float deadline;
        void Require(bool ok,string label)
        {
            if (!ok) { Write("failed: "+label);deadline=0;UnityEditor.EditorApplication.isPlaying=false;throw new Exception(label); }
            checks++;Debug.Log("SCIENTIFIC_CLOCK_CHECK: "+label);
        }
        void Update() { if(deadline>0 && Time.realtimeSinceStartup>deadline) { Write("failed: timeout");deadline=0;UnityEditor.EditorApplication.isPlaying=false; } }
        IEnumerator Start()
        {
            deadline=Time.realtimeSinceStartup+90;body=FindObjectOfType<ResearchViewer>();
            while(!body.Connected || !body.Combat || !body.Combat.FootAvatar)yield return null;
            combat=body.Combat;combat.SetPractice(true,true);yield return null;
            Require(FindObjectsOfType<CombatOpponent>().Length==4,"enemy toggle creates four opponents");
            combat.SetPractice(true,false);yield return null;
            Require(FindObjectsOfType<CombatOpponent>().Length==0,"enemy toggle removes opponents");
            combat.SetPractice(false,false);yield return null;
            Require(FindObjectsOfType<CombatTarget>().Length==0,"practice toggle removes all targets");
            var obj=new GameObject("Paused clock test arrow");obj.transform.position=body.riderAnchor.position+Vector3.up*8;
            var arrow=obj.AddComponent<CombatArrow>();arrow.clock=combat;arrow.velocity=Vector3.forward*10;
            body.RequestPause(true);while(!body.CurrentFrame.paused)yield return null;
            yield return null;
            Vector3 position=obj.transform.position;float clock=body.CurrentFrame.sim_time;
            var animator=body.riderAnchor.GetComponentInChildren<Animator>();
            float pose=animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
            yield return new WaitForSecondsRealtime(.5f);
            Require(body.CurrentFrame.sim_time==clock && combat.CombatPaused,"body and combat pause together");
            Require((obj.transform.position-position).sqrMagnitude<1e-10f,"projectile holds exact paused position");
            Require(Mathf.Abs(animator.GetCurrentAnimatorStateInfo(0).normalizedTime-pose)<1e-5f,"animation clock holds while paused");
            body.RequestPause(false);while(body.CurrentFrame.sim_time<clock+.06f)yield return null;
            Require(obj && (obj.transform.position-position).magnitude>.3f,"projectile resumes on body clock");
            body.RequestPause(true);while(!body.CurrentFrame.paused)yield return null;yield return null;
            var visual=body.GetComponent<RiderAnimationVisual>();
            animator.Play("Ride",0,0);visual.SetClock(0);
            float before=animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
            float length=animator.GetCurrentAnimatorStateInfo(0).length;
            visual.SetClock(.12f);
            float advanced=(animator.GetCurrentAnimatorStateInfo(0).normalizedTime-before)*length;
            Require(Mathf.Abs(advanced-.12f)<.001f,"animation consumes full arriving simulation interval");
            Destroy(obj);deadline=0;Write("passed");UnityEditor.EditorApplication.isPlaying=false;
        }
        void Write(string status) { File.WriteAllText(Path.Combine(Application.dataPath,"../Research/scientific-clock-evaluation.json"),"{\"status\":\""+status+"\",\"checks\":"+checks+"}"); }
    }
}
#endif
