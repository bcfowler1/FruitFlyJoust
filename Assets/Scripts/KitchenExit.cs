using UnityEngine;
using UnityEngine.SceneManagement;

namespace FruitFlyJoust
{
    public sealed class KitchenExit : MonoBehaviour
    {
        KitchenLevel level;
        void Awake() { level = FindObjectOfType<KitchenLevel>(); }
        void OnTriggerEnter(Collider other)
        {
            if (!other.GetComponentInParent<FlyMotor>() || !level) return;
            level.exitReached = true;
            if (Application.CanStreamedLevelBeLoaded(level.nextScene))
                SceneManager.LoadScene(level.nextScene);
        }
    }
}
