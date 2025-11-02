using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SpawnCollisionDetector : MonoBehaviour
{

    private void OnTriggerStay(Collider other)
    {
        Destroy(gameObject);    // destroy self
        
    }

}
