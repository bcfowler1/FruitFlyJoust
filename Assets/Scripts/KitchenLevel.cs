using UnityEngine;

namespace FruitFlyJoust
{
    // The world is deliberately enlarged for fly-scale play: one room foot is four Unity units.
    public sealed class KitchenLevel : MonoBehaviour
    {
        public int seed = 1049;
        public const float UnitsPerFoot = 4f;
        public const float RoomWidth = 8f * UnitsPerFoot;
        public const float CeilingHeight = 12f * UnitsPerFoot;
        public string nextScene = "KitchenLevel2";
        [HideInInspector] public string layoutSignature;
        public bool exitReached;

        void OnGUI()
        {
            if (!exitReached) return;
            GUI.color = new Color(.08f, .07f, .06f, .9f);
            GUI.DrawTexture(new Rect(Screen.width / 2 - 225, 30, 450, 52), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(Screen.width / 2 - 205, 44, 425, 30), "Kitchen complete — through the lock to the next room");
        }
    }

}
