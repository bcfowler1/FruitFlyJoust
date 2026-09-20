using UnityEngine;

namespace FruitFlyJoust
{
    public struct FlySenses
    {
        public float leftRein, rightRein, lift;
        public float obstacleLeft, obstacleRight, obstacleAhead;
        public bool spur, brake, land;
    }

    public struct FlyIntent
    {
        public float turn, speed, climb;
        public bool land;
    }

    // Neural backends can replace this component without changing the motor or rider.
    public abstract class FlyBrain : MonoBehaviour
    {
        public abstract FlyIntent Tick(FlySenses senses, float dt);
        public virtual void ResetBrain() { }
    }

}

