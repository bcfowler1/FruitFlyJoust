using UnityEngine;
namespace FruitFlyJoust
{
    public sealed class WingBeat : MonoBehaviour
    {
        public int side;
        private FlyMotor motor;
        void Start() { motor = GetComponentInParent<FlyMotor>(); }
        void Update()
        {
            Quaternion pose = motor && motor.Phase == RidePhase.Perched ? Quaternion.Euler(0, side * -25, 0) :
                Quaternion.Euler(0, side * -15, side * Mathf.Sin(Time.time * 95) * 22);
            transform.localRotation = motor && motor.Phase == RidePhase.Perched ?
                Quaternion.Slerp(transform.localRotation, pose, 1 - Mathf.Exp(-12 * Time.deltaTime)) : pose;
        }
    }
}
