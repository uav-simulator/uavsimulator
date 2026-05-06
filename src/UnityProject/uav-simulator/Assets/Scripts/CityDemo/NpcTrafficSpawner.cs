using System.Collections.Generic;
using UnityEngine;

namespace UavSimulator.CityDemo
{
    /// <summary>
    /// Spawns N WaypointFollowerVehicle instances on random graph nodes.
    /// Uses round-robin colour rotation through provided NPC prefabs (or
    /// falls back to procedural fallback shells coloured by index).
    /// </summary>
    public sealed class NpcTrafficSpawner : MonoBehaviour
    {
        [SerializeField] private CityWaypointGraph graph;
        [SerializeField] private GameObject[] npcVehiclePrefabs;
        [SerializeField] private int npcCount = 5;
        [SerializeField] private int seed = 42;

        private readonly List<GameObject> spawned = new();

        public void Spawn()
        {
            DespawnAll();
            if (graph == null || npcCount <= 0) return;
            var rng = new System.Random(seed);
            for (int i = 0; i < npcCount; i++)
            {
                var prefab = npcVehiclePrefabs != null && npcVehiclePrefabs.Length > 0
                    ? npcVehiclePrefabs[i % npcVehiclePrefabs.Length]
                    : null;
                if (prefab == null) continue;
                var startNode = graph.nodes.Count > 0 ? graph.nodes[rng.Next(0, graph.nodes.Count)] : null;
                if (startNode == null) continue;

                var instance = Instantiate(prefab, startNode.position, Quaternion.identity, transform);
                instance.name = $"Npc_{i:D2}_{prefab.name}";

                var follower = instance.GetComponent<WaypointFollowerVehicle>();
                if (follower != null)
                {
                    follower.__TestSetGraphAndStart(graph, startNode.id);
                }
                spawned.Add(instance);
            }
        }

        public void DespawnAll()
        {
            foreach (var go in spawned) if (go != null) Destroy(go);
            spawned.Clear();
        }

        private void Start() { Spawn(); }
    }
}
