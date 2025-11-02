using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class RoomTile : MonoBehaviour
{
    //public GameObject[] pickupSpawns;
    public GameObject[] buildingSpawns;
    //public GameObject[] NPCPrefabs;
    //public GameObject[] NPCSpawns;

    public List <GameObject> buildingPrefabs;
    //public GameObject[] pickupPrefabs;
    public GameObject[] gateWays;
    public GameObject floor;

    //public float pickupRespawnTime = 30.0f;

    public RandomMapGenerator _randomMapGenerator;
    public bool spawningBuildings = false;

    private void Awake()
    {
        _randomMapGenerator = GameObject.FindGameObjectWithTag("MapGenerator").GetComponent<RandomMapGenerator>();
        InitNavMesh();
        SpawnBuildings();
    }

    private void InitNavMesh()
    {
        Collider[] cols = gameObject.GetComponentsInChildren<Collider>();
        foreach (Collider col in cols)
        {
            if (col.gameObject.CompareTag("Walkable"))
            {
                floor = col.gameObject;
                if (!floor.GetComponent<NavMeshSurface>())
                {
                    floor.AddComponent<NavMeshSurface>();
                }
            }
        }
    }

    private void SpawnBuildings()
    {
        if (buildingSpawns.Length > 0)
        {
            foreach (GameObject bSpawn in buildingSpawns)
            {
                Instantiate(buildingPrefabs[Random.Range(0, buildingPrefabs.Count)], bSpawn.transform);
            }
        }
        
    }






}
