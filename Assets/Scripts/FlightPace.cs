using System;

namespace FruitFlyJoust
{
    // Speed requests are separate from pitch/rein signals and physical acceleration.
    public sealed class FlightPace
    {
        public float Cruise { get; private set; } = 5;
        private float burst;
        public float Tick(bool spurPressed, bool brakePressed, float dt)
        {
            if (brakePressed)
            {
                Cruise = Math.Max(0, Cruise - 2);
                burst = 0;
            }
            else if (spurPressed)
            {
                Cruise = Math.Min(13, Cruise + 2);
                burst = 5;
            }
            float requested = Cruise + burst;
            burst *= (float)Math.Exp(-Math.Max(0, dt) / .7);
            return requested;
        }
        public void Reset() { Cruise = 5; burst = 0; }
        public static float ClimbRequest(float stickForward, float additionalLift)
        { return Math.Max(-1, Math.Min(1, -stickForward + additionalLift)); }
    }
}
