using UnityEngine;
using UnityEngine.UI;

public class HealthBar : MonoBehaviour
{
    public Image fillImage;   // Assign in prefab
    private Transform target;

    public void AttachTo(Transform targetTransform, Vector3 offset)
    {
        target = targetTransform;
        transform.SetParent(null); // detach from prefab parent
        this.offset = offset;
    }

    private Vector3 offset;

    void LateUpdate()
    {
        if (target != null)
        {
            transform.position = target.position + offset;
            transform.rotation = Camera.main.transform.rotation; // always face camera
        }
    }

    public void UpdateHealth(float current, float max)
    {
        if (fillImage != null)
            fillImage.fillAmount = current / max;
    }
}
