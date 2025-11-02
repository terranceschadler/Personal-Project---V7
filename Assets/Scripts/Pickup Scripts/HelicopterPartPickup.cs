using UnityEngine;

public class HelicopterPartPickup : MonoBehaviour
{
    [Header("Part Info")]
    public string partName = "HelicopterPart";

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        //Debug.Log($"[HelicopterPickup] Player collected helicopter part: {partName}");
        if (GameManager.Instance != null)
        {
            // Add part to the GameManager inventory
            GameManager.Instance.AddHelicopterPart(partName);

            // Force the HUD/UI to refresh right away
            GameManager.Instance.RebroadcastHelicopterProgress();
        }

        Destroy(gameObject);
    }
}
