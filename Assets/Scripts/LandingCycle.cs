using System;

namespace FruitFlyJoust
{
    public enum RidePhase { Flying, Landing, Perched, Launching }

    public sealed class LandingCycle
    {
        public RidePhase Phase { get; private set; }
        private float launchTime;
        private bool waitForLandingRelease;

        public RidePhase Tick(bool land, bool spur, bool supported, bool settled, float dt)
        {
            if (!land) waitForLandingRelease = false;
            if (Phase == RidePhase.Perched && !supported) Phase = RidePhase.Flying;
            if (spur && (Phase == RidePhase.Perched || Phase == RidePhase.Landing))
            {
                Phase = RidePhase.Launching;
                launchTime = .65f;
                waitForLandingRelease = true;
            }
            else if (Phase == RidePhase.Launching)
            {
                launchTime -= Math.Max(0, dt);
                if (launchTime <= 0) Phase = RidePhase.Flying;
            }
            else if (Phase != RidePhase.Perched)
            {
                Phase = land && !waitForLandingRelease ? RidePhase.Landing : RidePhase.Flying;
                if (Phase == RidePhase.Landing && supported && settled) Phase = RidePhase.Perched;
            }
            return Phase;
        }

        public void Reset() { Phase = RidePhase.Flying; launchTime = 0; waitForLandingRelease = false; }
        public void ResumeFlight()
        {
            if (Phase == RidePhase.Landing) Phase = RidePhase.Flying;
            launchTime = 0;
            waitForLandingRelease = false;
        }
        public void ForceFlight()
        {
            Phase=RidePhase.Flying;launchTime=0;waitForLandingRelease=true;
        }
    }
}
