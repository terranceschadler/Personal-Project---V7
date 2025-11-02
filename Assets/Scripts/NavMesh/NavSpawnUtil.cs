using UnityEngine;
using UnityEngine.AI;

public static class NavSpawnUtil
{
    /// <summary>
    /// Finds a nearby NavMesh point and snaps it to ground colliders (so Y matches the surface).
    /// Returns true on success, with groundedPos set.
    /// </summary>
    public static bool TryGetGroundedNavmeshPosition(
        Vector3 desired,
        float sampleRadius,
        LayerMask groundMask,
        out Vector3 groundedPos,
        float verticalProbeHeight = 5f,
        float extraDowncast = 10f)
    {
        groundedPos = desired;

        // 1) Find nearest navmesh position
        if (!NavMesh.SamplePosition(desired, out var hit, sampleRadius, NavMesh.AllAreas))
            return false;

        var pos = hit.position;

        // 2) Raycast down to actual ground collider so the Y is correct
        float topY = Mathf.Max(pos.y, desired.y) + verticalProbeHeight;
        Vector3 rayStart = new Vector3(pos.x, topY, pos.z);
        float rayLen = verticalProbeHeight + extraDowncast;

        if (Physics.Raycast(rayStart, Vector3.down, out var groundHit, rayLen, groundMask, QueryTriggerInteraction.Ignore))
        {
            pos.y = groundHit.point.y;
        }
        else
        {
            // Fallback: small spherecast to catch edges
            if (Physics.SphereCast(rayStart, 0.2f, Vector3.down, out groundHit, rayLen, groundMask, QueryTriggerInteraction.Ignore))
                pos.y = groundHit.point.y;
            // If still no hit, keep navmesh Y
        }

        groundedPos = pos;
        return true;
    }

    /// <summary>
    /// Safely instantiates a prefab with NavMeshAgent, places it on a grounded navmesh point, and warps the agent.
    /// Returns the instance or null if placement failed.
    /// </summary>
    public static GameObject SpawnAgentGrounded(
        GameObject prefab,
        Vector3 desired,
        float sampleRadius,
        LayerMask groundMask,
        Quaternion? rotOverride = null)
    {
        if (!TryGetGroundedNavmeshPosition(desired, sampleRadius, groundMask, out var pos))
            return null;

        // rot is a Quaternion (no .Value)
        var rot = rotOverride ?? Quaternion.identity;

        var go = Object.Instantiate(prefab, pos, rot);

        var agent = go.GetComponent<NavMeshAgent>();
        if (agent != null)
        {
            if (!agent.enabled) agent.enabled = true;
            agent.baseOffset = 0f;
            agent.Warp(pos);
        }

        var cc = go.GetComponent<CharacterController>();
        if (cc != null)
        {
            cc.enabled = false;
            go.transform.position = pos;
            cc.enabled = true;
        }

        return go;
    }
}
