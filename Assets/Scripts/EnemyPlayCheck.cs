#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using UnityEngine;
namespace FruitFlyJoust
{
    [DefaultExecutionOrder(-50)]
    public sealed class EnemyPlayCheck : MonoBehaviour
    {
        private RiderCombat rider;
        private bool landing;
        private int checks;
        private float timeout;
        float Clock { get { return rider.research ? rider.research.CurrentFrame.sim_time : Time.time; } }
        IEnumerator WaitClock(float seconds) { float until=Clock+seconds;while(Clock<until)yield return null; }
        void Update()
        {
            if (landing && rider) { if(rider.research)rider.research.RequestLanding();else rider.fly.rider.land=true; }
            if(timeout>0 && Time.realtimeSinceStartup>timeout)
            { Report("failed: timeout");timeout=0;UnityEditor.EditorApplication.isPlaying=false; }
        }
        void Require(bool passed,string label)
        {
            if (!passed) { Report("failed: "+label); UnityEditor.EditorApplication.isPlaying=false; throw new Exception(label); }
            checks++;Debug.Log("ENEMY_CHECK: "+label);
        }
        IEnumerator Start()
        {
            rider=FindObjectOfType<RiderCombat>();
            timeout=Time.realtimeSinceStartup+360;
            if(rider.research)
            {
                while(!rider.research.Connected || !rider.FootAvatar)yield return null;
                rider.SetPractice(true,true);yield return null;
            }
            else { rider.course.ResetCourse();yield return new WaitForSeconds(.2f); }
            var jouster=FindObjectOfType<MountedJoustOpponent>();
            if(jouster)
            {
                float initialCompetence=jouster.Competence;
                Require(initialCompetence<.25f && jouster.Mounted,"mounted jouster begins at low competency");
                yield return WaitClock(.25f);
                Require(jouster.WingMotionDegrees>5,"mounted fly mirrors the animated biological wing stroke");
                Require(jouster.RiderForwardAlignment>.9f,"opponent rider faces the lance and travel direction while mounted");
                Require(jouster.RiderHealth && jouster.FlyHealth,"mounted opponent exposes separate rider and fly hit points");
                Require(jouster.RiderHealth.GetComponent<Collider>() && jouster.FlyHealth.GetComponent<Collider>(),"mounted rider and fly have active lance hit volumes");
                float riderBefore=jouster.RiderHealth.Health;
                Require(rider.TestLanceHit(jouster.RiderHealth,2),"player lance route accepts mounted rider contact");
                Require(jouster.RiderHealth.Health<riderBefore && FindObjectOfType<FloatingDamageNumber>(),"mounted rider hit shows damage and remaining hit points");
                float flyBefore=jouster.FlyHealth.Health;
                Require(rider.TestLanceHit(jouster.FlyHealth,2),"player lance route accepts enemy fly contact");
                Require(jouster.FlyHealth.Health<flyBefore && jouster.Mounted,"lance independently damages enemy fly");
                jouster.RiderHealth.Hit(1000);yield return WaitClock(3.2f);
                Require(jouster.Mounted && jouster.RespawnCount==1 && jouster.Competence>initialCompetence,
                    "defeated mounted jouster respawns one competency level stronger");
                Require(jouster.RiderForwardAlignment>.9f && jouster.RiderThoraxDistance<.65f,
                    "respawned opponent rider remains aligned and centered on the thorax");
                float respawnFlyHealth=jouster.FlyHealth.Health;
                Require(rider.TestLanceHit(jouster.FlyHealth,2) && jouster.FlyHealth.Health<respawnFlyHealth,
                    "respawned enemy fly independently takes lance damage");
                yield return WaitClock(.3f);Vector3 flyVelocity=jouster.CurrentVelocity;
                jouster.FlyHealth.Hit(1000);
                Require(jouster.ReceiveFlyLanceContact(5),"destroying the enemy fly unseats its rider");
                Require(jouster.LastFlyCorpse && (flyVelocity.sqrMagnitude<.01f || Vector3.Dot(jouster.LastFlyCorpse.Velocity,flyVelocity.normalized)>.1f),
                    "dead moving fly becomes a ragdoll corpse retaining forward velocity");
                Require(jouster.RiderRagdolled,"unseated opponent enters ragdoll while falling");
                float fallDeadline=Clock+5;while(!jouster.GetComponent<CombatOpponent>() && Clock<fallDeadline)yield return null;
                Require(jouster.GetComponent<CombatOpponent>() && jouster.LastFallDamage<=30 && jouster.RiderHealth.Health>0,
                    "unseated opponent survives bounded fall damage and continues ground combat");
                Require(!jouster.RiderRagdolled,"surviving opponent recovers from ragdoll after landing");
                jouster.gameObject.SetActive(false);
            }
            var opponents=FindObjectsOfType<CombatOpponent>();
            Require(opponents.Length>=2,"opponents present");
            foreach(var armed in opponents)Require(armed.WeaponVisible,"ground opponent visibly carries its combat weapon");
            foreach(var opponent in opponents) opponent.enabled=false;
            foreach(var arrow in FindObjectsOfType<OpponentArrow>()) Destroy(arrow.gameObject);
            rider.ResetHealth();
            landing=true;
            float deadline=Clock+8;
            while((rider.research ? !rider.research.IsPerched : rider.fly.Phase!=RidePhase.Perched) && Clock<deadline)yield return null;
            landing=false;Require(rider.TryDismount(),"rider dismounted for encounter");
            Vector3 origin=rider.FootAvatar.position;
            foreach(var opponent in opponents)
            {
                var controller=opponent.GetComponent<CharacterController>();controller.enabled=false;
                opponent.transform.position=new Vector3(20,1,20);controller.enabled=true;
            }
            var enemy=opponents[0];var feet=enemy.GetComponent<CharacterController>();
            feet.enabled=false;enemy.transform.position=origin+Vector3.forward*4+Vector3.up;
            feet.enabled=true;enemy.style=CombatOpponent.Style.Swordsman;enemy.enabled=true;
            Physics.SyncTransforms();Vector3 initial=enemy.transform.position;
            yield return WaitClock(.8f);
            Require(Vector3.Distance(enemy.transform.position,origin)<Vector3.Distance(initial,origin),"swordsman approaches rider");
            deadline=Clock+4;
            while(rider.Health==100&&Clock<deadline)yield return null;
            Require(rider.Health<100,"enemy melee damages rider");
            enemy.enabled=false;float health=rider.Health;
            enemy.GetComponent<CombatTarget>().Hit(100);enemy.enabled=true;
            yield return WaitClock(1.4f);
            Require(rider.Health==health,"defeated opponent stops attacking");enemy.enabled=false;
            feet.enabled=false;enemy.transform.position=new Vector3(20,1,24);feet.enabled=true;
            var archer=opponents[1];var archerFeet=archer.GetComponent<CharacterController>();
            archerFeet.enabled=false;archer.transform.position=origin+Vector3.forward*5+Vector3.up;
            archerFeet.enabled=true;archer.style=CombatOpponent.Style.Archer;archer.enabled=true;
            Physics.SyncTransforms();deadline=Clock+4;
            while(rider.Health==health&&Clock<deadline)yield return null;
            Require(rider.Health<health,"enemy arrow damages rider");archer.enabled=false;
            Require(!rider.TakeDamage(float.NaN),"invalid damage refused");
            Vector3 defeatPosition=rider.RiderPosition;
            rider.TakeDamage(1000);Require(rider.Defeated,"rider defeat state");
            Require(!rider.TakeDamage(10),"defeated rider refuses further damage");
            int respawns=rider.PlayerRespawnCount;deadline=Clock+rider.defeatRespawnDelay+.75f;
            while(rider.PlayerRespawnCount==respawns&&Clock<deadline)yield return null;
            Require(!rider.Defeated && rider.Health==100 && rider.PlayerRespawnCount==respawns+1,"defeated rider automatically respawns at full health");
            float respawnNearest=float.PositiveInfinity,defeatNearest=float.PositiveInfinity;
            foreach(var opponent in FindObjectsOfType<CombatOpponent>())if(!opponent.Defeated)
            {respawnNearest=Mathf.Min(respawnNearest,Vector3.Distance(rider.RiderPosition,opponent.transform.position));defeatNearest=Mathf.Min(defeatNearest,Vector3.Distance(defeatPosition,opponent.transform.position));}
            Require(respawnNearest>defeatNearest,"player respawns farther from living opponents");
            timeout=0;Report("passed");UnityEditor.EditorApplication.isPlaying=false;
        }
        void Report(string status) {File.WriteAllText(Path.Combine(Application.dataPath,rider && rider.research ? "../Research/scientific-enemy-evaluation.json" : "../Research/enemy-play-evaluation.json"),
            "{\"status\":\""+status+"\",\"checks\":"+checks+"}");}
    }
}
#endif

