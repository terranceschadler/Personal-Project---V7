using UnityEngine;

public class MinimapFollow : MonoBehaviour
{
    public Transform player;   // drag your Player here in Inspector
    public float height = 20f; // how high above the player the camera stays

    void LateUpdate()
    {
        // Follow player position only
        Vector3 newPos = player.position;
        newPos.y += height;
        transform.position = newPos;

        // Always look straight down
        transform.rotation = Quaternion.Euler(90f, 0f, 0f);
    }
}
