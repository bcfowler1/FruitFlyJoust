using UnityEngine;
namespace FruitFlyJoust
{
    public sealed class WingBeat : MonoBehaviour
    {
        public int side;
        private FlyMotor motor;
        private float phase;
        void Start() { motor = GetComponentInParent<FlyMotor>(); }
        void Update()
        {
            bool resting=motor && (motor.Dead || motor.Phase==RidePhase.Perched);
            float speed=motor ? motor.FlightSpeed : 0;
            float speed01=Mathf.InverseLerp(.5f,8f,speed);
            if(!resting)phase=Mathf.Repeat(phase+Time.deltaTime*Mathf.Lerp(10,38,speed01),Mathf.PI*2);
            Quaternion pose = resting ? Quaternion.Euler(0, side * -25, 0) :
                Quaternion.Euler(0,side*-15,side*Mathf.Sin(phase)*Mathf.Lerp(10,28,speed01));
            transform.localRotation = resting ?
                Quaternion.Slerp(transform.localRotation, pose, 1 - Mathf.Exp(-12 * Time.deltaTime)) : pose;
        }
    }
}
