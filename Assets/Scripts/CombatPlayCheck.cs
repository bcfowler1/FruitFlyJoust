#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace FruitFlyJoust
{
    [DefaultExecutionOrder(-50)]
    public sealed class CombatPlayCheck : MonoBehaviour
    {
        private RiderCombat combat;
        private bool requestLanding;
        private bool requestLance;
        private float rollRequest;
        private float turnRequest;
        private Vector2 walkingRequest;
        private bool climbRequest;
        private int checks;
        private float deadline;
        static Transform Part(Transform root,string name)
        { foreach(Transform child in root) if(child.name==name)return child; return null; }
        void Require(bool passed, string label)
        {
            if (!passed) { deadline = 0; WriteReport("failed: " + label); UnityEditor.EditorApplication.isPlaying = false;
                throw new Exception("Combat check failed: " + label); }
            checks++; Debug.Log("COMBAT_CHECK: " + label);
        }
        void Update()
        {
            if (requestLanding && combat) { combat.fly.rider.land = true; combat.fly.rider.reins = Vector2.zero; }
            if (requestLance && combat) combat.fly.rider.primaryAction = 1;
            if(combat && rollRequest!=0)combat.fly.rider.roll=rollRequest;
            if(combat && turnRequest!=0)combat.fly.rider.reins=new Vector2(turnRequest,0);
            if(combat && walkingRequest!=Vector2.zero)combat.fly.rider.reins=walkingRequest;
            if(combat && climbRequest)combat.fly.rider.reins=new Vector2(0,-1);
            if (deadline > 0 && Time.time > deadline)
            { Debug.LogError("COMBAT_CHECK_TIMEOUT"); WriteReport("failed: timeout"); UnityEditor.EditorApplication.isPlaying = false; }
        }
        void CaptureSeatViews()
        {
            var cameraObject=new GameObject("Seat fit inspection camera");
            var camera=cameraObject.AddComponent<Camera>();camera.fieldOfView=35;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.18f,.22f,.28f);
            var target=combat.fly.transform.position+combat.fly.transform.up*.05f;
            var texture=new RenderTexture(1000,1000,24);camera.targetTexture=texture;
            foreach(var side in new[]{Vector3.right,Vector3.forward})
            {
                camera.transform.position=target+combat.fly.transform.TransformDirection(side)*3.8f+combat.fly.transform.up*.35f;
                camera.transform.LookAt(target,combat.fly.transform.up);camera.Render();
                var previous=RenderTexture.active;RenderTexture.active=texture;
                var pixels=new Texture2D(1000,1000,TextureFormat.RGB24,false);pixels.ReadPixels(new Rect(0,0,1000,1000),0,0);pixels.Apply();RenderTexture.active=previous;
                File.WriteAllBytes(Path.Combine(Application.dataPath,"../Research/rider-seat-"+(side==Vector3.right ? "side" : "front")+".rgb"),pixels.GetRawTextureData());Destroy(pixels);
            }
            camera.targetTexture=null;texture.Release();Destroy(texture);Destroy(cameraObject);
        }
        IEnumerator Start()
        {
            deadline = Time.time + 30;
            combat = FindObjectOfType<RiderCombat>();
            combat.course.ResetCourse();
            yield return new WaitForSeconds(.2f);
            Require(combat && combat.FootAvatar, "runtime rider created");
            var seat=combat.fly.GetComponent<RiderAnimationVisual>();
            Require(seat && Mathf.Abs(seat.mountedSeatHeight+.33f)<.001f && Mathf.Abs(seat.mountedSeatForward+.16f)<.001f && Mathf.Abs(seat.visualScale-.78f)<.001f,"detailed mounted seat settings applied after rider creation");
            var wallFrame=new GameObject("Temporary wall rider frame");wallFrame.transform.rotation=Quaternion.Euler(0,0,90);
            seat.StabilizeTorsoAgainstGravity(wallFrame.transform);
            Require(seat.LastTorsoStabilizationDegrees>50 && seat.LastTorsoStabilizationDegrees<=55,
                "mounted torso partially counters gravity on a 90-degree wall while pelvis remains surface-aligned");
            Destroy(wallFrame);
            yield return new WaitForSeconds(.25f);
            CaptureSeatViews();
            var detail=combat.fly.GetComponent<DetailedFlyVisual>();
            var meshRoot=combat.fly.transform.Find("Detailed NeuroMechFly appearance");
            Require(detail && meshRoot && meshRoot.GetComponentsInChildren<MeshRenderer>().Length==69,"detailed 69-part NeuroMechFly mesh loaded");
            Require(detail.UsesMeasuredWingCycle,"flight wings use the published measured three-axis FMech cycle");
            Require(detail.UsesSpectralWingMaterial,"both biomodel wings use the translucent spectral material with enhanced veins");
            Require(detail.UsesSpectralEyeMaterial,"both biomodel eyes retain red while using the spectral orange sheen material");
            Transform head=null;foreach(Transform part in meshRoot)if(part.name=="0/Head")head=part;
            Require(head,"articulated head mesh present");Quaternion headBefore=head.localRotation;
            Transform eye=Part(meshRoot,"0/LEye"),antenna=Part(meshRoot,"0/LPedicel");
            Require(eye && antenna,"head eye and antenna meshes present");
            Vector3 eyeOffset=head.InverseTransformPoint(eye.position),antennaOffset=head.InverseTransformPoint(antenna.position);
            float maximumHeadAttachmentError=0,minimumHeadAxisAgreement=1;
            Transform leftWing=Part(meshRoot,"0/LWing");Require(leftWing,"left wing mesh present");Quaternion wingBefore=leftWing.localRotation;
            float maximumWingHingeError=0,maximumWingMotion=0,maximumWingDifferential=0;
            float maximumHeadMotion=0;float rollEnd=Time.time+.6f;
            rollRequest=1;
            while(Time.time<rollEnd)
            {
                yield return null;maximumWingHingeError=Mathf.Max(maximumWingHingeError,detail.MaximumWingHingeError());maximumHeadMotion=Mathf.Max(maximumHeadMotion,Quaternion.Angle(headBefore,head.localRotation));
                maximumWingMotion=Mathf.Max(maximumWingMotion,Quaternion.Angle(wingBefore,leftWing.localRotation));maximumWingDifferential=Mathf.Max(maximumWingDifferential,detail.WingSteeringDifferential);
                maximumHeadAttachmentError=Mathf.Max(maximumHeadAttachmentError,
                    Vector3.Distance(eyeOffset,head.InverseTransformPoint(eye.position)),
                    Vector3.Distance(antennaOffset,head.InverseTransformPoint(antenna.position)));
                Quaternion delta=head.localRotation*Quaternion.Inverse(headBefore);
                delta.ToAngleAxis(out float angle,out Vector3 axis);
                if(Quaternion.Angle(headBefore,head.localRotation)>.1f)
                    minimumHeadAxisAgreement=Mathf.Min(minimumHeadAxisAgreement,Mathf.Abs(Vector3.Dot(axis.normalized,Vector3.up)));
            }
            rollRequest=0;
            Require(maximumWingHingeError<.0001f,"both animated wings keep their native hinge fixed");
            Require(maximumWingMotion>35,"measured wing cycle produces a full multi-axis flight stroke");
            Require(Mathf.Abs(DetailedFlyVisual.CalculateWingFlapsPerSecond(RidePhase.Flying,0)-12)<.01f &&
                Mathf.Abs(DetailedFlyVisual.CalculateWingFlapsPerSecond(RidePhase.Flying,8)-38)<.01f,
                "flight-speed wing mapping uses the tightened twelve-to-thirty-eight flap range");
            Require(Mathf.Abs(DetailedFlyVisual.CalculateWingFlapsPerSecond(RidePhase.Landing,0)-10.85f)<.01f,
                "near-stationary landing preserves the previous slow 10.85-flap settling rate");
            Require(maximumWingDifferential>.2f,"banking produces left-right wing amplitude asymmetry");
            Require(Vector3.Dot(combat.fly.transform.up,Vector3.up)<.65f && Mathf.Abs(combat.fly.senses.lift)<.01f,"shoulder cue rolls body without commanding climb");
            Require(seat.LastTorsoStabilizationDegrees>20,"airborne shoulder roll makes rider torso counter gravity");
            Require(combat.view.TrackingRiderHead,"mounted camera tracks the final gravity-corrected rider orientation");
            Require(combat.view.TrackingStableRiderFrame,"flight camera uses the stable gravity-corrected rider frame without animation jitter");
            Require(maximumHeadMotion<.05f,"head geometry remains in its authored connected pose");
            Require(maximumHeadAttachmentError<.001f,"eyes and antenna remain rigidly attached");
            float releasedBank=Vector3.Angle(combat.fly.transform.up,Vector3.up);
            yield return new WaitForSeconds(.8f);
            Require(Vector3.Angle(combat.fly.transform.up,Vector3.up)<releasedBank*.8f,"released shoulder roll gently returns toward level");
            turnRequest=1;float naturalBankDeadline=Time.time+.7f;float automaticTorsoCorrection=0;
            while(Time.time<naturalBankDeadline){yield return null;automaticTorsoCorrection=Mathf.Max(automaticTorsoCorrection,seat.LastTorsoStabilizationDegrees);}
            turnRequest=0;
            Require(Mathf.Abs(combat.fly.NaturalBankDegrees)>10,"ordinary steering banks the complete fly and saddle frame");
            Require(automaticTorsoCorrection>6,"turn-induced bank applies the same gravity-upright torso correction as shoulder roll");
            Transform leg=Part(meshRoot,"0/LFTibia");Require(leg,"leg mesh present");Quaternion flightLeg=leg.localRotation;
            yield return new WaitForSeconds(.3f);
            Require(Quaternion.Angle(flightLeg,leg.localRotation)<6.1f,"biomodel flight-leg animation remains bounded");
            Require(detail.MaximumFlightPoseError()<.001f,"all flight-leg segments exactly match the captured biomodel pose");
            CaptureFlightAppearance();
            combat.course.ResetCourse();yield return new WaitForSeconds(.2f);
            Require(!combat.TryDismount() && combat.Mounted, "airborne dismount refused");
            requestLanding = true;
            while (combat.fly.Phase != RidePhase.Perched) yield return null;
            requestLanding = false;
            bool autonomousIdleBeforeCameraCheck=combat.fly.autonomousIdle;
            combat.fly.autonomousIdle=false;
            // Allow the deliberate one-way surface-frame blend to finish before
            // measuring periodic camera drift. Sampling after .4 s mistook normal
            // landing convergence for oscillation.
            yield return new WaitForSeconds(1.5f);
            Require(combat.view.TrackingStableFlyFrame,"mounted perched camera ignores looping rider-head motion");
            Quaternion perchedCamera=combat.view.transform.rotation;
            yield return new WaitForSeconds(.35f);
            Require(Quaternion.Angle(perchedCamera,combat.view.transform.rotation)<.15f,"stationary mounted perch camera does not oscillate");
            Transform restingMiddleLeg=Part(meshRoot,"0/LMTibia");Require(restingMiddleLeg,"middle leg mesh present");
            Quaternion restLeg=restingMiddleLeg.localRotation;
            yield return new WaitForSeconds(.25f);
            Require(Quaternion.Angle(restLeg,restingMiddleLeg.localRotation)<.2f,"stationary perched support legs rest while forelegs may groom");
            combat.fly.autonomousIdle=autonomousIdleBeforeCameraCheck;
            Transform rightLeg=Part(meshRoot,"0/RFTibia");Require(rightLeg,"right foreleg mesh present");
            Quaternion idleHeadStart=head.localRotation,idleLegStart=leg.localRotation,idleRightLegStart=rightLeg.localRotation;
            Vector3 idleEyeOffset=head.InverseTransformPoint(eye.position),idleAntennaOffset=head.InverseTransformPoint(antenna.position);
            float idleHeadMotion=0,leftGroomMotion=0,rightGroomMotion=0,idleAttachmentError=0;
            float groomingDeadline=Time.time+2.8f;
            while(Time.time<groomingDeadline)
            {
                yield return null;
                idleHeadMotion=Mathf.Max(idleHeadMotion,Quaternion.Angle(idleHeadStart,head.localRotation));
                leftGroomMotion=Mathf.Max(leftGroomMotion,Quaternion.Angle(idleLegStart,leg.localRotation));
                rightGroomMotion=Mathf.Max(rightGroomMotion,Quaternion.Angle(idleRightLegStart,rightLeg.localRotation));
                idleAttachmentError=Mathf.Max(idleAttachmentError,
                    Vector3.Distance(idleEyeOffset,head.InverseTransformPoint(eye.position)),
                    Vector3.Distance(idleAntennaOffset,head.InverseTransformPoint(antenna.position)));
            }
            Require(idleHeadMotion>2f && idleHeadMotion<11,
                "perched cleaning applies a visible bounded side-to-side head tilt");
            Require(idleAttachmentError<.001f,
                "idle head tilt keeps facial geometry connected");
            Require(leftGroomMotion>8 && rightGroomMotion>8,
                "perched idle alternates complete left and right foreleg chains for grooming");
            Vector3 walkStart=combat.fly.transform.position;
            float restingHunger=combat.fly.CalculateHungerPerMinute(false,0,0,0,0);
            float cruisingHunger=combat.fly.CalculateHungerPerMinute(true,4,0,0,0);
            float acrobaticHunger=combat.fly.CalculateHungerPerMinute(true,8,85,2,combat.fly.rollDegreesPerSecond);
            Require(cruisingHunger>restingHunger && acrobaticHunger>cruisingHunger,"flight, speed, turning, climbing, and rolling progressively increase hunger");
            float acrobaticFillSeconds=(1-.15f)/acrobaticHunger*60;
            Require(acrobaticFillSeconds>85 && acrobaticFillSeconds<95,"fast acrobatic flight fills the reset hunger meter in about ninety seconds");
            var foodObject=GameObject.CreatePrimitive(PrimitiveType.Sphere);foodObject.name="Mounted walking food check";
            foodObject.transform.position=walkStart+combat.fly.transform.forward*.25f;foodObject.transform.localScale=Vector3.one*.2f;
            var food=foodObject.AddComponent<FlyFood>();food.nutrition=.5f;int meals=combat.fly.FoodEatenCount;combat.fly.SetHunger(.8f);float originalFoodScale=foodObject.transform.localScale.x;
            walkingRequest=new Vector2(.4f,1);
            yield return new WaitForSeconds(.5f);
            Require(Vector3.Distance(walkStart,combat.fly.transform.position)>.1f && combat.fly.Phase==RidePhase.Perched && combat.fly.SurfaceWalkingSpeed>.1f,"grounded stick steers and walks without launching");
            Require(combat.fly.FoodEatenCount==meals+1 && combat.fly.Hunger<.8f && food.RemainingFraction<1 && food.RemainingFraction>.7f && foodObject.transform.localScale.x<originalFoodScale,
                "mounted fly feeds gradually and visibly reduces food while walking over it");
            Destroy(foodObject);
            combat.fly.SetHunger(.95f);yield return new WaitForFixedUpdate();
            Require(combat.fly.SeekingFood && combat.fly.RiderAuthority<.2f,"very hungry fly overrides the reins and seeks available food");
            combat.fly.SetHunger(.15f);
            float playerFlyHealth=combat.PlayerFlyHealth.Health;
            Require(combat.TakeFlyDamage(20) && combat.PlayerFlyHealth.Health<playerFlyHealth,"player fly has separate damageable health");
            combat.PlayerFlyHealth.ResetTarget();
            walkingRequest=Vector2.zero;combat.fly.rider.reins=Vector2.zero;
            yield return new WaitForSeconds(.4f);
            Vector3 stopped=combat.fly.transform.position;
            yield return new WaitForSeconds(.2f);
            Require(Vector3.Distance(stopped,combat.fly.transform.position)<.01f,"grounded stick release stops walking");
            Vector3 mountedRiderPosition=combat.RenderedRiderPosition;
            Require(combat.TryDismount() && !combat.Mounted, "safe dismount from actual perch");
            Require(combat.LastDismountLateral<-.7f,"player dismounts on the forward-facing fly's left side when that foothold is clear");
            Require(combat.RiderTransitioning && Vector3.Distance(mountedRiderPosition,combat.RenderedRiderPosition)<.03f,
                "left dismount begins with the rendered rider continuously at the saddle instead of teleporting");
            Require(combat.LastDroppedLance && combat.LastDroppedLance.useGravity,
                "dismount drops the full lance as a physical object instead of making it vanish");
            var idleFoodObject=GameObject.CreatePrimitive(PrimitiveType.Sphere);idleFoodObject.name="Dismounted autonomous feeding check";
            idleFoodObject.transform.position=combat.fly.transform.position+combat.fly.transform.forward*.45f;idleFoodObject.transform.localScale=Vector3.one*.24f;
            var idleFood=idleFoodObject.AddComponent<FlyFood>();idleFood.nutrition=.6f;combat.fly.SetHunger(.5f);
            yield return new WaitForSeconds(1.05f);
            Require(combat.fly.FoodEatenCount>meals+1 && idleFood.RemainingFraction<.72f && idleFood.RemainingFraction>.65f && combat.fly.Hunger<.35f,
                "nearby dismounted fly autonomously feeds for one second and consumes thirty percent of remaining food");
            var nutritionNumber=FindObjectOfType<FloatingNutritionNumber>();
            Require(nutritionNumber && nutritionNumber.Amount>1,"feeding shows accumulated positive nutrition above the fly");
            Destroy(idleFoodObject);combat.fly.SetHunger(.15f);
            yield return new WaitForSeconds(.3f);
            Require(combat.FootAvatar.GetComponent<CharacterController>().isGrounded, "foot controller on floor");
            yield return new WaitForSeconds(.8f);
            Require(combat.view.FrameTiltDegrees<3,"on-foot camera levels against gravity after dismount");
            Quaternion onFootCamera=combat.view.transform.rotation;
            combat.FootAvatar.rotation=Quaternion.Euler(0,combat.FootAvatar.eulerAngles.y+180,0);
            yield return null;
            Require(!combat.view.followAnchorRotation && Quaternion.Angle(onFootCamera,combat.view.transform.rotation)<.5f,"on-foot facing change does not pivot camera");
            Vector3 restingFly=combat.fly.transform.position;
            yield return new WaitForSeconds(2.6f);
            Require(Vector3.Distance(restingFly,combat.fly.transform.position)>.02f && Vector3.Distance(restingFly,combat.fly.transform.position)<.8f && combat.fly.Phase==RidePhase.Perched,"dismounted fly takes a bounded surface walk");
            combat.FootAvatar.position+=Vector3.right*3;Physics.SyncTransforms();
            Require(combat.CallFlyNear() && combat.fly.RecallActive,"dismounted rider can whistle for the fly");
            float recallDeadline=Time.time+12;
            while(combat.fly.RecallActive && Time.time<recallDeadline)yield return null;
            Require(!combat.fly.RecallActive && combat.fly.Phase==RidePhase.Perched &&
                Vector3.Distance(combat.FootAvatar.position,combat.fly.transform.position)<2.8f,
                "called fly approaches, lands, and stops within mounting range (active="+combat.fly.RecallActive+
                ", phase="+combat.fly.Phase+", distance="+Vector3.Distance(combat.FootAvatar.position,combat.fly.transform.position).ToString("F2")+")");
            var target = FindObjectOfType<CombatTarget>();
            int otherIndex = 0;
            foreach (var other in FindObjectsOfType<CombatTarget>())
                if (other != target) other.transform.position = new Vector3(20, 1, 20 + otherIndex++ * 2);
            Vector3 position = combat.FootAvatar.position;
            combat.view.enabled = false;
            combat.view.transform.position = position + Vector3.up * .9f - Vector3.forward * 4;
            combat.view.transform.rotation = Quaternion.identity;
            target.transform.position = position + Vector3.up + Vector3.forward * 1.2f;
            Physics.SyncTransforms(); combat.SelectWeapon(RiderCombat.Weapon.Sword);
            Require(combat.SwordVisual && combat.SwordVisual.gameObject.activeSelf &&
                combat.SwordVisual.GetComponentsInChildren<MeshRenderer>().Length==5,
                "sword uses separate blade, point, crossguard, grip, and pommel geometry");
            combat.SwordStrike(); Require(target.Health == 100, "melee waits for animation windup");
            float meleeDeadline=Time.time+.4f;while(target.Health==100 && Time.time<meleeDeadline)yield return null;
            Require(target.Health == 75, "melee collision damages target after windup (health="+target.Health+")");
            Require(target.LastDamage==25 && FindObjectOfType<FloatingDamageNumber>(),"target hit produces a visible floating damage number");
            combat.SwordStrike(); Require(target.Health == 75, "melee cooldown prevents duplicate hit");
            yield return new WaitForSeconds(.5f);
            combat.SwordStrike(RiderCombat.SwordAttack.LeftToRight);
            Require(combat.LastSwordAttack==RiderCombat.SwordAttack.LeftToRight,"X sword command selects left-to-right cut");
            yield return new WaitForSeconds(.13f);float leftCutStart=combat.SwordTipLateral;
            yield return new WaitForSeconds(.32f);float leftCutEnd=combat.SwordTipLateral;
            Require(leftCutEnd-leftCutStart>.45f,"left-to-right cut carries the sword tip across the rider from left to right");
            yield return new WaitForSeconds(.15f);
            combat.SwordStrike(RiderCombat.SwordAttack.RightToLeft);
            Require(combat.LastSwordAttack==RiderCombat.SwordAttack.RightToLeft,"B sword command selects right-to-left cut");
            yield return new WaitForSeconds(.13f);float rightCutStart=combat.SwordTipLateral;
            yield return new WaitForSeconds(.32f);float rightCutEnd=combat.SwordTipLateral;
            Require(rightCutStart-rightCutEnd>.45f,"right-to-left cut carries the sword tip across the rider from right to left");
            yield return new WaitForSeconds(.15f);
            Vector3 beforeLunge=combat.FootAvatar.position;
            Vector3 beforeThrustHandle=combat.SwordVisual.position;
            combat.SwordStrike(RiderCombat.SwordAttack.Thrust);
            Require(combat.LastSwordAttack==RiderCombat.SwordAttack.Thrust,"right-trigger sword command selects thrust");
            float minimumForwardAlignment=1,maximumGripError=0;float thrustUntil=Time.time+.25f;
            while(Time.time<thrustUntil)
            {
                yield return null;
                maximumGripError=Mathf.Max(maximumGripError,combat.SwordGripError);
                if(combat.SwordThrustExtension>.1f)minimumForwardAlignment=Mathf.Min(minimumForwardAlignment,combat.SwordForwardAlignment);
            }
            Require(Vector3.Dot(combat.SwordVisual.position-beforeThrustHandle,combat.FootAvatar.forward)>.12f,
                "sword thrust advances the rider, hand, and weapon directly forward together");
            Require(maximumGripError<.001f,"sword hand remains attached to the authored hilt position during thrust");
            Require(minimumForwardAlignment>.98f,"sword thrust keeps the blade aligned with the forward attack axis");
            Require(Vector3.Distance(beforeLunge,combat.FootAvatar.position)>.08f,"sword thrust advances the grounded rider with a bounded lunge");
            // The smoothed thrust now has a .55 s recovery; wait through it so the
            // subsequent bow release tests arrow flight rather than cooldown rejection.
            yield return new WaitForSeconds(.35f);target.ResetTarget();
            target.transform.position = position + Vector3.up + Vector3.forward * 6;
            Physics.SyncTransforms(); combat.SelectWeapon(RiderCombat.Weapon.Bow);
            Require(combat.BowVisual && combat.BowVisual.gameObject.activeSelf &&
                combat.BowVisual.GetComponentsInChildren<LineRenderer>().Length==2,
                "bow uses curved limbs and a visible string instead of the weapon-box placeholder");
            combat.ReleaseArrow(1);
            yield return new WaitForSeconds(.7f);
            Require(target.Health == 55, "on-foot full-draw arrow deals 45 damage to a reset target");
            var hoverMountCycle=new LandingCycle();
            hoverMountCycle.Tick(true,false,true,false,.02f);
            Require(hoverMountCycle.Phase==RidePhase.Landing,"hover mount regression begins while recall is descending");
            hoverMountCycle.ResumeFlight();
            Require(hoverMountCycle.Phase==RidePhase.Flying,"hover mount immediately returns the landing cycle to rider-controlled flight");
            Vector3 walkingRiderPosition=combat.RenderedRiderPosition;
            Require(combat.TryMount() && combat.Mounted, "deliberate remount near perched fly");
            Require(combat.RiderTransitioning && Vector3.Distance(walkingRiderPosition,combat.RenderedRiderPosition)<.03f,
                "mount animation begins continuously from the rider's walking position");
            Require(!combat.fly.RecallActive,"remount cancels recall landing control before returning authority to the rider");
            climbRequest=true;
            yield return new WaitForFixedUpdate();yield return new WaitForFixedUpdate();
            Require(combat.fly.senses.lift>.9f && combat.fly.intent.climb>3.5f,
                "remount hands climb control back to the rider instead of retaining recall landing or braking");
            climbRequest=false;combat.fly.rider.reins=Vector2.zero;
            Require(combat.mountedVisual.gameObject.activeSelf, "mounted rider restored");
            target.ResetTarget(); Require(target.Health == 100, "target reset");
            combat.SelectWeapon(RiderCombat.Weapon.Bow);
            Require(combat.StowedLanceVisible,
                "switching weapons while mounted keeps the lance visibly secured between saddle and armour");
            combat.view.transform.rotation=Quaternion.LookRotation(combat.fly.transform.forward,combat.fly.transform.up);
            combat.fly.rider.secondaryAction=1;yield return new WaitForSeconds(.45f);
            Require(seat.LastWaistAimDegrees>80 && seat.LastWaistAimDegrees<100,
                "forward archery stance turns the rider ninety degrees right at the waist");
            Require(seat.LeftArmAimAlignment>.75f && Vector3.Dot(combat.BowVisual.forward,combat.fly.transform.forward)>.95f,
                "side-on archery stance keeps the left arm and bow aimed in the forward firing direction");
            Require(seat.BowDrawHandDistance>.42f,
                "holding the left trigger keeps the right hand pulling the arrow behind the bow");
            combat.view.transform.rotation=Quaternion.LookRotation(
                (-combat.fly.transform.forward+combat.fly.transform.right*.25f).normalized,combat.fly.transform.up);
            yield return null;yield return null;
            Require(Mathf.Abs(seat.LastWaistAimDegrees)>90,"bow aiming behind twists the rider at the waist toward aim direction");
            Require(seat.LeftArmAimAlignment>.75f && Vector3.Dot(combat.BowVisual.forward,combat.view.transform.forward)>.95f,
                "bow aim extends the left arm and places the bow in the firing direction");
            combat.fly.rider.secondaryAction=0;
            combat.view.transform.position = combat.fly.transform.position + Vector3.up * .9f - Vector3.forward * 4;
            combat.view.transform.rotation = Quaternion.identity;
            target.transform.position = combat.fly.transform.position + Vector3.up * .9f + Vector3.forward * 6;
            Physics.SyncTransforms();
            combat.ReleaseArrow(1); yield return new WaitForSeconds(.7f);
            Require(target.Health == 55, "mounted arrow flight damages target");
            target.ResetTarget();
            target.transform.position = new Vector3(20, 1, 28);
            Physics.SyncTransforms(); combat.SelectWeapon(RiderCombat.Weapon.Lance);
            requestLance = true; combat.fly.rider.RequestSpur();
            yield return new WaitForSeconds(1);
            Vector3 velocity = combat.fly.GetComponent<Rigidbody>().velocity;
            Require(velocity.magnitude > 3, "mounted fly moving for lance test");
            target.transform.position = combat.LanceTip + velocity.normalized * .8f;
            Physics.SyncTransforms();
            float joustDeadline = Time.time + 6;
            while (target.Health == 100 && Time.time < joustDeadline) yield return null;
            requestLance = false;
            Require(target.Health < 100, "moving mounted lance collision damages target");
            Require(target.LastDamage>0,"lance contact records positive target damage");
            Require(combat.ForceUnseat(Vector3.up*1.5f+combat.fly.transform.right*2),"solid enemy lance contact can unseat the mounted player");
            Require(combat.RiderRagdolled && combat.RiderRagdollBodyCount>=10,
                "unseated player uses a jointed humanoid bone ragdoll rather than the controller capsule");
            float fallDeadline=Time.time+6;while(!combat.FootAvatar.GetComponent<CharacterController>().isGrounded && Time.time<fallDeadline)yield return null;
            yield return null;
            Require(!combat.Mounted && !combat.Defeated && combat.LastFallDamage<=30,
                "player fall damage is bounded and normally survivable for continued ground combat");
            Require(!combat.RiderRagdolled,"surviving player recovers from ragdoll after landing");
            Require(combat.weapon==RiderCombat.Weapon.Sword,"unseated player continues combat on foot with sword");
            combat.view.enabled = true;
            deadline = 0; WriteReport("passed"); Debug.Log("COMBAT_PLAY_CHECKS_PASSED: " + checks);
            UnityEditor.EditorApplication.isPlaying = false;
        }
        void CaptureFlightAppearance()
        {
            var cameraObject=new GameObject("Isolated flight-pose inspection camera");
            var camera=cameraObject.AddComponent<Camera>();
            camera.fieldOfView=32;camera.clearFlags=CameraClearFlags.SolidColor;
            camera.backgroundColor=new Color(.18f,.22f,.28f);camera.nearClipPlane=.01f;camera.farClipPlane=100;
            var detailRoot=combat.fly.transform.Find("Detailed NeuroMechFly appearance");
            var renderers=detailRoot ? detailRoot.GetComponentsInChildren<Renderer>() : combat.fly.GetComponentsInChildren<Renderer>();
            Bounds bounds=renderers.Length>0 ? renderers[0].bounds : new Bounds(combat.fly.transform.position,Vector3.one);
            foreach(var renderer in renderers)if(renderer.enabled)bounds.Encapsulate(renderer.bounds);
            var sceneRenderers=FindObjectsOfType<Renderer>();var hidden=new System.Collections.Generic.List<Renderer>();
            foreach(var renderer in sceneRenderers)
                if(renderer.enabled && (!detailRoot || !renderer.transform.IsChildOf(detailRoot))){renderer.enabled=false;hidden.Add(renderer);}
            var target=new RenderTexture(1000,1000,24);camera.targetTexture=target;
            string[] names={"right","left","front","rear","top","bottom"};
            Vector3[] directions={combat.fly.transform.right,-combat.fly.transform.right,combat.fly.transform.forward,-combat.fly.transform.forward,combat.fly.transform.up,-combat.fly.transform.up};
            for(int i=0;i<names.Length;i++)
            {
                camera.transform.position=bounds.center+directions[i]*Mathf.Max(1.2f,bounds.extents.magnitude*2.8f);
                Vector3 cameraUp=Mathf.Abs(Vector3.Dot(directions[i].normalized,combat.fly.transform.up))>.9f ? combat.fly.transform.forward : combat.fly.transform.up;
                camera.transform.LookAt(bounds.center,cameraUp);camera.Render();
                RenderTexture.active=target;var image=new Texture2D(1000,1000,TextureFormat.RGB24,false);
                image.ReadPixels(new Rect(0,0,1000,1000),0,0);image.Apply();
                File.WriteAllBytes(Path.Combine(Application.dataPath,"../Research/playable-flight-"+names[i]+".rgb"),image.GetRawTextureData());Destroy(image);
            }
            RenderTexture.active=null;camera.targetTexture=null;target.Release();Destroy(target);
            foreach(var renderer in hidden)if(renderer)renderer.enabled=true;
            Destroy(cameraObject);
        }
        void WriteReport(string status)
        { File.WriteAllText(Path.Combine(Application.dataPath, "../Research/combat-play-evaluation.json"),
            "{\"status\":\"" + status + "\",\"checks\":" + checks + ",\"method\":\"Unity Play-mode physical scene checks\"}"); }
    }
}
#endif


