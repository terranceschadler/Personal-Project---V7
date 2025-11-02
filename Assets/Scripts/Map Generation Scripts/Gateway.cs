using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Gateway : MonoBehaviour
{
    private Collider _myTrigger;

    private void Start()
    {
        _myTrigger = GetComponent<Collider>();
        _myTrigger.isTrigger = true;
        GetComponent<MeshRenderer>().enabled = false;
    }


}
