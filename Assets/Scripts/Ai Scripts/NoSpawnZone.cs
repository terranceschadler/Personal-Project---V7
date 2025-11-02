using UnityEngine;

[DisallowMultipleComponent]
public class NoSpawnZone : MonoBehaviour
{
    [Tooltip("Optional radius around this tile where enemies cannot spawn.")]
    public float radius = 5f;

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, radius);
    }
}
