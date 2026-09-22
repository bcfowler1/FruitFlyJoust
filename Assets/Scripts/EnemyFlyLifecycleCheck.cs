using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace FruitFlyJoust
{
    public sealed class EnemyFlyLifecycleCheck : MonoBehaviour
    {
        int checks;RiderCombat rider;string report;GameObject validationArena;readonly List<string> failures=new List<string>();
        Vector3 baselineRiderScale;float baselineRiderHeight;
        void Require(bool passed,string label)
        {checks++;if(passed)return;string message="Enemy fly lifecycle check failed: "+label;File.WriteAllText(report,"{\"status\":\"failed: "+Escape(message)+"\",\"checks\":"+checks+"}");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying=false;
#endif
            throw new System.Exception(message);}
        void Observe(bool passed,string label){checks++;if(!passed)failures.Add(label);}
        IEnumerator Start()
        {
            report=Path.Combine(Application.dataPath,"../Research/enemy-fly-lifecycle-evaluation.json");
            rider=FindObjectOfType<RiderCombat>();Require(rider,"rider combat exists");
            yield return RunDeathCase();yield return RunUnseatCase();
            if(failures.Count>0)
            {
                string message="Enemy fly lifecycle observations failed: "+string.Join(" | ",failures.ToArray());
                File.WriteAllText(report,"{\"status\":\"failed: "+Escape(message)+"\",\"checks\":"+checks+",\"failures\":"+failures.Count+"}");
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying=false;
#endif
                throw new System.Exception(message);
            }
            File.WriteAllText(report,"{\"status\":\"passed\",\"checks\":"+checks+"}");
            Debug.Log("ENEMY_FLY_LIFECYCLE_PASS: "+checks+" checks");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying=false;
#endif
            Destroy(gameObject);
        }
        IEnumerator FreshJouster()
        {
            foreach(var previous in FindObjectsOfType<MountedJoustOpponent>())if(previous)Destroy(previous.gameObject);
            if(validationArena)Destroy(validationArena);
            // Destroy is deferred until the end of the frame. Wait until every
            // previous root is gone, then build a dedicated validation enemy. This
            // bypasses ResearchViewer and the optional combat-practice floor frame.
            float cleanupDeadline=Time.realtimeSinceStartup+2;
            while(FindObjectsOfType<MountedJoustOpponent>().Length>0 && Time.realtimeSinceStartup<cleanupDeadline)yield return null;
            validationArena=new GameObject("Enemy lifecycle validation arena");
            rider.CreateValidationMountedJouster(validationArena.transform);
            float spawnDeadline=Time.realtimeSinceStartup+2;
            while(!MountedEnemy() && Time.realtimeSinceStartup<spawnDeadline)yield return null;
        }
        MountedJoustOpponent MountedEnemy()
        {
            foreach(var candidate in FindObjectsOfType<MountedJoustOpponent>())if(candidate && candidate.Mounted)return candidate;
            return null;
        }
        IEnumerator RunDeathCase()
        {
            yield return FreshJouster();var enemy=MountedEnemy();Require(enemy,"mounted enemy spawned for death case");
            Require(Resources.Load<GameObject>("Enemies/MountedJouster"),
                "mounted jouster prefab loads from Resources");
            yield return new WaitForSeconds(.5f);
            Observe(enemy.FlyVisibleRendererCount>50 && enemy.FlyVisualBoundsSize>.2f &&
                enemy.AnatomicalForwardAlignment>.9f && enemy.RiderForwardAlignment>.9f && enemy.RiderThoraxDistance<.65f,
                "fresh and replacement enemy uses a complete aligned biological fly visual (renderers="+
                enemy.FlyVisibleRendererCount+", bounds="+enemy.FlyVisualBoundsSize.ToString("F2")+", anatomy="+
                enemy.AnatomicalForwardAlignment.ToString("F2")+", rider="+enemy.RiderForwardAlignment.ToString("F2")+")");
            Vector3 mountedScale=enemy.RiderWorldScale;
            Vector3 mountedSeat=enemy.SaddleLocalPosition;
            Observe(Vector3.Distance(mountedScale,Vector3.one*RiderCombat.CanonicalRiderVisualScale)<.03f,
                "mounted enemy starts at canonical world scale (scale="+mountedScale.ToString("F3")+")");
            float mountedHeight=enemy.RiderVisualHeight;
            baselineRiderScale=mountedScale;baselineRiderHeight=mountedHeight;
            for(int generation=1;generation<=8;generation++)
            {
                int previousRoot=enemy.gameObject.GetInstanceID();
                enemy=enemy.ValidationRespawn();Require(enemy,"fresh prefab instance exists after respawn "+generation);
                yield return new WaitForSeconds(.5f);
                Observe(enemy.gameObject.GetInstanceID()!=previousRoot,
                    "respawn "+generation+" creates a distinct mounted jouster root");
                Observe(Vector3.Distance(enemy.RiderWorldScale,mountedScale)<.03f &&
                    Mathf.Abs(enemy.RiderVisualHeight-mountedHeight)<.08f &&
                    Vector3.Distance(enemy.transform.lossyScale,Vector3.one)<.01f &&
                    Vector3.Distance(enemy.SaddleLocalPosition,mountedSeat)<.03f &&
                    Mathf.Abs(enemy.RiderThoraxLateralOffset)<.025f,
                    "fresh prefab respawn "+generation+" preserves rider scale, height, and seat (scale="+
                    enemy.RiderWorldScale.ToString("F3")+", height="+enemy.RiderVisualHeight.ToString("F3")+
                    ", seat drift="+Vector3.Distance(enemy.SaddleLocalPosition,mountedSeat).ToString("F3")+")");
            }
            Vector3 impactDirection=enemy.transform.right;enemy.FlyHealth.Hit(1000);
            Require(enemy.ReceiveFlyLanceContact(5,impactDirection),"zero-health fly accepts lethal lance contact with impact direction");
            var corpse=enemy.LastFlyCorpse;Require(corpse && corpse.VisibleRendererCount>0,"death creates a visible corpse");
            Observe(corpse.ArticulatedBodyCount>=20 && corpse.AppendageColliderCount>=20,
                "dead fly uses articulated wing and leg rigidbodies with separate small colliders (bodies="+
                corpse.ArticulatedBodyCount+", colliders="+corpse.AppendageColliderCount+")");
            Observe(corpse.Velocity.y<0 && corpse.Velocity.magnitude<4,"corpse starts downward at bounded speed");
            float impactComponent=Vector3.Dot(Vector3.ProjectOnPlane(corpse.Velocity,Vector3.up),impactDirection);
            Observe(impactComponent>.1f,"corpse retains a bounded component of lance impact force (component="+impactComponent.ToString("F3")+")");
            float corpseStartY=corpse.VisualCenter.y,highest=corpseStartY,lowest=highest,deadline=Time.time+5;
            while(corpse && !corpse.HasGroundContact && Time.time<deadline)
            {highest=Mathf.Max(highest,corpse.VisualCenter.y);lowest=Mathf.Min(lowest,corpse.VisualCenter.y);yield return new WaitForFixedUpdate();}
            Require(corpse,"corpse remains present through fall");
            Vector3 fallenScale=enemy.RiderWorldScale;
            Observe(Vector3.Distance(fallenScale,mountedScale)<.03f,
                "fallen enemy ragdoll preserves the mounted rider scale instead of inheriting tack or arena scale (mounted="+
                mountedScale.ToString("F3")+", fallen="+fallenScale.ToString("F3")+")");
            Observe(highest<=corpseStartY+.12f,"corpse never shoots upward after death (rise="+(highest-corpseStartY).ToString("F3")+")");
            Observe(corpse.HasGroundContact && corpse.GroundClearance<=.08f,
                "corpse fully reaches the ground (clearance="+corpse.GroundClearance.ToString("F3")+")");
            Vector3 restStart=corpse.transform.position;yield return new WaitForSeconds(1);
            Observe(corpse && Vector3.Distance(restStart,corpse.transform.position)<1.5f,"grounded corpse remains near its landing point");
        }
        IEnumerator RunUnseatCase()
        {
            // The death case schedules the real replacement jouster used by normal
            // gameplay. Exercise that replacement instead of rebuilding the research
            // arena, whose floor frame may be temporarily unavailable after teardown.
            float replacementDeadline=Time.time+9;var enemy=MountedEnemy();
            while(!enemy && Time.time<replacementDeadline){yield return null;enemy=MountedEnemy();}
            Require(enemy,"replacement mounted enemy spawned for unseat case");
            Observe(Vector3.Distance(enemy.RiderWorldScale,baselineRiderScale)<.03f &&
                Mathf.Abs(enemy.RiderVisualHeight-baselineRiderHeight)<.08f,
                "normal combat replacement preserves the rider's canonical size (scale="+
                enemy.RiderWorldScale.ToString("F3")+", height="+enemy.RiderVisualHeight.ToString("F3")+")");
            int before=FindObjectsOfType<MountedJoustOpponent>().Length;Vector3 riderBefore=enemy.RenderedRiderPosition;Require(enemy.ReceiveLanceContact(5),"healthy rider accepts unseating contact");
            Observe(Vector3.Distance(riderBefore,enemy.RenderedRiderPosition)<.03f,"unseating preserves the rendered rider position on the first falling frame");
            var detached=FindObjectOfType<DetachedEnemyFly>();Require(detached && enemy.FlyEscaping && enemy.RiderRagdolled,"healthy fly remains visible while rider falls");
            Vector3 previous=detached.transform.position,previousRider=enemy.RenderedRiderPosition,previousRoot=enemy.transform.position;
            float maximumStep=0,maximumTransitionStep=0,deadline=Time.time+3;string transitionContext="none";
            while(detached && Time.time<deadline)
            {yield return new WaitForEndOfFrame();maximumStep=Mathf.Max(maximumStep,Vector3.Distance(previous,detached.transform.position));previous=detached.transform.position;float step=Vector3.Distance(previousRider,enemy.RenderedRiderPosition);if(enemy.RiderTransitioning && step>maximumTransitionStep){maximumTransitionStep=step;transitionContext="early mounted="+enemy.Mounted+" rootStep="+Vector3.Distance(previousRoot,enemy.transform.position).ToString("F2")+" riderOffset="+Vector3.Distance(enemy.RenderedRiderPosition,enemy.transform.position).ToString("F2");}previousRider=enemy.RenderedRiderPosition;previousRoot=enemy.transform.position;}
            Observe(detached && maximumStep<.3f,"circling fly moves continuously without teleporting (largest frame step="+
                maximumStep.ToString("F3")+")");
            deadline=Time.time+16;while(!enemy.Mounted && Time.time<deadline)
            {yield return new WaitForEndOfFrame();float step=Vector3.Distance(previousRider,enemy.RenderedRiderPosition);if(enemy.RiderTransitioning && step>maximumTransitionStep){maximumTransitionStep=step;transitionContext="ground mounted="+enemy.Mounted+" rootStep="+Vector3.Distance(previousRoot,enemy.transform.position).ToString("F2")+" riderOffset="+Vector3.Distance(enemy.RenderedRiderPosition,enemy.transform.position).ToString("F2");}previousRider=enemy.RenderedRiderPosition;previousRoot=enemy.transform.position;}
            while(enemy.RiderTransitioning && Time.time<deadline+3)
            {yield return new WaitForEndOfFrame();float step=Vector3.Distance(previousRider,enemy.RenderedRiderPosition);if(step>maximumTransitionStep){maximumTransitionStep=step;transitionContext="mount mounted="+enemy.Mounted+" rootStep="+Vector3.Distance(previousRoot,enemy.transform.position).ToString("F2")+" riderOffset="+Vector3.Distance(enemy.RenderedRiderPosition,enemy.transform.position).ToString("F2");}previousRider=enemy.RenderedRiderPosition;previousRoot=enemy.transform.position;}
            Observe(maximumTransitionStep<.35f,"rendered rider remains continuous during ground recovery and remount transitions (largest transition step="+maximumTransitionStep.ToString("F3")+", "+transitionContext+")");
            int mounted=0;foreach(var candidate in FindObjectsOfType<MountedJoustOpponent>())if(candidate.Mounted)mounted++;
            Observe(enemy.Mounted && enemy.ReturnedRemountCount==1,
                "same rider remounts returning fly (mounted="+enemy.Mounted+", returns="+enemy.ReturnedRemountCount+
                ", health="+enemy.RiderHealth.Health.ToString("F1")+", clearance="+enemy.GroundClearance.ToString("F2")+")");
            int total=FindObjectsOfType<MountedJoustOpponent>().Length;
            Observe(mounted==1 && total==before,
                "unseat recovery does not duplicate mounted jousters (mounted="+mounted+", total="+total+", before="+before+")");
        }
        void OnDestroy(){if(validationArena)Destroy(validationArena);}
        static string Escape(string value){return value.Replace("\\","\\\\").Replace("\"","\\\"").Replace("\r"," ").Replace("\n"," ");}
    }
}
