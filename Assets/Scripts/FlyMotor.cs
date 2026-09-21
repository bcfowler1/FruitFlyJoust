using UnityEngine;

namespace FruitFlyJoust
{
    [RequireComponent(typeof(Rigidbody))]
    public sealed class FlyMotor : MonoBehaviour
    {
        public Transform bodyVisual;
        public RiderInput rider;
        public FlyBrain brain;
        public bool grounded;
        public FlySenses senses;
        public FlyIntent intent;
        private Rigidbody rb;
        private float heading;
        private float rollAngle;
        public float rollDegreesPerSecond = 110;
        public float rollLevelReturnSeconds = 1.5f;
        public bool autonomousIdle = true;
        public bool IdleWalking { get; private set; }
        public float SurfaceWalkingSpeed { get; private set; }
        public float SurfaceWalkingDirection { get { return IdleWalking ? 1 : Mathf.Sign(surfaceDrive); } }
        private float surfaceDrive;
        private float idleClock;
        private Vector3 idleHome;
        private bool wasDismounted;
        private float speed;
        private readonly LandingCycle landing = new LandingCycle();
        private Collider perchSurface;
        private Vector3 perchLocalPosition;
        private Quaternion perchLocalRotation;
        private Vector3 launchNormal = Vector3.up;
        private CapsuleCollider capsule;
        private float cornerGripGrace;
        private bool recallActive,recallLanding;
        private Vector3 recallTarget;
        public bool RecallActive { get { return recallActive; } }
        [Range(0,1)] public float hunger=.15f;
        public float hungerPerMinute=.12f;
        public float flightHungerPerMinute=.08f;
        public float speedHungerPerMeterPerMinute=.018f;
        public float turnHungerPerDegreePerMinute=.0012f;
        public float climbHungerPerMeterPerMinute=.035f;
        public float rollHungerPerDegreePerMinute=.0008f;
        public float Hunger { get { return hunger; } }
        public float CurrentHungerPerMinute { get; private set; }
        public bool SeekingFood { get; private set; }
        public float RiderAuthority { get; private set; }=1;
        public int FoodEatenCount { get; private set; }
        FlyFood foodTarget;
        public bool Dead { get; private set; }
        public float FlightSpeed { get { return rb ? rb.velocity.magnitude : 0; } }
        public RidePhase Phase { get { return landing.Phase; } }
        public string SurfaceName { get { return perchSurface ? perchSurface.name : ""; } }
        [Min(.5f)] public float surfaceProbeDistance = 5;

        bool FindSurface(out RaycastHit surface)
        {
            surface = default(RaycastHit);
            if (Phase == RidePhase.Perched && perchSurface)
            {
                Vector3 normal = perchSurface.transform.rotation * perchLocalRotation * Vector3.up;
                if (!Physics.Raycast(rb.position, -normal, out surface, surfaceProbeDistance, 1,
                    QueryTriggerInteraction.Ignore) || surface.collider != perchSurface) return false;
                return cornerGripGrace>0 || HasFootprint(surface);
            }
            Vector3[] directions = { Vector3.down, Vector3.up, transform.forward, -transform.forward,
                transform.right, -transform.right, rb.velocity.normalized };
            float nearest = float.PositiveInfinity;
            bool found = false;
            if(rider.land && Physics.Raycast(rb.position,-transform.up,out var feetSurface,surfaceProbeDistance,1,QueryTriggerInteraction.Ignore) && HasFootprint(feetSurface))
            { surface=feetSurface;return true; }
            foreach (Vector3 direction in directions)
            {
                if (direction.sqrMagnitude < .01f || !Physics.Raycast(rb.position, direction, out var candidate,
                    surfaceProbeDistance, 1, QueryTriggerInteraction.Ignore) || candidate.distance >= nearest ||
                    !HasFootprint(candidate)) continue;
                surface = candidate; nearest = candidate.distance; found = true;
            }
            return found;
        }

        bool HasFootprint(RaycastHit surface)
        { return HasFootprint(surface, rb.position); }

        bool HasFootprint(RaycastHit surface, Vector3 position)
        {
            Quaternion pose = SurfaceGeometry.Pose(transform.forward, surface.normal);
            Vector3[] offsets = { pose * Vector3.right * .4f, pose * Vector3.left * .4f,
                pose * Vector3.forward * .65f, pose * Vector3.back * .65f };
            foreach (Vector3 offset in offsets)
                if (!Physics.Raycast(position + offset, -surface.normal, out var edge, surfaceProbeDistance, 1,
                    QueryTriggerInteraction.Ignore) || edge.collider != surface.collider ||
                    Vector3.Dot(edge.normal, surface.normal) < .95f ||
                    Mathf.Abs(Vector3.Dot(edge.point - surface.point, surface.normal)) > .25f) return false;
            return true;
        }

