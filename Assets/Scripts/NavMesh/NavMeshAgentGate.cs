using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Keeps a NavMeshAgent disabled until NavMeshRuntimeBaker has published a valid surface.
/// Optionally warps onto the nearest mesh point before enabling to avoid the
/// "not close enough to the NavMesh" error.
/// </summary>
[DefaultExecutionOrder(1000)] // run after most Awake/OnEnable so we can disable agents early
public class NavMeshAgentGate : MonoBehaviour
{
    public float sampleMaxDistance = 5f;     // how far to search for a nearby navmesh point
    public bool warpOntoMesh = true;         // warp to sampled point before enabling
    public bool recheckEveryFrameUntilReady = true;

    private NavMeshAgent _agent;
    private bool _subscribed;

    private void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        if (_agent && _agent.enabled) _agent.enabled = false; // prevent early init errors
    }

    private void OnEnable()
    {
        TrySubscribe();
        TryEnableIfReady(); // in case baker is already done
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void Update()
    {
        if (recheckEveryFrameUntilReady && _agent && !_agent.enabled)
            TryEnableIfReady();
    }

    private void TrySubscribe()
    {
        var baker = NavMeshRuntimeBaker.Instance;
        if (baker == null || _subscribed) return;
        baker.OnBakeCompleted += OnBakeCompleted;
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        var baker = NavMeshRuntimeBaker.Instance;
        if (baker != null && _subscribed)
            baker.OnBakeCompleted -= OnBakeCompleted;
        _subscribed = false;
    }

    private void OnBakeCompleted()
    {
        TryEnableIfReady();
        Unsubscribe(); // we only care about the first publish; remove if you re-bake during play
    }

    private void TryEnableIfReady()
    {
        if (_agent == null || _agent.enabled) return;

        // Only proceed if there is at least one navmesh loaded
        // (NavMesh.CalculateTriangulation().vertices.Length > 0 is another option)
        if (!NavMeshRuntimeBaker.Instance || !NavMeshRuntimeBaker.Instance.BakeCompleted)
            return;

        if (warpOntoMesh)
        {
            var pos = transform.position;
            if (NavMesh.SamplePosition(pos, out var hit, sampleMaxDistance, NavMesh.AllAreas))
            {
                // Put agent safely onto the mesh
                _agent.Warp(hit.position);
            }
            // If no sample found, we still enable; designer can increase sampleMaxDistance or adjust spawn height
        }

        _agent.enabled = true;
    }
}
