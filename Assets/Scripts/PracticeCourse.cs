using UnityEngine;

namespace FruitFlyJoust
{
    public sealed class PracticeCourse : MonoBehaviour
    {
        public FlyMotor fly;
        public Transform[] hoops;
        public Transform pillar;
        public Vector3 landingCenter;
        public int stage;
        private float previousAngle, orbit;
        private bool trackingOrbit;
        private Vector3 previousPosition;
        void Start() { previousPosition = fly.transform.position; }
        public void ResetCourse()
        { fly.ResetRide(); stage = 0; orbit = 0; trackingOrbit = false; previousPosition = fly.transform.position; }
        void Update()
        {
            Vector3 pos = fly.transform.position;
            if (fly.rider.resetRide)
            {
                ResetCourse(); return;
            }
            if (stage < 3)
            {
                Transform hoop = hoops[stage];
                Vector3 a = hoop.InverseTransformPoint(previousPosition);
                Vector3 b = hoop.InverseTransformPoint(pos);
                if (a.z * b.z < 0)
                {
                    Vector3 cross = Vector3.Lerp(a, b, -a.z / (b.z - a.z));
                    if (new Vector2(cross.x, cross.y).magnitude < 2.6f) stage++;
                }
            }
            else if (stage == 3)
            {
                Vector3 offset = pos - pillar.position;
                float distance = new Vector2(offset.x, offset.z).magnitude;
                float angle = Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg;
                if (distance > 3 && distance < 14)
                {
                    if (trackingOrbit) orbit += Mathf.DeltaAngle(previousAngle, angle);
                    trackingOrbit = true;
                    previousAngle = angle;
                    if (Mathf.Abs(orbit) >= 330) stage++;
                }
                else trackingOrbit = false;
            }
            else if (stage == 4 && fly.Phase == RidePhase.Perched &&
                Mathf.Abs(pos.x - landingCenter.x) < 4 && Mathf.Abs(pos.z - landingCenter.z) < 4 &&
                Mathf.Abs(pos.y - (landingCenter.y + 1)) < 1) stage++;
            previousPosition = pos;
        }
        void OnGUI()
        {
            GUI.color = new Color(.06f, .09f, .12f, .92f);
            GUI.DrawTexture(new Rect(16, 16, 650, 244), Texture2D.whiteTexture);
            GUI.color = Color.white;
            string objective = stage < 3 ? "Fly through hoop " + (stage + 1) + " of 3" : stage == 3 ?
                "Circle the tall pillar: " + Mathf.RoundToInt(Mathf.Abs(orbit)) + " / 330 degrees" :
                stage == 4 ? "Land on the green table (hold B / L)" : "Course complete! Backspace to ride again";
            GUI.Label(new Rect(30, 25, 580, 28), "FRUIT FLY JOUST — THE RIDE");
            GUI.Label(new Rect(30, 52, 580, 28), objective);
            GUI.Label(new Rect(30, 80, 620, 28), "Left stick: steer, pull back to climb / push forward to dive   Right stick: look");
            GUI.Label(new Rect(30, 104, 620, 28), "A: spur burst + cruising boost   B: slow / hold to land   R-stick click: recenter");
            GUI.Label(new Rect(30, 128, 580, 28), "Brain: " + (fly.senses.obstacleAhead > .2f ? "avoiding obstacle" : "following reins") +
                "   Input: " + fly.rider.controllerStatus);
            GUI.Label(new Rect(30, 152, 620, 28), "LB/RB: roll   Start: reset   Keyboard: Z/V roll, S climb, W dive, Space spur, L land");
            GUI.Label(new Rect(30, 176, 620, 28), "Reins " + fly.rider.reins.ToString("F2") + "   Look " + fly.rider.look.ToString("F2") +
                "   " + (Application.isFocused ? "Input active" : "Click Game view to enable input"));
            GUI.Label(new Rect(30, 200, 620, 28), fly.Phase == RidePhase.Perched ?
                "Perched on " + fly.SurfaceName + " — press A / Space to launch" : "Flight state: " + fly.Phase);
            GUI.Label(new Rect(30,224,620,28),"Fly hunger: "+Mathf.RoundToInt(fly.Hunger*100)+"%   "+(fly.SeekingFood ? "Seeking food — rider authority "+Mathf.RoundToInt(fly.RiderAuthority*100)+"%" : "Following rider"));
            GUI.Label(new Rect(Screen.width / 2 - 5, Screen.height / 2 - 10, 20, 20), "+");
        }
    }
}
