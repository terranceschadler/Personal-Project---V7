using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RandomMapGenerator : MonoBehaviour
{
    [Header("Tiles")]
    public GameObject startingTile;
    public GameObject deadEndTile;
    [Tooltip("Regular map tiles to spawn.")]
    public GameObject[] tilePrefabs;

    [Header("Unique Tiles (spawn at most once each, placed after regulars)")]
    public GameObject[] uniqueTilePrefabs;
    [Range(0f, 1f)] public float uniqueSpawnChance = 0.25f;

    [Header("Bake")]
    public NavMeshRuntimeBaker runtimeBaker;
    public bool autoBakeOnComplete = true;

    [Header("Pacing & Safety")]
    public int tilesPerFrame = 8;
    public int uniquesPerFrame = 8;
    public int deadEndsPerFrame = 16;
    public float cellSize = 1f;
    public int noProgressFrameLimit = 120;

    [Header("State (read-only)")]
    [SerializeField] private bool _initializing;
    [SerializeField] private bool _deadEndsCompleted;
    [SerializeField] private bool _mapCompleted;
    public bool MapCompleted => _mapCompleted;

    [Tooltip("How many regular tiles to spawn (not counting start/unique/dead-ends).")]
    public int tileCount = 20;

    // Spawn caches
    public List<Transform> tileSpawns = new List<Transform>();
    private readonly HashSet<Vector3Int> _occupiedCells = new HashSet<Vector3Int>();

    // Unique pool (runtime)
    private readonly List<GameObject> _remainingUniques = new List<GameObject>();

    // Internals
    private int _tileCountTotal;
    private bool _bakeTriggered;
    private Coroutine _genRoutine;

    public event Action OnMapCompleted;

    private void Awake()
    {
        _tileCountTotal = tileCount;

        // Drop the starting tile immediately
        if (startingTile)
        {
            Instantiate(startingTile, transform.position, transform.rotation);
            MarkOccupied(transform.position);
        }

        RebuildUniquePool();
    }

    private void Start()
    {
        if (!Application.isPlaying) return;

        if (!runtimeBaker) runtimeBaker = FindFirstObjectByType<NavMeshRuntimeBaker>();

        // Wire baker to wait for THIS generator
        if (runtimeBaker)
        {
            runtimeBaker.mapGenerator = this;
            runtimeBaker.waitForGenerator = true;
        }

        InitTileSpawns();

        if (_genRoutine == null)
            _genRoutine = StartCoroutine(GenerateMapRoutine());
    }

    private IEnumerator GenerateMapRoutine()
    {
        // -------- PASS 1: Place REGULAR tiles only --------
        int framesWithoutProgress = 0;

        while (tileCount > 0)
        {
            InitTileSpawns();
            if (tileSpawns.Count == 0) break;

            int placedThisFrame = 0;
            var snapshot = new List<Transform>(tileSpawns);

            for (int i = 0; i < snapshot.Count && tileCount > 0; i++)
            {
                var sp = snapshot[i];
                if (!sp) continue;
                if (!IsCellFree(sp.position)) continue;

                var prefab = GetRandomRegularTile();
                if (!prefab)
                {
                    Debug.LogWarning("[MapGen] No regular tiles configured. Skipping to unique/dead-end passes.");
                    tileCount = 0;
                    break;
                }

                Instantiate(prefab, sp.position, sp.rotation);

                ConsumeSpawn(sp);
                MarkOccupied(sp.position);
                tileCount--;

                placedThisFrame++;
                if (placedThisFrame >= tilesPerFrame)
                {
                    placedThisFrame = 0;
                    yield return null;
                }
            }

            framesWithoutProgress = (placedThisFrame == 0) ? framesWithoutProgress + 1 : 0;
            if (framesWithoutProgress > noProgressFrameLimit)
            {
                Debug.LogWarning("[MapGen] No progress for too long; moving on.");
                break;
            }

            yield return null;
        }

        // -------- PASS 2: Place UNIQUE tiles --------
        if (_remainingUniques.Count > 0)
        {
            InitTileSpawns();
            if (tileSpawns.Count > 0)
            {
                int spawnedThisFrame = 0;
                var snapshot = new List<Transform>(tileSpawns);

                for (int i = 0; i < snapshot.Count && _remainingUniques.Count > 0; i++)
                {
                    var sp = snapshot[i];
                    if (!sp) continue;

                    if (!IsCellFree(sp.position)) { ConsumeSpawn(sp); continue; }

                    if (UnityEngine.Random.value <= uniqueSpawnChance)
                    {
                        var uniquePrefab = TakeRandomUnique();
                        if (uniquePrefab)
                        {
                            Instantiate(uniquePrefab, sp.position, sp.rotation);

                            ConsumeSpawn(sp);
                            MarkOccupied(sp.position);

                            spawnedThisFrame++;
                            if (spawnedThisFrame >= uniquesPerFrame)
                            {
                                spawnedThisFrame = 0;
                                yield return null;
                            }
                        }
                    }
                }
            }
        }

        // -------- PASS 3: Fill remaining with DEAD-ENDS --------
        InitTileSpawns();
        if (tileSpawns.Count > 0)
        {
            int spawnedThisFrame = 0;
            var ends = new List<Transform>(tileSpawns);

            for (int i = 0; i < ends.Count; i++)
            {
                var sp = ends[i];
                if (!sp) continue;

                if (!IsCellFree(sp.position)) { ConsumeSpawn(sp); continue; }

                if (deadEndTile)
                    Instantiate(deadEndTile, sp.position, sp.rotation);

                ConsumeSpawn(sp);
                MarkOccupied(sp.position);

                spawnedThisFrame++;
                if (spawnedThisFrame >= deadEndsPerFrame)
                {
                    spawnedThisFrame = 0;
                    yield return null;
                }
            }
            _deadEndsCompleted = true;
        }

        // -------- FINALIZE --------
        _mapCompleted = true;
        OnMapCompleted?.Invoke();

        if (autoBakeOnComplete && !_bakeTriggered)
            TryBakeNavMeshOnce();
    }

    private void TryBakeNavMeshOnce()
    {
        if (_bakeTriggered) return;
        _bakeTriggered = true;

        if (runtimeBaker)
        {
            StartCoroutine(BakeNextFrame());
        }
        else
        {
            Debug.LogWarning("[MapGen] No NavMeshRuntimeBaker found. Skipping runtime bake.");
        }
    }

    private IEnumerator BakeNextFrame()
    {
        yield return null; // let late colliders register
        runtimeBaker.RequestBake(this);
    }

    private void InitTileSpawns()
    {
        if (_initializing) return;

        _initializing = true;
        tileSpawns.Clear();

        var sPoints = GameObject.FindGameObjectsWithTag("TileSpawn");
        for (int i = 0; i < sPoints.Length; i++)
        {
            var t = sPoints[i].transform;
            if (!t || !t.gameObject.activeInHierarchy) continue;
            tileSpawns.Add(t);
        }

        _initializing = false;
    }

    private GameObject GetRandomRegularTile()
    {
        if (tilePrefabs != null && tilePrefabs.Length > 0)
            return tilePrefabs[UnityEngine.Random.Range(0, tilePrefabs.Length)];
        return null;
    }

    private GameObject TakeRandomUnique()
    {
        if (_remainingUniques.Count == 0) return null;
        int idx = UnityEngine.Random.Range(0, _remainingUniques.Count);
        var unique = _remainingUniques[idx];
        _remainingUniques.RemoveAt(idx);
        return unique;
    }

    private void ConsumeSpawn(Transform sp)
    {
        if (!sp) return;
        sp.tag = "Untagged";
        sp.gameObject.SetActive(false);
    }

    private Vector3Int CellOf(Vector3 p)
    {
        float s = Mathf.Max(0.0001f, cellSize);
        return new Vector3Int(
            Mathf.RoundToInt(p.x / s),
            Mathf.RoundToInt(p.y / s),
            Mathf.RoundToInt(p.z / s)
        );
    }

    private void MarkOccupied(Vector3 p) => _occupiedCells.Add(CellOf(p));
    private bool IsCellFree(Vector3 p) => !_occupiedCells.Contains(CellOf(p));

    private void RebuildUniquePool()
    {
        _remainingUniques.Clear();
        if (uniqueTilePrefabs != null)
        {
            for (int i = 0; i < uniqueTilePrefabs.Length; i++)
            {
                var go = uniqueTilePrefabs[i];
                if (go && !_remainingUniques.Contains(go))
                    _remainingUniques.Add(go);
            }
        }
    }

    // -------------------- Public --------------------
    public void RegenerateMap()
    {
        if (_genRoutine != null) { StopCoroutine(_genRoutine); _genRoutine = null; }

        // Destroy all existing tiles
        var allTiles = GameObject.FindGameObjectsWithTag("RoomTile");
        for (int i = 0; i < allTiles.Length; i++)
            Destroy(allTiles[i]);

        // Disable stray TileSpawn markers
        var spawns = GameObject.FindGameObjectsWithTag("TileSpawn");
        for (int i = 0; i < spawns.Length; i++)
            spawns[i].SetActive(false);

        // Reset state
        _occupiedCells.Clear();
        tileSpawns.Clear();

        tileCount = _tileCountTotal;
        _initializing = _deadEndsCompleted = _mapCompleted = _bakeTriggered = false;

        // Rebuild unique pool
        RebuildUniquePool();

        // Re-place starting tile
        if (startingTile)
        {
            Instantiate(startingTile, transform.position, transform.rotation);
            MarkOccupied(transform.position);
        }

        InitTileSpawns();

        _genRoutine = StartCoroutine(GenerateMapRoutine());
    }
}
