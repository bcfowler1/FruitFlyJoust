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
        bool TryFindEncounterFooting(Vector3 riderFeet,float radius,float minimumFlyClearance,out Vector3 placement)
        {
            placement=Vector3.zero;
            float bestClearance=float.NegativeInfinity;
            Vector3 fly=rider.FlyHitPosition;
            for(int i=0;i<24;i++)
            {
                float angle=i*Mathf.PI*2/24;
                Vector3 offset=new Vector3(Mathf.Sin(angle),0,Mathf.Cos(angle))*radius;
                foreach(var hit in Physics.RaycastAll(riderFeet+offset+Vector3.up*2,Vector3.down,4,1,QueryTriggerInteraction.Ignore))
                {
                    if(!hit.collider || Vector3.Dot(hit.normal,Vector3.up)<.75f ||
                       hit.collider.attachedRigidbody ||
                       hit.collider.GetComponentInParent<CharacterController>() ||
                       hit.collider.GetComponentInParent<MountedJoustOpponent>())continue;
                    float heightDifference=Mathf.Abs(hit.point.y-riderFeet.y);
                    if(heightDifference>.2f)continue;
                    float flyClearance=Vector3.ProjectOnPlane(hit.point-fly,Vector3.up).magnitude;
                    if(flyClearance<minimumFlyClearance || flyClearance<=bestClearance)continue;
                    Vector3 eye=hit.point+Vector3.up*.66f;
                    Vector3 targetEye=riderFeet+Vector3.up*.6f;
                    Vector3 ray=targetEye-eye;
                    bool obstructed=false;
                    foreach(var sightHit in Physics.RaycastAll(eye,ray.normalized,ray.magnitude,1,QueryTriggerInteraction.Ignore))
                    {
                        if(sightHit.collider.GetComponentInParent<CombatOpponent>() ||
                           sightHit.collider.GetComponentInParent<CharacterController>() &&
                           sightHit.collider.transform.IsChildOf(rider.FootAvatar))continue;
                        if(sightHit.distance<ray.magnitude-.5f){obstructed=true;break;}
                    }
                    if(obstructed)continue;
                    placement=hit.point+Vector3.up*.06f;
                    bestClearance=flyClearance;
                }
            }
            return bestClearance>float.NegativeInfinity;
        }
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
                Require(jouster.WingMotionDegrees>5,"mounted enemy fly independently animates its biological wing stroke");
                Require(jouster.WingSpeedScale>0,"living opponent wing animation is driven by flight speed");
                Require(jouster.GroundClearance>=.55f,"live enemy fly remains above the ground collision surface");
                Require(jouster.RiderForwardAlignment>.9f,"opponent rider faces the lance and travel direction while mounted");
                Require(Mathf.Abs(jouster.RiderThoraxLateralOffset)<.025f,
                    "opponent rider is centered on the fly thorax forward axis (lateral="+
                    jouster.RiderThoraxLateralOffset.ToString("F3")+")");
                Require(rider.LanceGripError<.03f && jouster.LanceGripError<.03f,
                    "player and opponent lances are held at the authored handle in their right hands");
                Require(Mathf.Abs(rider.LanceReach-jouster.LanceReach)<.03f,
                    "player and opponent have equal effective lance reach from the hand (player="+
                    rider.LanceReach.ToString("F3")+", opponent="+jouster.LanceReach.ToString("F3")+")");
                Require(jouster.RiderHealth && jouster.FlyHealth,"mounted opponent exposes separate rider and fly hit points");
                CombatTarget enemyHaltere=null;foreach(var target in jouster.GetComponentsInChildren<CombatTarget>())if(target.name.Contains("haltere")){enemyHaltere=target;break;}
                float intactHaltere=jouster.HaltereIntegrity;Require(enemyHaltere && rider.TestLanceHit(enemyHaltere,2),"enemy haltere has a lance-addressable hit volume");yield return null;
                Require(jouster.HaltereIntegrity<intactHaltere,"enemy haltere damage reduces flight stability without counting as an unseat");
                var scentObject=new GameObject("Scent acquisition check");scentObject.transform.position=jouster.transform.position-Vector3.right*2;var scent=scentObject.AddComponent<ScentedBait>();yield return null;
                Require(scent.Contains(jouster.transform.position) && jouster.ScentSearching,
                    "enemy fly only acquires scented bait after entering its downwind plume and retains a search memory");Destroy(scentObject);
                jouster.SetEnemyHunger(0);float enemyFedTop=jouster.EnemyTopSpeed;
                jouster.SetEnemyHunger(1);float enemyHungryTop=jouster.EnemyTopSpeed;jouster.SetEnemyHunger(.35f);
                Require(Mathf.Abs(enemyHungryTop/enemyFedTop-.9f)<.001f,
                    "enemy fly top speed uses the same restrained hunger scaling");
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
                Vector3 escapeStart=jouster.transform.position;float flyHealthBeforeRiderShot=jouster.FlyHealth.Health;
                jouster.RiderHealth.Hit(1000);yield return null;
                Require(jouster.FlyEscaping && !jouster.Mounted && jouster.RiderRagdolled,
                    "shooting the mounted rider separates the falling rider from a living escaping fly");
                Require(jouster.FlyHealth.Health==flyHealthBeforeRiderShot,
                    "an arrow kill on the rider does not damage or kill the independently healthy fly");
                float maximumEscapeStep=0,maximumEscapeSpeed=0;Vector3 previousEscape=jouster.transform.position;
                float escapeSampleDeadline=Clock+1.2f;
                while(Clock<escapeSampleDeadline)
                {
                    yield return null;maximumEscapeStep=Mathf.Max(maximumEscapeStep,Vector3.Distance(previousEscape,jouster.transform.position));
                    maximumEscapeSpeed=Mathf.Max(maximumEscapeSpeed,jouster.CurrentVelocity.magnitude);previousEscape=jouster.transform.position;
                }
                float escapeDistance=Vector3.Distance(escapeStart,jouster.transform.position);
                Require(escapeDistance>1 && escapeDistance<12 && maximumEscapeStep<.5f && maximumEscapeSpeed<10,
                    "unridden fly escapes for a visible interval at bounded flight velocity without teleporting (distance="+
                    escapeDistance.ToString("F2")+", step="+maximumEscapeStep.ToString("F3")+", speed="+
                    maximumEscapeSpeed.ToString("F2")+")");
                Require(jouster.WingSpeedScale>0,"living fly keeps flapping after its rider is shot off");
                float respawnDeadline=Clock+5.2f;
                while(Clock<respawnDeadline)
                {
                    MountedJoustOpponent replacement=null;
                    foreach(var candidate in FindObjectsOfType<MountedJoustOpponent>())
                        if(candidate && candidate.Mounted && candidate.RespawnCount>=1){replacement=candidate;break;}
                    if(replacement){jouster=replacement;break;}
                    yield return null;
                }
                Require(jouster.Mounted && jouster.RespawnCount==1 && jouster.Competence>initialCompetence,
                    "defeated mounted jouster respawns one competency level stronger");
                Require(!rider.PlayerCameraFrameContains(jouster.LastRespawnPosition,1.25f),
                    "defeated enemy jouster respawns outside the player camera frame");
                Require(rider.LastEnemyRespawnUsesCornerEntry,
                    "enemy fly enters from beyond an upper corner or a farther lower camera corner");
                Require(jouster.AnatomicalForwardAlignment>.9f && jouster.RiderForwardAlignment>.9f &&
                    jouster.RiderThoraxDistance<.65f && Mathf.Abs(jouster.RiderThoraxLateralOffset)<.025f,
                    "respawned opponent fly and rider remain aligned and centered on the thorax");
                var enemyFoodObject=GameObject.CreatePrimitive(PrimitiveType.Sphere);enemyFoodObject.name="Enemy return feeding check";
                // DetachedEnemyFly chooses the closest available food at unseat.
                // Put this measured source next to that fly, rather than letting
                // an arena pellet become its nearer target during the check.
                enemyFoodObject.transform.position=jouster.transform.position+jouster.transform.right*.75f+Vector3.up*.4f;
                var enemyFood=enemyFoodObject.AddComponent<FlyFood>();enemyFood.nutrition=.7f;jouster.SetEnemyHunger(.8f);
                float livingFlyHealth=jouster.FlyHealth.Health;
                Require(jouster.ReceiveLanceContact(5) && jouster.FlyEscaping && jouster.FlyHealth.Health==livingFlyHealth &&
                    FindObjectOfType<DetachedEnemyFly>() && jouster.LastDroppedLance && jouster.LastDroppedLance.useGravity,
                    "unseating a rider from a living fly leaves a visible mount that flies away instead of vanishing");
                float returnDeadline=Clock+16;while(!jouster.Mounted && Clock<returnDeadline)yield return null;
                Require(jouster.Mounted && jouster.ReturnedRemountCount==1 && !FindObjectOfType<DetachedEnemyFly>() &&
                    enemyFood.RemainingFraction<1 && jouster.EnemyHunger<.8f,
                    "hungry living fly circles, feeds, returns after rider recovery, and is remounted by the same rider (mounted="+
                    jouster.Mounted+", returns="+jouster.ReturnedRemountCount+", detached="+(FindObjectOfType<DetachedEnemyFly>()!=null)+
                    ", food="+enemyFood.RemainingFraction.ToString("F2")+", hunger="+jouster.EnemyHunger.ToString("F2")+")");
                Destroy(enemyFoodObject);
                float respawnFlyHealth=jouster.FlyHealth.Health;
                Require(rider.TestLanceHit(jouster.FlyHealth,2) && jouster.FlyHealth.Health<respawnFlyHealth,
                    "respawned enemy fly independently takes lance damage");
                yield return WaitClock(.3f);
                float poseDeadline=Clock+3;
                while(jouster.RiderTransitioning && Clock<poseDeadline)yield return null;
                yield return new WaitForEndOfFrame();
                Vector3 flyVelocity=jouster.CurrentVelocity;
                float riderFallStartY=jouster.RenderedRiderPosition.y;
                float originalMountedRiderHeight=jouster.RiderVisualHeight;
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
                bool riderDescended=false,riderWasRagdolled=false;int riderRagdollBodies=0;
                float corpseGroundDeadline=Clock+7;
                while(jouster.LastFlyCorpse && (!jouster.LastFlyCorpse.HasGroundContact || jouster.LastFlyCorpse.RestingSeconds<.2f) && Clock<corpseGroundDeadline)
                {
                    riderDescended|=jouster.RenderedRiderPosition.y<riderFallStartY-.2f;
                    riderWasRagdolled|=jouster.RiderRagdolled;riderRagdollBodies=Mathf.Max(riderRagdollBodies,jouster.RiderRagdollBodyCount);
                    yield return null;
                }
                Require(jouster.LastFlyCorpse && jouster.LastFlyCorpse.HasGroundContact,
                    "dead enemy fly reaches and remains on a ground surface (clearance="+
                    (jouster.LastFlyCorpse ? jouster.LastFlyCorpse.GroundClearance.ToString("F3") : "missing")+")");
                Require(jouster.LastFlyCorpse && jouster.LastFlyCorpse.RestingSeconds>=.2f,
                    "dead enemy fly settles and lies still on the ground instead of vanishing or remaining airborne");
                Require(riderWasRagdolled && riderRagdollBodies>=10,
                    "unseated opponent uses a jointed humanoid bone ragdoll rather than the controller capsule");
                Require(riderDescended,
                    "unseated enemy rider visibly falls with the fly and lies as a ground ragdoll before recovery");
                Require(jouster.ReplacementMountScheduled,
                    "dead enemy fly schedules a fresh enemy mount after the corpse has visibly fallen");
                float fallDeadline=Clock+5;
                while(jouster && !jouster.GetComponent<CombatOpponent>() && Clock<fallDeadline)yield return null;
                bool survivedFall=jouster && jouster.RiderHealth.Health>0;
                if(jouster)
                {
                    Require(jouster.LastFallDamage<=30 && jouster.UnseatingCount==2 &&
                        (!survivedFall || jouster.GetComponent<CombatOpponent>()),
                        "unseating applies cumulative health damage and a survivor continues ground combat (unseatings="+
                        jouster.UnseatingCount+", fallDamage="+jouster.LastFallDamage.ToString("F1")+", health="+
                        jouster.RiderHealth.Health.ToString("F1")+", grounded="+(jouster.GetComponent<CombatOpponent>()!=null)+
                        ", clearance="+jouster.GroundClearance.ToString("F2")+", feet="+jouster.FeetGrounded+
                        ", vertical="+jouster.FallVerticalSpeed.ToString("F2")+", clock="+rider.CombatDeltaTime.ToString("F3")+
                        ", position="+jouster.transform.position.ToString("F2")+")");
                    if(survivedFall)Require(!jouster.RiderRagdolled,"surviving opponent recovers from ragdoll after landing");
                }
                float replacementDeadline=Clock+7;while(jouster && jouster.ReplacementMountScheduled && Clock<replacementDeadline)yield return null;
                int mountedEnemyCount=0;MountedJoustOpponent replacementRider=null;
                foreach(var candidate in FindObjectsOfType<MountedJoustOpponent>())if(candidate.Mounted){mountedEnemyCount++;replacementRider=candidate;}
                int totalJousters=FindObjectsOfType<MountedJoustOpponent>().Length;
                bool validSurvivorReplacement=survivedFall && jouster && !jouster.Mounted && jouster.GetComponent<CombatOpponent>() &&
                    totalJousters==mountedEnemyCountBeforeFlyDeath+1;
                bool validDeathReplacement=!survivedFall && mountedEnemyCount==1 && totalJousters==mountedEnemyCountBeforeFlyDeath;
                Require((!jouster || !jouster.ReplacementMountScheduled) && mountedEnemyCount==1 &&
                    (validSurvivorReplacement || validDeathReplacement),
                    "a fallen rider leaves exactly one replacement flying jouster while only a survivor remains grounded (survived="+
                    survivedFall+", originalMounted="+(jouster && jouster.Mounted)+", mounted="+mountedEnemyCount+", total="+totalJousters+")");
                Require(replacementRider && Mathf.Abs(replacementRider.RiderVisualHeight-originalMountedRiderHeight)<.08f &&
                    Vector3.Distance(replacementRider.RiderWorldScale,Vector3.one*RiderCombat.CanonicalRiderVisualScale)<.03f,
                    "replacement mounted rider keeps the same canonical size after repeated fly deaths (originalHeight="+
                    originalMountedRiderHeight.ToString("F3")+", newHeight="+(replacementRider ? replacementRider.RiderVisualHeight.ToString("F3") : "missing")+
                    ", worldScale="+(replacementRider ? replacementRider.RiderWorldScale.ToString("F3") : "missing")+")");
                if(jouster)jouster.gameObject.SetActive(false);
            }
            var opponents=FindObjectsOfType<CombatOpponent>();
            Require(opponents.Length>=2,"opponents present");
            foreach(var armed in opponents)Require(armed.WeaponVisible,"ground opponent visibly carries its combat weapon");
            float playerHeight=rider.RiderVisualHeight;
            foreach(var groundEnemy in opponents)
            {
                float heightRatio=groundEnemy.VisualHeight/Mathf.Max(.001f,playerHeight);
                var controller=groundEnemy.GetComponent<CharacterController>();
                Require(groundEnemy.VisualHeight>0 && groundEnemy.GroundFootError<.2f && groundEnemy.SupportedByWalkableGround,
                    "ground opponent is visible and aligned to a real walking surface (standing/mounted height ratio="+
                    heightRatio.ToString("F2")+", footError="+groundEnemy.GroundFootError.ToString("F3")+
                    ", supported="+groundEnemy.SupportedByWalkableGround+")");
                Require(controller && Mathf.Abs(controller.height-1.2f*RiderCombat.RiderBodyScale)<.001f && Mathf.Abs(controller.radius-.2f*RiderCombat.RiderBodyScale)<.001f,
                    "ground opponent collision dimensions match the player rider");
                Vector3 localScale=groundEnemy.VisualLocalScale,actorScale=groundEnemy.ActorWorldScale;
                Require(Mathf.Abs(localScale.x-RiderCombat.CanonicalRiderVisualScale)<.015f &&
                    Mathf.Abs(localScale.y-RiderCombat.CanonicalRiderVisualScale)<.015f &&
                    Mathf.Abs(localScale.z-RiderCombat.CanonicalRiderVisualScale)<.015f &&
                    Mathf.Abs(actorScale.x-1)<.015f && Mathf.Abs(actorScale.y-1)<.015f && Mathf.Abs(actorScale.z-1)<.015f,
                    "ground opponent starts with the canonical local body scale on an unscaled gameplay root (body="+
                    localScale.ToString("F3")+", actor="+actorScale.ToString("F3")+")");
                Require(groundEnemy.SightRange>=15,"ground opponent attention radius remains in world units after visual scaling");
            }
            if(opponents.Length>0)
            {
                var airborne=opponents[0];var airborneController=airborne.GetComponent<CharacterController>();
                Vector3 savedPosition=airborne.transform.position;Quaternion savedRotation=airborne.transform.rotation;
                airborneController.enabled=false;airborne.transform.position=savedPosition+Vector3.up*2;airborneController.enabled=true;
                Physics.SyncTransforms();float suspendedY=airborne.transform.position.y,minY=suspendedY;bool observedUnsupported=false;
                float airborneDeadline=Clock+1.2f;
                while(Clock<airborneDeadline && minY>=suspendedY-.05f)
                {
                    yield return null;observedUnsupported|=!airborne.SupportedByWalkableGround || airborne.UnsupportedTime>0;
                    minY=Mathf.Min(minY,airborne.transform.position.y);
                }
                Require(observedUnsupported && minY<suspendedY-.05f,
                    "unsupported ground opponent stops walking and falls instead of remaining suspended in midair (startY="+
                    suspendedY.ToString("F3")+", minY="+minY.ToString("F3")+", supportedNow="+airborne.SupportedByWalkableGround+")");
                airborneController.enabled=false;airborne.transform.position=savedPosition;airborne.transform.rotation=savedRotation;airborneController.enabled=true;
                Physics.SyncTransforms();yield return WaitClock(.12f);
                Require(airborne.SupportedByWalkableGround,"ground opponent reacquires a real walkable surface after falling");
            }
            foreach(var opponent in opponents) opponent.enabled=false;
            foreach(var arrow in FindObjectsOfType<OpponentArrow>()) Destroy(arrow.gameObject);
            rider.ResetHealth();
            landing=true;
            float deadline=Clock+8;
            while((rider.research ? !rider.research.IsPerched : rider.fly.Phase!=RidePhase.Perched) && Clock<deadline)yield return null;
            landing=false;Require(rider.TryDismount(),"rider dismounted for encounter");
            Vector3 origin=rider.FootAvatar.position;
            bool clearSpyglassFormation=false;
            for(int attempt=0;attempt<16 && !clearSpyglassFormation;attempt++)
            {
                float angle=attempt*Mathf.PI*2/16;Vector3 radial=new Vector3(Mathf.Sin(angle),0,Mathf.Cos(angle));
                Vector3 tangent=Vector3.Cross(Vector3.up,radial);int scoutIndex=0;
                foreach(var unit in opponents)
                {
                    var controller=unit.GetComponent<CharacterController>();controller.enabled=false;
                    unit.transform.position=origin+radial*21+tangent*((scoutIndex++-1.5f)*1.5f);
                    controller.enabled=true;unit.enabled=true;
                }
                Physics.SyncTransforms();yield return WaitClock(.1f);
                foreach(var unit in opponents)if(unit.HasSpyglass && unit.RiderVisible){clearSpyglassFormation=true;break;}
            }
            yield return WaitClock(.1f);
            int spyglassCount=0;CombatOpponent scout=null;
            foreach(var unit in opponents)if(unit.HasSpyglass){spyglassCount++;scout=unit;}
            Require(spyglassCount==1 && scout && scout.SpyglassVisible && scout.RiderVisible,
                "one distant ground unit visibly equips a 24-unit spyglass when all units are outside ordinary awareness (count="+
                spyglassCount+", prop="+(scout && scout.SpyglassVisible)+", sight="+(scout && scout.RiderVisible)+")");
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
            foreach(var unit in opponents)if(unit!=enemy)unit.enabled=false;
            foreach(var projectile in FindObjectsOfType<OpponentArrow>())Destroy(projectile.gameObject);
            Vector3 approachFooting;
            Require(TryFindEncounterFooting(rider.RiderPosition,12,2,out approachFooting),
                "a visible walkable approach position exists within the swordsman's sight radius");
            feet.enabled=false;enemy.transform.position=approachFooting;
            feet.enabled=true;enemy.style=CombatOpponent.Style.Swordsman;enemy.ClearTransientCombatState();enemy.enabled=true;
            Physics.SyncTransforms();Vector3 initial=enemy.transform.position;
            yield return WaitClock(.8f);
            float initialDistance=Vector3.Distance(initial,origin),finalDistance=Vector3.Distance(enemy.transform.position,origin);
            Require(finalDistance<initialDistance,"swordsman approaches rider (initial="+initialDistance.ToString("F2")+
                ", final="+finalDistance.ToString("F2")+", supported="+enemy.SupportedByWalkableGround+", visible="+enemy.RiderVisible+")");
            Require(Vector3.Distance(initial,origin)>10 && enemy.RiderVisible,
                "swordsman reacts from its world-space attention radius rather than visual scale");
            Vector3 meleeFooting;
            Require(TryFindEncounterFooting(rider.RiderPosition,1.4f,1.8f,out meleeFooting),
                "a walkable melee position exists beside the dismounted rider");
            feet.enabled=false;enemy.transform.position=meleeFooting;feet.enabled=true;
            enemy.ClearTransientCombatState();Physics.SyncTransforms();
            yield return WaitClock(.15f);
            Require(enemy.SupportedByWalkableGround,
                "swordsman stands on the same walkable surface as the rider before attacking (player="+
                rider.RiderPosition.ToString("F2")+", enemy="+enemy.transform.position.ToString("F2")+")");
            deadline=Clock+4;
            while(rider.Health==100&&Clock<deadline)yield return null;
            Require(rider.Health<100,"enemy melee damages rider (health="+rider.Health.ToString("F1")+
                ", distance="+Vector3.Distance(enemy.transform.position,rider.RiderPosition).ToString("F2")+
                ", visible="+enemy.RiderVisible+", supported="+enemy.SupportedByWalkableGround+
                ", player="+rider.RiderPosition.ToString("F2")+", enemy="+enemy.transform.position.ToString("F2")+
                ", grounded="+feet.isGrounded+", unsupportedTime="+enemy.UnsupportedTime.ToString("F2")+")");
            enemy.enabled=false;float health=rider.Health;
            enemy.GetComponent<CombatTarget>().Hit(100);enemy.enabled=true;
            yield return WaitClock(1.4f);
            Require(rider.Health==health,"defeated opponent stops attacking (before="+health.ToString("F1")+
                ", after="+rider.Health.ToString("F1")+", remainingArrows="+FindObjectsOfType<OpponentArrow>().Length+")");enemy.enabled=false;
            feet.enabled=false;enemy.transform.position=new Vector3(20,1,24);feet.enabled=true;
            var archer=opponents[1];var archerFeet=archer.GetComponent<CharacterController>();
            archerFeet.enabled=false;archer.transform.position=origin+Vector3.forward*5;
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

