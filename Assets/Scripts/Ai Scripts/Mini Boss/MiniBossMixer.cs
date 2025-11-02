// Assets/Scripts/Ai Scripts/MiniBossMixer.cs
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
public class MiniBossMixer : MonoBehaviour
{
    [Header("References")]
    public Transform player;                       // If empty, will try to find by tag "Player"
    [Tooltip("Pick randomly from these. Each prefab should include a MiniBoss component.")]
    public GameObject[] miniBossPrefabs;

    [Header("Spawn Cadence")]
    [Min(0.5f)] public float minInterval = 12f;
    [Min(0.5f)] public float maxInterval = 25f;
    [Tooltip("Maximum mini-bosses alive at once (cap).")]
    [Min(0)] public int maxConcurrent = 3;

    [Header("Spawn Placement")]
    [Tooltip("Keep mini-bosses at least this far from player.")]
    [Min(0f)] public float minDistanceFromPlayer = 12f;
    [Tooltip("Spawn ring (min/max) world units from player.")]
    public Vector2 spawnRing = new Vector2(18f, 34f);
    [Tooltip("Try this many random points per spawn attempt before giving up.")]
    [Min(1)] public int maxPlacementTries = 12;

    [Header("NavMesh")]
    [Tooltip("Area mask used when sampling NavMesh (default: All Areas = -1).")]
    public int navAreaMask = NavMesh.AllAreas;
    [Tooltip("Agent radius for placement clearance (rough).")]
    [Min(0f)] public float agentRadius = 0.6f;

    [Header("Line of Sight (Optional)")]
    public bool requireNoLineOfSight = true;
    public LayerMask losObstacles = ~0; // everything by default

    [Header("Safety / Filtering")]
    [Tooltip("Layers to avoid overlapping when spawning (e.g. Player, Walls, MapBounds).")]
    public LayerMask overlapBlockers = 0;
    [Min(0f)] public float overlapRadius = 0.9f;

    [Header("Diagnostics")]
    public bool debugLogs = true;

    private readonly List<GameObject> _live = new List<GameObject>();
    private WaitForSeconds _shortWait;

    void Awake()
    {
        _shortWait = new WaitForSeconds(0.2f);
        if (player == null)
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) player = p.transform;
        }
    }

    void OnEnable()
    {
        StartCoroutine(SpawnLoop());
    }

    void OnDisable()
    {
        StopAllCoroutines();
    }

    IEnumerator SpawnLoop()
    {
        if (miniBossPrefabs == null || miniBossPrefabs.Length == 0)
        {
            if (debugLogs) Debug.LogWarning("[MiniBossMixer] No miniBossPrefabs assigned.");
            yield break;
        }
        while (true)
        {
            // Clean list
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                if (_live[i] == null) _live.RemoveAt(i);
            }

            if (_live.Count < maxConcurrent && player != null)
            {
                TrySpawnOne();
            }

            float next = Random.Range(minInterval, maxInterval);
            yield return new WaitForSeconds(next);
        }
    }

    void TrySpawnOne()
    {
        if (player == null) return;

        Vector3 chosen;
        if (!FindSpawnPoint(player.position, out chosen))
        {
            if (debugLogs) Debug.LogWarning("[MiniBossMixer] Failed to find valid spawn point this cycle.");
            return;
        }

        var prefab = miniBossPrefabs[Random.Range(0, miniBossPrefabs.Length)];
        var go = Instantiate(prefab, chosen, Quaternion.identity);

        _live.Add(go);

        // Ensure it has a MiniBoss scaler—if not, add one for safety
        var mb = go.GetComponent<MiniBoss>();
        if (mb == null) mb = go.AddComponent<MiniBoss>();

        // Immediately (re)apply in case prefab was saved at odd scale
        mb.ApplyRandomSizeAndHealth();

        if (debugLogs)
            Debug.Log($"[MiniBossMixer] Spawned '{go.name}' at {chosen} (alive={_live.Count}/{maxConcurrent}).");
    }

    bool FindSpawnPoint(Vector3 center, out Vector3 pos)
    {
        pos = center;
        float minR = Mathf.Max(0f, spawnRing.x);
        float maxR = Mathf.Max(minR + 0.1f, spawnRing.y);

        for (int i = 0; i < maxPlacementTries; i++)
        {
            // Ring sample
            float ang = Random.Range(0f, Mathf.PI * 2f);
            float r = Random.Range(minR, maxR);
            Vector3 candidate = center + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * r;

            // NavMesh sample
            if (NavMesh.SamplePosition(candidate, out var hit, 3.0f, navAreaMask))
            {
                Vector3 p = hit.position;

                // Distance gate
                if ((p - center).sqrMagnitude < (minDistanceFromPlayer * minDistanceFromPlayer))
                    continue;

                // Optional LOS block (avoid immediate visibility)
                if (requireNoLineOfSight)
                {
                    Vector3 eyeFrom = center + Vector3.up * 1.7f;
                    Vector3 eyeTo = p + Vector3.up * 1.0f;
                    if (!Physics.Linecast(eyeFrom, eyeTo, losObstacles))
                        continue; // has clear LOS -> skip
                }

                // Optional overlap check
                if (overlapBlockers.value != 0)
                {
                    if (Physics.CheckSphere(p, overlapRadius, overlapBlockers, QueryTriggerInteraction.Ignore))
                        continue;
                }

                pos = p;
                return true;
            }
        }
        return false;
    }
}
