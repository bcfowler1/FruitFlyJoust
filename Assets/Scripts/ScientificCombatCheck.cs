#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace FruitFlyJoust
{
    [DefaultExecutionOrder(-50)]
    public sealed class ScientificCombatCheck : MonoBehaviour
    {
        private ResearchViewer body;
        private RiderCombat combat;
        private int checks;
        private float deadline;
        private bool walkingRequest, lanceRequest;
        private Transform lanceTarget;
        private float diagnosticTime=-1;
        private string lanceDiagnostic="";
        void Require(bool ok,string label)
        {
            if (!ok) { Write("failed: "+label); deadline=0; UnityEditor.EditorApplication.isPlaying=false; throw new Exception(label); }
            checks++;Debug.Log("SCIENTIFIC_COMBAT_CHECK: "+label);
        }
        void Update()
        {
            if (body && body.riderAnchor)
            {
                var input=body.riderAnchor.GetComponent<RiderInput>();
                if (walkingRequest) input.reins=new Vector2(0,-.4f);
                if (lanceRequest) input.primaryAction=1;
            }
            if(lanceRequest && lanceTarget && body.CurrentFrame.sim_time>diagnosticTime+.1f)
            {
                diagnosticTime=body.CurrentFrame.sim_time;
                lanceDiagnostic="time="+diagnosticTime+" tip="+combat.LanceTip.ToString("F3")+" target="+lanceTarget.position.ToString("F3")+" velocity="+body.BodyVelocity.ToString("F3")+" forward="+body.riderAnchor.forward.ToString("F3");
                Debug.Log("SCIENTIFIC_LANCE_TRACE: "+lanceDiagnostic);
            }
            if (deadline>0 && Time.realtimeSinceStartup>deadline)
            { Write("failed: timeout");deadline=0;UnityEditor.EditorApplication.isPlaying=false; }
        }
        IEnumerator WaitSimulation(float seconds)
        {
            float target=body.CurrentFrame.sim_time+seconds;
            while (body.CurrentFrame.sim_time<target)yield return null;
        }
        IEnumerator Start()
        {
            deadline=Time.realtimeSinceStartup+180;
            body=FindObjectOfType<ResearchViewer>();
            body.autonomousIdle=false; // This regression requires a stationary mount for precise combat targets.
            while (!body.Connected || !body.Combat || !body.Combat.FootAvatar)yield return null;
            combat=body.Combat;
            Require(!combat.TryDismount(),"walking dismount refused");
            body.RequestLanding();while (!body.IsPerched)yield return null;
            Require(combat.TryDismount(),"safe dismount from real claw hold");
            while (body.CurrentFrame.rider_attached)yield return null;
            Require(!body.CurrentFrame.rider_attached,"physical rider load removed");
            yield return WaitSimulation(.15f);
            Require(combat.FootAvatar.GetComponent<CharacterController>().isGrounded,"foot controller grounded");
            var target=GameObject.CreatePrimitive(PrimitiveType.Capsule);target.name="Scientific combat test dummy";
            var damage=target.AddComponent<CombatTarget>();
            Vector3 position=combat.FootAvatar.position;
            combat.view.enabled=false;
            combat.view.transform.position=position+Vector3.up*.9f-Vector3.forward*4;
            combat.view.transform.rotation=Quaternion.identity;
            target.transform.position=position+Vector3.up*.7f+Vector3.forward*1.2f;
            Physics.SyncTransforms();combat.SelectWeapon(RiderCombat.Weapon.Sword);
            combat.SwordStrike();Require(damage.Health==100,"melee waits for animated windup");
            yield return WaitSimulation(.2f);
            Require(damage.Health==75,"on-foot melee hits");
            combat.SwordStrike();Require(damage.Health==75,"melee cooldown");
            yield return WaitSimulation(.45f);
            combat.SelectWeapon(RiderCombat.Weapon.Bow);
            target.transform.position=position+Vector3.up*.9f+Vector3.forward*6;
            Physics.SyncTransforms();combat.ReleaseArrow(1);
            yield return WaitSimulation(.4f);
            Require(damage.Health==30,"on-foot arrow hits on scientific clock");
            Require(combat.TryMount(),"remount beside perched scientific body");
            while (!body.CurrentFrame.rider_attached)yield return null;
            Require(body.CurrentFrame.rider_attached,"one-third physical rider load restored");
            damage.ResetTarget();combat.SelectWeapon(RiderCombat.Weapon.Bow);
            combat.view.transform.position=body.riderAnchor.position+Vector3.up*.9f-Vector3.forward*4;
            combat.view.transform.rotation=Quaternion.identity;
            target.transform.position=body.riderAnchor.position+Vector3.up*.9f+Vector3.forward*6;
            Physics.SyncTransforms();combat.ReleaseArrow(1);yield return WaitSimulation(.4f);
            Require(damage.Health==55,"mounted arrow hits on scientific body");
            damage.ResetTarget();combat.SelectWeapon(RiderCombat.Weapon.Lance);
            walkingRequest=true;while (body.CurrentFrame.perch_phase!="walking") yield return null;
            walkingRequest=false;
            while (Vector3.Dot(body.BodyVelocity,body.riderAnchor.forward)<=3) yield return null;
            Require(body.CurrentFrame.rider_attached,"walking rider load remains attached");
            // A fixed target ahead of the lance; instantaneous body velocity includes vertical gait oscillation.
            foreach(float phaseDelay in new[]{0f,.1f,.2f})
            {
                yield return WaitSimulation(phaseDelay);
                damage.ResetTarget();lanceTarget=target.transform;lanceRequest=true;
                target.transform.position=combat.LanceTip+body.riderAnchor.forward*.8f;Physics.SyncTransforms();
                while(damage.Health==100)yield return null;
                Require(damage.Health<100,"moving scientific-body lance collision at phase "+phaseDelay);
                lanceRequest=false;yield return WaitSimulation(.55f);
            }
            Require(!float.IsNaN(combat.LanceTip.x) && !float.IsInfinity(combat.LanceTip.x),"finite hand-attached weapon pose");
            if(body.CurrentFrame.neurons>0) Require(Mathf.Abs(body.CurrentFrame.neural_time-body.CurrentFrame.sim_time)<.001f,"full-brain and body clocks match during combat");
            combat.view.enabled=true;Destroy(target);deadline=0;Write("passed");
            UnityEditor.EditorApplication.isPlaying=false;
        }
        void Write(string status)
        {
            int neurons=body && body.CurrentFrame!=null ? body.CurrentFrame.neurons : 0;
            string report="{\"status\":\""+status+"\",\"checks\":"+checks+",\"neurons\":"+neurons+",\"scientific_body\":\"NeuroMechFly\",\"limits\":\"Gameplay combat damage; no biological injury model or wing flight\"}";
            if(lanceDiagnostic!="")Debug.Log("SCIENTIFIC_LANCE_FINAL: "+lanceDiagnostic);
            File.WriteAllText(Path.Combine(Application.dataPath,"../Research/"+(neurons>0 ? "scientific-combat-full-brain-evaluation.json" : "scientific-combat-evaluation.json")),report);
        }
    }
}
#endif
