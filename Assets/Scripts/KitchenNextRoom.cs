using UnityEngine;

namespace FruitFlyJoust
{
    public sealed class KitchenNextRoom : MonoBehaviour
    {
        void OnGUI()
        {
            GUI.color = new Color(.07f, .08f, .07f, .91f);
            GUI.DrawTexture(new Rect(Screen.width / 2 - 245, 20, 490, 56), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(Screen.width / 2 - 228, 37, 470, 30),
                "Level 2 arrival — this room is ready for its own design.");
        }
    }
}
