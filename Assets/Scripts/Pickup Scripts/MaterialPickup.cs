using UnityEngine;

public class MaterialPickup : MonoBehaviour
{
    [Header("Settings")]
    public int materialValue = 1;
    public float rotateSpeed = 90f;
    public float groundCheckDistance = 5f;

    private void Start()
    {
        SnapToGround();
    }

    private void Update()
    {
        transform.Rotate(Vector3.up, rotateSpeed * Time.deltaTime, Space.World);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            GameManager.Instance.AddMaterials(materialValue);
            Destroy(gameObject);
        }
    }

    private void SnapToGround()
    {
        RaycastHit hit;
        if (Physics.Raycast(transform.position + Vector3.up, Vector3.down, out hit, groundCheckDistance, LayerMask.GetMask("Default")))
        {
            transform.position = hit.point;
            transform.up = Vector3.up;
        }
    }
}
