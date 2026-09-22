using UnityEngine;

namespace FruitFlyJoust
{
    public sealed class KitchenHazard : MonoBehaviour
    {
        public enum Kind { TapWater, Disposal, Stove, WaterTrap, Steam }
        public Kind kind;
        public float damagePerSecond = 8f;
        float nextTick;

        void OnTriggerStay(Collider other)
        {
            if (Time.time < nextTick) return;
            var combat = FindObjectOfType<RiderCombat>();
            if (!combat || combat.Defeated) return;
            bool flyContact = other.GetComponentInParent<FlyMotor>() == combat.fly;
            bool riderContact = !combat.Mounted && other.gameObject.name == "Dismounted rider";
            if (!flyContact && !riderContact) return;
            bool damaged = flyContact && combat.Mounted
                ? combat.TakeFlyDamage(damagePerSecond * .25f)
                : riderContact && combat.TakeDamage(damagePerSecond * .25f);
            if (damaged) nextTick = Time.time + .25f;
        }
    }
}
