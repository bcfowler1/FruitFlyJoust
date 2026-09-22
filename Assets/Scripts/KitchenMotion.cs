using UnityEngine;

namespace FruitFlyJoust
{
    public sealed class KitchenMotion : MonoBehaviour
    {
        public enum Motion { CeilingFan, Disposal, WaterDrop, SteamPuff }
        public Motion motion;
        public float speed = 1;
        public float travel = 2;
        Vector3 start;

        void Awake() { start = transform.localPosition; }
        void Update()
        {
            if (motion == Motion.SteamPuff)
            {
                float height = Mathf.Repeat(Time.time * speed + start.y, travel);
                transform.localPosition = new Vector3(start.x + Mathf.Sin(Time.time * 2 + start.y) * .14f,
                    start.y + height, start.z);
            }
            else if (motion == Motion.WaterDrop)
            {
                float distance = Mathf.Repeat(Time.time * speed + start.y, travel);
                transform.localPosition = new Vector3(start.x, start.y - distance, start.z);
            }
            else transform.Rotate(Vector3.up, speed * Time.deltaTime, Space.Self);
        }
    }
}
