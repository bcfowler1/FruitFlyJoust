using System.Collections;
using System.IO;
using UnityEngine;

namespace FruitFlyJoust
{
    public sealed class EnemyFlyLifecycleCheck : MonoBehaviour
    {
        int checks;RiderCombat rider;string report;
        void Require(bool passed,string label)
        {checks++;if(passed)return;string message="Enemy fly lifecycle check failed: "+label;File.WriteAllText(report,"{\"status\":\"failed: "+Escape(message)+"\",\"checks\":"+checks+"}");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying=false;
#endif
            throw new System.Exception(message);}
        IEnumerator Start()
        {
            report=Path.Combine(Application.dataPath,"../Research/enemy-fly-lifecycle-evaluation.json");
            rider=FindObjectOfType<RiderCombat>();Require(rider,"rider combat exists");
            yield return RunDeathCase();yield return RunUnseatCase();
            File.WriteAllText(report,"{\"status\":\"passed\",\"checks\":"+checks+"}");
            Debug.Log("ENEMY_FLY_LIFECYCLE_PASS: "+checks+" checks");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying=false;
#endif
            Destroy(gameObject);
        }
        IEnumerator FreshJouster()
        {
            rider.SetPractice(false,false);yield return null;rider.SetPractice(true,true);yield return null;yield return null;
        }
        IEnumerator RunDeathCase()
        {
            yield return FreshJouster();var enemy=FindObjectOfType<MountedJoustOpponent>();Require(enemy && enemy.Mounted,"mounted enemy spawned for death case");
            Vector3 impactDirection=enemy.transform.right;enemy.FlyHealth.Hit(1000);
            Require(enemy.ReceiveFlyLanceContact(5,impactDirection),"zero-health fly accepts lethal lance contact with impact direction");
            var corpse=enemy.LastFlyCorpse;Require(corpse && corpse.VisibleRendererCount>0,"death creates a visible corpse");
            Require(corpse.Velocity.y<0 && corpse.Velocity.magnitude<4,"corpse starts downward at bounded speed");
            Require(Vector3.Dot(Vector3.ProjectOnPlane(corpse.Velocity,Vector3.up),impactDirection)>.1f,"corpse retains a bounded component of lance impact force");
            float corpseStartY=corpse.transform.position.y,highest=corpseStartY,lowest=highest,deadline=Time.time+5;
            while(corpse && !corpse.HasGroundContact && Time.time<deadline)
            {highest=Mathf.Max(highest,corpse.transform.position.y);lowest=Mathf.Min(lowest,corpse.transform.position.y);yield return new WaitForFixedUpdate();}
            Require(corpse,"corpse remains present through fall");
            Require(highest<=corpseStartY+.12f,"corpse never shoots upward after death (rise="+(highest-corpseStartY).ToString("F3")+")");
            Require(lowest<corpseStartY-.2f,"corpse visibly descends");Require(corpse.HasGroundContact,"corpse reaches ground");
            Vector3 restStart=corpse.transform.position;yield return new WaitForSeconds(1);
            Require(corpse && Vector3.Distance(restStart,corpse.transform.position)<1.5f,"grounded corpse remains near its landing point");
        }
        IEnumerator RunUnseatCase()
        {
            yield return FreshJouster();var enemy=FindObjectOfType<MountedJoustOpponent>();Require(enemy && enemy.Mounted,"mounted enemy spawned for unseat case");
            int before=FindObjectsOfType<MountedJoustOpponent>().Length;Require(enemy.ReceiveLanceContact(5),"healthy rider accepts unseating contact");
            var detached=FindObjectOfType<DetachedEnemyFly>();Require(detached && enemy.FlyEscaping && enemy.RiderRagdolled,"healthy fly remains visible while rider falls");
            Vector3 previous=detached.transform.position;float maximumStep=0,deadline=Time.time+3;
            while(detached && Time.time<deadline){yield return null;maximumStep=Mathf.Max(maximumStep,Vector3.Distance(previous,detached.transform.position));previous=detached.transform.position;}
            Require(detached && maximumStep<.3f,"circling fly moves continuously without teleporting");
            deadline=Time.time+16;while(!enemy.Mounted && Time.time<deadline)yield return null;
            int mounted=0;foreach(var candidate in FindObjectsOfType<MountedJoustOpponent>())if(candidate.Mounted)mounted++;
            Require(enemy.Mounted && enemy.ReturnedRemountCount==1,"same rider remounts returning fly");
            Require(mounted==1 && FindObjectsOfType<MountedJoustOpponent>().Length==before,"unseat recovery does not duplicate mounted jousters");
        }
        static string Escape(string value){return value.Replace("\\","\\\\").Replace("\"","\\\"").Replace("\r"," ").Replace("\n"," ");}
    }
}
