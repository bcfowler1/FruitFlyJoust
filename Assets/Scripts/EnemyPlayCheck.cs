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
                Require(jouster.WingSpeedScale>0,"living opponent wing animation is driven by flight speed");
                Require(jouster.GroundClearance>=.55f,"live enemy fly remains above the ground collision surface");
                Require(jouster.RiderForwardAlignment>.9f,"opponent rider faces the lance and travel direction while mounted");
                Require(jouster.RiderHealth && jouster.FlyHealth,"mounted opponent exposes separate rider and fly hit points");
                Require(jouster.RiderHealth.GetComponent<Collider>() && jouster.FlyHealth.GetComponent<Collider>(),"mounted rider and fly have active lance hit volumes");
                Vector3 saddleLocal=jouster.SaddleLocalPosition,riderLocal=jouster.RiderLocalPosition,previousRoot=jouster.transform.position;
                float maximumSaddleDrift=0,maximumRiderDrift=0,maximumRootStep=0;float stabilityDeadline=Clock+.6f;
                while(Clock<stabilityDeadline)
                {
                    yield return null;
                    maximumSaddleDrift=Mathf.Max(maximumSaddleDrift,Vector3.Distance(saddleLocal,jouster.SaddleLocalPosition));
                    maximumRiderDrift=Mathf.Max(maximumRiderDrift,Vector3.Distance(riderLocal,jouster.RiderLocalPosition));
                    maximumRootStep=Mathf.Max(maximumRootStep,Vector3.Distance(previousRoot,jouster.transform.position));previousRoot=jouster.transform.position;
                }
                Require(maximumSaddleDrift<.001f && maximumRiderDrift<.001f,
                    "opponent saddle and rider remain fixed to the thorax frame while turning");
                Require(maximumRootStep<.5f,"opponent flight position advances continuously without frame teleports");
                float riderBefore=jouster.RiderHealth.Health;
                Require(rider.TestLanceHit(jouster.RiderHealth,2),"player lance route accepts mounted rider contact");
                Require(jouster.RiderHealth.Health<riderBefore && FindObjectOfType<FloatingDamageNumber>(),"mounted rider hit shows damage and remaining hit points");
                float flyBefore=jouster.FlyHealth.Health;
                Require(rider.TestLanceHit(jouster.FlyHealth,2),"player lance route accepts enemy fly contact");
                Require(jouster.FlyHealth.Health<flyBefore && jouster.Mounted,"lance independently damages enemy fly");
                Vector3 escapeStart=jouster.transform.position;jouster.RiderHealth.Hit(1000);yield return null;
                Require(jouster.FlyEscaping && !jouster.Mounted && jouster.RiderRagdolled,
                    "shooting the mounted rider separates the falling rider from a living escaping fly");
                float maximumEscapeStep=0,maximumEscapeSpeed=0;Vector3 previousEscape=jouster.transform.position;
                float escapeSampleDeadline=Clock+1.2f;
                while(Clock<escapeSampleDeadline)
                {
                    yield return null;maximumEscapeStep=Mathf.Max(maximumEscapeStep,Vector3.Distance(previousEscape,jouster.transform.position));
                    maximumEscapeSpeed=Mathf.Max(maximumEscapeSpeed,jouster.CurrentVelocity.magnitude);previousEscape=jouster.transform.position;
                }
                float escapeDistance=Vector3.Distance(escapeStart,jouster.transform.position);
                Require(escapeDistance>1 && escapeDistance<12 && maximumEscapeStep<.5f && maximumEscapeSpeed<10,
                    "unridden fly escapes for a visible interval at bounded flight velocity without teleporting");
                Require(jouster.WingSpeedScale>0,"living fly keeps flapping after its rider is shot off");
                float respawnDeadline=Clock+5.2f;while(jouster.RespawnCount<1 && Clock<respawnDeadline)yield return null;
                Require(jouster.Mounted && jouster.RespawnCount==1 && jouster.Competence>initialCompetence,
                    "defeated mounted jouster respawns one competency level stronger");
                Require(jouster.AnatomicalForwardAlignment>.9f && jouster.RiderForwardAlignment>.9f && jouster.RiderThoraxDistance<.65f,
                    "respawned opponent fly and rider remain aligned and centered on the thorax");
                var enemyFoodObject=GameObject.CreatePrimitive(PrimitiveType.Sphere);enemyFoodObject.name="Enemy return feeding check";
                enemyFoodObject.transform.position=jouster.transform.position+jouster.transform.right*3+Vector3.up*.4f;
                var enemyFood=enemyFoodObject.AddComponent<FlyFood>();enemyFood.nutrition=.7f;jouster.SetEnemyHunger(.8f);
                float livingFlyHealth=jouster.FlyHealth.Health;
                Require(jouster.ReceiveLanceContact(5) && jouster.FlyEscaping && jouster.FlyHealth.Health==livingFlyHealth &&
                    FindObjectOfType<DetachedEnemyFly>() && jouster.LastDroppedLance && jouster.LastDroppedLance.useGravity,
                    "unseating a rider from a living fly leaves a visible mount that flies away instead of vanishing");
                float returnDeadline=Clock+16;while(!jouster.Mounted && Clock<returnDeadline)yield return null;
                Require(jouster.Mounted && jouster.ReturnedRemountCount==1 && !FindObjectOfType<DetachedEnemyFly>() &&
                    enemyFood.RemainingFraction<1 && jouster.EnemyHunger<.8f,
                    "hungry living fly circles, feeds, returns after rider recovery, and is remounted by the same rider");
                Destroy(enemyFoodObject);
                float respawnFlyHealth=jouster.FlyHealth.Health;
                Require(rider.TestLanceHit(jouster.FlyHealth,2) && jouster.FlyHealth.Health<respawnFlyHealth,
                    "respawned enemy fly independently takes lance damage");
                yield return WaitClock(.3f);Vector3 flyVelocity=jouster.CurrentVelocity;
                int mountedEnemyCountBeforeFlyDeath=FindObjectsOfType<MountedJoustOpponent>().Length;
                jouster.FlyHealth.Hit(1000);
                Require(jouster.ReceiveFlyLanceContact(5),"destroying the enemy fly unseats its rider");
                Require(jouster.LastFlyCorpse && jouster.LastFlyCorpse.VisibleRendererCount>0 && jouster.LastFlyCorpse.VisualBoundsSize>.2f,
                    "killed enemy fly leaves a visible, correctly scaled biological corpse at the death location");
                Require(jouster.LastFlyCorpse && jouster.LastFlyCorpse.Velocity.y<0,
                    "dead fly loses lift immediately and begins with downward velocity");
                Require(jouster.LastFlyCorpse.Velocity.magnitude<4,
                    "dead fly launch velocity remains slow enough to watch it reach the ground");
                Require(jouster.LastFlyCorpse && (flyVelocity.sqrMagnitude<.01f || Vector3.Dot(jouster.LastFlyCorpse.Velocity,flyVelocity.normalized)>.1f),
                    "dead moving fly becomes a ragdoll corpse retaining forward velocity");
                bool corpseAnimation=false;foreach(var behaviour in jouster.LastFlyCorpse.GetComponentsInChildren<MonoBehaviour>())if(behaviour!=jouster.LastFlyCorpse && behaviour.enabled)corpseAnimation=true;
                Require(!corpseAnimation,"dead fly corpse has no active wing animation");
                float corpseStartY=jouster.LastFlyCorpse.transform.position.y;
                yield return WaitClock(.8f);
                Require(jouster.LastFlyCorpse && jouster.LastFlyCorpse.minimumLifetime>=15,
                    "dead enemy fly remains as a corpse for at least fifteen seconds");
                Require(jouster.LastFlyCorpse.transform.position.y<corpseStartY-.05f || jouster.LastFlyCorpse.Velocity.y<-.1f,
                    "dead enemy fly falls toward the ground under gravity");
                float corpseGroundDeadline=Clock+5;while(jouster.LastFlyCorpse && !jouster.LastFlyCorpse.HasGroundContact && Clock<corpseGroundDeadline)yield return null;
                Require(jouster.LastFlyCorpse && jouster.LastFlyCorpse.HasGroundContact,
                    "dead enemy fly reaches and remains on a ground surface");
                Require(jouster.RiderRagdolled && jouster.RiderRagdollBodyCount>=10,
                    "unseated opponent uses a jointed humanoid bone ragdoll rather than the controller capsule");
                Require(jouster.ReplacementMountScheduled,
                    "dead enemy fly schedules a fresh enemy mount after the corpse has visibly fallen");
                float fallDeadline=Clock+5;while(!jouster.GetComponent<CombatOpponent>() && Clock<fallDeadline)yield return null;
                Require(jouster.GetComponent<CombatOpponent>() && jouster.LastFallDamage<=30 && jouster.RiderHealth.Health>0,
                    "unseated opponent survives bounded fall damage and continues ground combat");
                Require(!jouster.RiderRagdolled,"surviving opponent recovers from ragdoll after landing");
                float replacementDeadline=Clock+7;while(jouster.ReplacementMountScheduled && Clock<replacementDeadline)yield return null;
                int mountedEnemyCount=0;foreach(var candidate in FindObjectsOfType<MountedJoustOpponent>())if(candidate.Mounted)mountedEnemyCount++;
                Require(!jouster.ReplacementMountScheduled && !jouster.Mounted && jouster.GetComponent<CombatOpponent>() &&
                    mountedEnemyCount==1 && FindObjectsOfType<MountedJoustOpponent>().Length==mountedEnemyCountBeforeFlyDeath+1,
                    "surviving rider stays grounded while exactly one replacement flying jouster enters combat");
                jouster.gameObject.SetActive(false);
            }
            var opponents=FindObjectsOfType<CombatOpponent>();
            Require(opponents.Length>=2,"opponents present");
            foreach(var armed in opponents)Require(armed.WeaponVisible,"ground opponent visibly carries its combat weapon");
            float playerHeight=rider.RiderVisualHeight;
            foreach(var groundEnemy in opponents)
            {
                float heightRatio=groundEnemy.VisualHeight/Mathf.Max(.001f,playerHeight);
                var controller=groundEnemy.GetComponent<CharacterController>();
                Require(groundEnemy.VisualHeight>0 && heightRatio>.9f && heightRatio<1.1f && groundEnemy.GroundFootError<1.5f,
                    "ground opponent matches the player rider scale and walking surface");
                Require(controller && Mathf.Abs(controller.height-1.2f)<.001f && Mathf.Abs(controller.radius-.2f)<.001f,
                    "ground opponent collision dimensions match the player rider");
                Require(groundEnemy.SightRange>=15,"ground opponent attention radius remains in world units after visual scaling");
            }
            foreach(var opponent in opponents) opponent.enabled=false;
            foreach(var arrow in FindObjectsOfType<OpponentArrow>()) Destroy(arrow.gameObject);
            rider.ResetHealth();
            landing=true;
            float deadline=Clock+8;
            while((rider.research ? !rider.research.IsPerched : rider.fly.Phase!=RidePhase.Perched) && Clock<deadline)yield return null;
            landing=false;Require(rider.TryDismount(),"rider dismounted for encounter");
            Vector3 origin=rider.FootAvatar.position;
            int scoutIndex=0;
            foreach(var unit in opponents)
            {
                var controller=unit.GetComponent<CharacterController>();controller.enabled=false;
                unit.transform.position=origin+Vector3.forward*21+Vector3.right*(scoutIndex++*1.5f)+Vector3.up;
                controller.enabled=true;unit.enabled=true;
            }
            Physics.SyncTransforms();yield return WaitClock(.2f);
            int spyglassCount=0;CombatOpponent scout=null;
            foreach(var unit in opponents)if(unit.HasSpyglass){spyglassCount++;scout=unit;}
            Require(spyglassCount==1 && scout && scout.SpyglassVisible && scout.RiderVisible,
                "one distant ground unit visibly equips a 24-unit spyglass when all units are outside ordinary awareness");
            yield return WaitClock(2.1f);
            bool shared=false;foreach(var unit in opponents)if(unit!=scout && unit.SharedAwareness)shared=true;
            Require(shared,"spyglass scout conveys the player direction to nearby allies after two seconds");
            Vector3 incoming=(scout.transform.position-origin).normalized;Vector3 evadeStart=scout.transform.position;
            scout.GetComponent<CombatTarget>().Hit(5,incoming);yield return WaitClock(.45f);
            bool warned=false;foreach(var unit in opponents)if(unit!=scout && unit.EvadingIncomingFire)warned=true;
            Require(scout.EvadingIncomingFire && Vector3.Dot(scout.transform.position-evadeStart,incoming)>.02f,
                "unit hit by an unseen arrow moves away along the incoming-fire direction");
            Require(warned,"arrow-hit unit warns nearby allies so they scatter from incoming fire");
            foreach(var opponent in opponents)
            {
                var controller=opponent.GetComponent<CharacterController>();controller.enabled=false;
                opponent.transform.position=new Vector3(20,1,20);controller.enabled=true;
            }
            var enemy=opponents[0];var feet=enemy.GetComponent<CharacterController>();
            feet.enabled=false;enemy.transform.position=origin+Vector3.forward*12+Vector3.up;
            feet.enabled=true;enemy.style=CombatOpponent.Style.Swordsman;enemy.enabled=true;
            Physics.SyncTransforms();Vector3 initial=enemy.transform.position;
            yield return WaitClock(.8f);
            Require(Vector3.Distance(enemy.transform.position,origin)<Vector3.Distance(initial,origin),"swordsman approaches rider");
            Require(Vector3.Distance(initial,origin)>10 && enemy.RiderVisible,
                "swordsman reacts from its world-space attention radius rather than visual scale");
            feet.enabled=false;enemy.transform.position=origin+Vector3.forward*4+Vector3.up;feet.enabled=true;Physics.SyncTransforms();
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

