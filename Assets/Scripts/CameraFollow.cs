using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    public Transform target; // The player's Transform
    public Vector3 offset;  // The offset distance between the camera and the player
    public float smoothSpeed = 0.125f; // How smoothly the camera moves

    private void LateUpdate()
    {
        if (target == null)
        {
            Debug.LogWarning("CameraFollow: No target assigned!");
            return;
        }

        Vector3 desiredPosition = target.position + offset; // Calculate the desired camera position

        // Smoothly move the camera towards the desired position
        Vector3 smoothedPosition = Vector3.Lerp(transform.position, desiredPosition, smoothSpeed);
        transform.position = smoothedPosition;
    }
}
