using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FruitFlyJoust
{
    // A short in-Editor smoke test of real trigger damage and the scene transition.
    public sealed class KitchenLevelPlayCheck : MonoBehaviour
    {
        bool arrived;
        bool steamDamaged;
        void Awake()
        {
            DontDestroyOnLoad(gameObject);
            SceneManager.sceneLoaded += OnSceneLoaded;
        }
        void OnDestroy() { SceneManager.sceneLoaded -= OnSceneLoaded; }

        IEnumerator Start()
        {
            yield return new WaitForSeconds(.2f);
            var fly = FindObjectOfType<FlyMotor>();
            var combat = FindObjectOfType<RiderCombat>();
            var level = FindObjectOfType<KitchenLevel>();
            var steam = GameObject.Find("Scalding teapot steam hazard");
            if (!fly || !combat || !level || !steam) { Finish(false, "kitchen gameplay objects missing"); yield break; }
            var body = fly.GetComponent<Rigidbody>();
            fly.enabled = false;
            body.velocity = Vector3.zero;
            body.isKinematic = true;
            float initialHealth = combat.PlayerFlyHealth.Health;
            body.position = steam.transform.position;
            Physics.SyncTransforms();
            yield return new WaitForSeconds(.7f);
            steamDamaged = combat.PlayerFlyHealth.Health < initialHealth;
            body.position = new Vector3(0, 5.4f, -18.3f);
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();
            body.position = new Vector3(0, 5.4f, -19.65f);
            Physics.SyncTransforms();
            for (int i = 0; i < 45 && !arrived; i++) yield return new WaitForFixedUpdate();
            Finish(arrived && steamDamaged, "steamDamage=" + steamDamaged + " nextRoom=" + arrived);
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == "KitchenLevel2")
                arrived = FindObjectOfType<KitchenNextRoom>() && FindObjectOfType<RiderCombat>() &&
                          FindObjectOfType<FlyMotor>();
        }

        void Finish(bool passed, string detail)
        {
            string report = "{\"status\":\"" + (passed ? "passed" : "failed") +
                            "\",\"details\":\"" + detail + "\"}";
            File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath,
                "../Research/kitchen-play-evaluation.json")), report);
            if (passed) Debug.Log("KITCHEN_PLAY_CHECK_PASSED " + detail);
            else Debug.LogError("KITCHEN_PLAY_CHECK_FAILED " + detail);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }
    }
}