        void Perch(RaycastHit surface)
        {
            Quaternion pose = SurfaceGeometry.Pose(transform.forward, surface.normal);
            Vector3 target = surface.point + surface.normal * (capsule.radius + .025f);
            perchSurface = surface.collider;
            perchLocalPosition = perchSurface.transform.InverseTransformPoint(target);
            perchLocalRotation = Quaternion.Inverse(perchSurface.transform.rotation) * pose;
            rb.velocity = Vector3.zero;
            rb.MovePosition(target);
            rb.MoveRotation(pose);
            speed = 0;
        }

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
            capsule = GetComponent<CapsuleCollider>();
            rb.useGravity = false;
            rb.constraints = RigidbodyConstraints.FreezeRotation;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            heading = transform.eulerAngles.y;
            if(!GetComponent<DetailedFlyVisual>())gameObject.AddComponent<DetailedFlyVisual>();
        }
        void Start(){EnsureFoodBits();}
        void EnsureFoodBits()
        {
            if(FindObjectOfType<FlyFood>())return;
            Vector3[] offsets={new Vector3(-8,0,-5),new Vector3(7,0,-2),new Vector3(-13,0,11),new Vector3(12,0,10),new Vector3(2,0,17),new Vector3(-18,0,-14)};
            foreach(var offset in offsets)
            {
                Vector3 origin=new Vector3(offset.x,18,offset.z);
                if(!Physics.Raycast(origin,Vector3.down,out var ground,30,1,QueryTriggerInteraction.Ignore))continue;
                var bit=GameObject.CreatePrimitive(PrimitiveType.Sphere);bit.name="Fly food bit";bit.transform.position=ground.point+ground.normal*.18f;bit.transform.localScale=Vector3.one*.28f;
                var material=new Material(Shader.Find("Standard"));material.color=new Color(.95f,.55f,.08f);bit.GetComponent<Renderer>().sharedMaterial=material;bit.AddComponent<FlyFood>();
            }
        }
        FlyFood NearestFood()
        {
            FlyFood nearest=null;float distance=float.PositiveInfinity;
            foreach(var food in FindObjectsOfType<FlyFood>())if(food.Available)
            {float candidate=(food.transform.position-rb.position).sqrMagnitude;if(candidate<distance){distance=candidate;nearest=food;}}
            return nearest;
        }

        float Obstacle(Vector3 direction)
        {
            // Cast from beyond our own capsule; the environment is on layer 0.
            return Physics.SphereCast(transform.position, .35f, direction, out var hit, 5, 1,
                QueryTriggerInteraction.Ignore) ? 1 - hit.distance / 5 : 0;
        }

