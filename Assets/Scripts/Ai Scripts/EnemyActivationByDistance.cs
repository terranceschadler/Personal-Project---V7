using UnityEngine;
using UnityEngine.AI;

public class EnemyActivationByDistance : MonoBehaviour
{
    private NavMeshAgent agent;
    private EnemyController controller;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        controller = GetComponent<EnemyController>();
    }

    public void SetActive(bool value)
    {
        if (value)
        {
            // Warp/snap to nearest NavMesh BEFORE enabling
            if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 5f, NavMesh.AllAreas))
            {
                if (agent) agent.enabled = false; // disable first
                transform.position = hit.position; // snap to navmesh
                if (agent) agent.Warp(hit.position); // reset internal nav state
                if (agent) agent.enabled = true;    // safe to enable
            }
            else
            {
                Debug.LogWarning($"{name} could not find NavMesh at activation position!");
            }

            gameObject.SetActive(true);
        }
        else
        {
            // disable agent before pooling
            if (agent) agent.enabled = false;
            gameObject.SetActive(false);
        }
    }
}
