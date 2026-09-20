using UnityEngine;
namespace FruitFlyJoust
{
    public sealed class PlaceholderBrain : FlyBrain
    {
        private float turn;
        private readonly FlightPace pace = new FlightPace();
        public override FlyIntent Tick(FlySenses s, float dt)
        {
            float requested = s.rightRein - s.leftRein;
            float avoidance = (s.obstacleLeft - s.obstacleRight) * 1.8f;
            if (s.obstacleAhead > .25f && Mathf.Abs(avoidance) < .1f)
                avoidance = 1.1f * s.obstacleAhead;
            turn = Mathf.Lerp(turn, Mathf.Clamp(requested + avoidance, -1, 1), 1 - Mathf.Exp(-4 * dt));
            return new FlyIntent
            {
                turn = turn,
                speed = pace.Tick(s.spur, s.brake, dt) * (1 - .8f * s.obstacleAhead),
                climb = s.lift * 4 + s.obstacleAhead * 2,
                land = s.land
            };
        }
        public override void ResetBrain() { turn = 0; pace.Reset(); }
    }
}