        void FixedUpdate()
        {
            if(Dead){rb.velocity=Vector3.zero;return;}
            float dt = Time.fixedDeltaTime;
            bool airborne=Phase==RidePhase.Flying || Phase==RidePhase.Launching || Phase==RidePhase.Landing;
            float turnDegreesPerSecond=airborne ? Mathf.Abs(intent.turn)*85 : 0;
            float climbMetersPerSecond=airborne ? Mathf.Abs(intent.climb) : 0;
            float rollRateDegrees=airborne ? Mathf.Abs(rider.roll)*rollDegreesPerSecond : 0;
            CurrentHungerPerMinute=CalculateHungerPerMinute(airborne,rb.velocity.magnitude,turnDegreesPerSecond,climbMetersPerSecond,rollRateDegrees);
            hunger=Mathf.Clamp01(hunger+CurrentHungerPerMinute/60*dt);
            cornerGripGrace=Mathf.Max(0,cornerGripGrace-dt);
            Vector2 controlReins=rider.reins;float controlLift=rider.lift;
            bool recallSpur=false,recallBrake=false;
            if(!foodTarget || !foodTarget.Available)foodTarget=NearestFood();
            float foodDistance=foodTarget ? Vector3.Distance(rb.position,foodTarget.transform.position) : float.PositiveInfinity;
            if(foodTarget && foodDistance<.75f)
            {hunger=Mathf.Max(0,hunger-foodTarget.nutrition);foodTarget.Eat();FoodEatenCount++;foodTarget=null;SeekingFood=false;}
            RiderAuthority=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.55f,.9f,hunger));
            SeekingFood=foodTarget && hunger>=.62f;
            if(SeekingFood)
            {
                Vector3 toFood=foodTarget.transform.position-rb.position;Vector3 planar=Vector3.ProjectOnPlane(toFood,Vector3.up);
                float turn=planar.sqrMagnitude>.01f ? Vector3.SignedAngle(transform.forward,planar.normalized,Vector3.up) : 0;
                Vector2 forage=new Vector2(Mathf.Clamp(turn/40,-1,1),planar.magnitude>1 ? .65f : 0);
                float forageLift=Mathf.Clamp((foodTarget.transform.position.y+(planar.magnitude>2 ? 1.5f : .35f)-rb.position.y)*.8f,-1,1);
                controlReins=Vector2.Lerp(forage,controlReins,RiderAuthority);controlLift=Mathf.Lerp(forageLift,controlLift,RiderAuthority);
                recallSpur=Phase==RidePhase.Perched && foodDistance>1;recallBrake=foodDistance<2;
            }
            if(recallActive && !SeekingFood)
            {
                Vector3 toTarget=recallTarget-rb.position;
                Vector3 planar=Vector3.ProjectOnPlane(toTarget,Vector3.up);
                float distance=planar.magnitude;
                if(Phase==RidePhase.Perched)
                {
                    if(distance<1.6f){recallActive=recallLanding=false;}
                    else recallSpur=true;
                }
                if(recallActive && Phase!=RidePhase.Perched)
                {
                    float turn=planar.sqrMagnitude>.01f ? Vector3.SignedAngle(transform.forward,planar.normalized,Vector3.up) : 0;
                    controlReins=new Vector2(Mathf.Clamp(turn/45,-1,1),distance>1.4f ? .7f : 0);
                    float desiredHeight=recallTarget.y+(distance>2 ? 2.2f : .75f);
                    controlLift=Mathf.Clamp((desiredHeight-rb.position.y)*.8f,-1,1);
                    recallLanding=distance<1.25f;
                    recallBrake=distance<2.5f;
                }
            }
            senses = new FlySenses
            {
                leftRein = Mathf.Max(0, -controlReins.x), rightRein = Mathf.Max(0, controlReins.x),
                lift = FlightPace.ClimbRequest(controlReins.y, controlLift),
                spur = rider.ConsumeSpur() || recallSpur, brake = rider.ConsumeBrake() || recallBrake, land = rider.land && RiderAuthority>.25f || recallLanding,
                obstacleLeft = Obstacle(Quaternion.Euler(0, -35, 0) * transform.forward),
                obstacleRight = Obstacle(Quaternion.Euler(0, 35, 0) * transform.forward),
                obstacleAhead = Obstacle(transform.forward)
            };
            intent = brain.Tick(senses, dt);
            bool supported = FindSurface(out var surface);
            float clearance = supported ? SurfaceGeometry.Clearance(capsule, surface.normal) : .525f;
            float gap = supported ? Vector3.Dot(rb.position - surface.point, surface.normal) - clearance : float.PositiveInfinity;
            grounded = supported && gap < .15f;
            bool settled = grounded && gap > -.1f && rb.velocity.magnitude < 1.2f &&
                Quaternion.Angle(rb.rotation, SurfaceGeometry.Pose(transform.forward, surface.normal)) < 12;
            RidePhase before = Phase;
            landing.Tick(intent.land, senses.spur, supported, settled, dt);
            if (before != RidePhase.Launching && Phase == RidePhase.Launching)
            {
                launchNormal = before == RidePhase.Perched ? rb.rotation * Vector3.up :
                    supported ? surface.normal : Vector3.up;
                Vector3 flatForward = Vector3.ProjectOnPlane(rb.rotation * Vector3.forward, Vector3.up);
                if (flatForward.sqrMagnitude > .01f) heading = Mathf.Atan2(flatForward.x, flatForward.z) * Mathf.Rad2Deg;
                rb.velocity = launchNormal * 3.5f;
            }
            if (Phase == RidePhase.Perched)
            {
                if (before != RidePhase.Perched) Perch(surface);
                else if (perchSurface)
                {
                    rb.velocity = Vector3.zero;
                    rb.MovePosition(perchSurface.transform.TransformPoint(perchLocalPosition));
                    rb.MoveRotation(perchSurface.transform.rotation * perchLocalRotation);
                }
                grounded = true;
                SurfaceWalkingSpeed=0;
                var combat=GetComponent<RiderCombat>();bool dismounted=combat && !combat.Mounted;
                if(dismounted && !wasDismounted){idleHome=rb.position;idleClock=0;}
                wasDismounted=dismounted;IdleWalking=false;
                if(!dismounted && perchSurface)
                {
                    surfaceDrive=Mathf.MoveTowards(surfaceDrive,rider.braking ? 0 : controlReins.y*.8f,dt*3);
                    Quaternion pose=perchSurface.transform.rotation*perchLocalRotation;
                    pose=Quaternion.AngleAxis(controlReins.x*95*dt,pose*Vector3.up)*pose;
                    perchLocalRotation=Quaternion.Inverse(perchSurface.transform.rotation)*pose;
                    rb.MoveRotation(pose);
                    Vector3 step=rb.position+pose*Vector3.forward*surfaceDrive*dt;
                    if(Physics.Raycast(step,-(pose*Vector3.up),out var ground,surfaceProbeDistance,1,QueryTriggerInteraction.Ignore) && ground.collider==perchSurface && HasFootprint(ground,step))
                    { perchLocalPosition=perchSurface.transform.InverseTransformPoint(step);rb.MovePosition(step);SurfaceWalkingSpeed=Mathf.Abs(surfaceDrive); }
                    else
                    {
                        float sign=Mathf.Sign(surfaceDrive);
                        Quaternion travelPose=sign>=0 ? pose : pose*Quaternion.Euler(0,180,0);
                        float clearanceAtCorner=SurfaceGeometry.Clearance(capsule,pose*Vector3.up);
                        if(SurfaceGeometry.FindRightAngleSurface(rb.position,travelPose,perchSurface,
                            clearanceAtCorner,.28f,out var corner))
                        {
                            Quaternion nextPose=SurfaceGeometry.CornerPose(travelPose,corner.normal);
                            if(sign<0)nextPose=nextPose*Quaternion.Euler(0,180,0);
                            Vector3 target=corner.point+corner.normal*SurfaceGeometry.Clearance(capsule,corner.normal);
                            perchSurface=corner.collider;perchLocalPosition=perchSurface.transform.InverseTransformPoint(target);
                            perchLocalRotation=Quaternion.Inverse(perchSurface.transform.rotation)*nextPose;
                            rb.MovePosition(target);rb.MoveRotation(nextPose);cornerGripGrace=.4f;
                            SurfaceWalkingSpeed=Mathf.Abs(surfaceDrive);
                        }
                        else surfaceDrive=0;
                    }
                }
                if(dismounted && autonomousIdle && perchSurface)
                {
                    idleClock+=dt;
                    if(idleClock%4>2 && idleClock%4<2.5f && Vector3.Distance(rb.position,idleHome)<.65f)
                    {
                        Vector3 step=rb.position+transform.forward*.4f*dt;
                        if(Physics.Raycast(step,-transform.up,out var ground,surfaceProbeDistance,1,QueryTriggerInteraction.Ignore) && ground.collider==perchSurface && HasFootprint(ground,step))
                        { perchLocalPosition=perchSurface.transform.InverseTransformPoint(step);rb.MovePosition(step);IdleWalking=true;SurfaceWalkingSpeed=.4f; }
                    }
                }
                if (bodyVisual) bodyVisual.localRotation = Quaternion.Slerp(bodyVisual.localRotation,
                    Quaternion.identity, 1 - Mathf.Exp(-8 * dt));
                return;
            }
            perchSurface = null;
            SurfaceWalkingSpeed=0;surfaceDrive=0;IdleWalking=false;
            Vector3 recallPlanar=Vector3.ProjectOnPlane(recallTarget-rb.position,Vector3.up);
            bool recallCruise=recallActive && !recallLanding && Phase!=RidePhase.Launching;
            if(recallCruise && recallPlanar.sqrMagnitude>.01f)
            {
                float wanted=Mathf.Atan2(recallPlanar.x,recallPlanar.z)*Mathf.Rad2Deg;
                heading=Mathf.MoveTowardsAngle(heading,wanted,140*dt);
            }
            else heading += intent.turn * 85 * dt;
            bool approaching = Phase == RidePhase.Landing;
            if (!approaching)
            {
                if(Mathf.Abs(rider.roll)>.01f)rollAngle=Mathf.Repeat(rollAngle+rider.roll*rollDegreesPerSecond*dt+180,360)-180;
                else rollAngle=Mathf.LerpAngle(rollAngle,0,1-Mathf.Exp(-dt/Mathf.Max(.1f,rollLevelReturnSeconds)));
            }
            Quaternion targetRotation = approaching && supported ? SurfaceGeometry.Pose(transform.forward, surface.normal) :
                Quaternion.Euler(0, heading, 0)*Quaternion.AngleAxis(rollAngle,Vector3.forward);
            rb.MoveRotation(Quaternion.RotateTowards(rb.rotation, targetRotation, 220 * dt));
            speed = Mathf.Lerp(speed, approaching ? 0 : intent.speed,
                1 - Mathf.Exp(-(approaching ? 4 : senses.spur ? 10 : senses.brake ? 6 : 2) * dt));
            float vertical = approaching ? -Mathf.Min(2, supported ? Mathf.Max(.15f, gap * 2) : 2) : intent.climb;
            if (Phase == RidePhase.Launching) vertical = 3.5f;
            if (grounded && !approaching && surface.normal.y > .9f && vertical < 0) vertical = 0;
            if (grounded && !approaching && !rider.braking && surface.normal.y > .9f && (rider.spur || senses.lift > .1f)) vertical = 3;
            Vector3 desired = Quaternion.Euler(0, heading, 0) * Vector3.forward * speed + Vector3.up * vertical;
            if(recallCruise && recallPlanar.sqrMagnitude>.01f)
            {
                float distance=recallPlanar.magnitude;
                float desiredHeight=recallTarget.y+(distance>2 ? 2.2f : .75f);
                float recallVertical=Mathf.Clamp((desiredHeight-rb.position.y)*2,-3,3);
                desired=recallPlanar.normalized*Mathf.Clamp(distance*1.4f,1,4)+Vector3.up*recallVertical;
            }
            if (approaching && supported)
                desired = Vector3.ProjectOnPlane(Quaternion.Euler(0, heading, 0) * Vector3.forward * speed,
                    surface.normal) * Mathf.Clamp01(gap / 2) -
                    surface.normal * Mathf.Min(2, Mathf.Max(.12f, gap * 2));
            if (Phase == RidePhase.Launching)
                desired = launchNormal * 3.5f + Vector3.ProjectOnPlane(desired, launchNormal) * .35f;
            rb.velocity = Vector3.Lerp(rb.velocity, desired, 1 - Mathf.Exp(-5 * dt));
            if (bodyVisual)
                bodyVisual.localRotation = Quaternion.Slerp(bodyVisual.localRotation,
                    approaching ? Quaternion.identity : Quaternion.Euler(-vertical * 3, 0, -intent.turn * 35),
                    1 - Mathf.Exp(-6 * dt));
        }

        public void ResetRide()
        {
            ReviveAfterDeath();
            rb.position = new Vector3(0, 1.2f, -18);
            rb.rotation = Quaternion.identity;
            rb.velocity = Vector3.zero;
            speed = 0;
            heading = 0;
            rollAngle = 0;
            idleClock=0;wasDismounted=IdleWalking=false;
            landing.Reset();
            perchSurface = null;
            grounded = false;
            launchNormal = Vector3.up;
            cornerGripGrace=0;
            recallActive=recallLanding=false;
            hunger=.15f;SeekingFood=false;RiderAuthority=1;foodTarget=null;
            brain.ResetBrain();
            rider.ResetCues();
        }
        public bool RequestRecall(Vector3 groundTarget)
        {
            if(Dead)return false;
            if(float.IsNaN(groundTarget.x)||float.IsNaN(groundTarget.y)||float.IsNaN(groundTarget.z))return false;
            recallTarget=groundTarget;recallActive=true;recallLanding=false;idleClock=0;return true;
        }
        public void SetHunger(float value){hunger=Mathf.Clamp01(value);foodTarget=null;}
        public float CalculateHungerPerMinute(bool flying,float flightSpeed,float turnDegreesPerSecond,float climbMetersPerSecond,float rollDegreesPerSecond)
        {
            float rate=Mathf.Max(0,hungerPerMinute);if(!flying)return rate;
            return rate+Mathf.Max(0,flightHungerPerMinute)+Mathf.Max(0,flightSpeed)*Mathf.Max(0,speedHungerPerMeterPerMinute)+
                Mathf.Max(0,turnDegreesPerSecond)*Mathf.Max(0,turnHungerPerDegreePerMinute)+
                Mathf.Max(0,climbMetersPerSecond)*Mathf.Max(0,climbHungerPerMeterPerMinute)+
                Mathf.Max(0,rollDegreesPerSecond)*Mathf.Max(0,rollHungerPerDegreePerMinute);
        }
        public FlyCorpse SpawnCorpse(Vector3 inheritedVelocity)
        {
            if(Dead)return null;Dead=true;
            Transform source=transform.Find("Detailed NeuroMechFly appearance");if(!source)source=bodyVisual;
            var corpse=FlyCorpse.Create(source,inheritedVelocity,true);if(bodyVisual)bodyVisual.gameObject.SetActive(false);rb.velocity=Vector3.zero;return corpse;
        }
        public void ReviveAfterDeath(){Dead=false;if(bodyVisual)bodyVisual.gameObject.SetActive(true);}
    }
}
