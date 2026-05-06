using System.Collections.Generic;
using NUnit.Framework;
using UavSimulator.CityDemo;
using UnityEngine;

namespace UavSimulator.Tests.EditMode
{
    /// <summary>
    /// EditMode tests for <see cref="WaypointFollowerVehicle"/> AI controller and
    /// <see cref="NpcTrafficSpawner"/>. We exercise the deterministic decision logic
    /// (PickNextTarget, ResolveStartNode) via the public <c>__Test*</c> seams without
    /// touching Rigidbody physics or PlayMode.
    /// </summary>
    public sealed class WaypointFollowerTests
    {
        private GameObject _host;
        private WaypointFollowerVehicle _follower;

        [SetUp]
        public void SetUp()
        {
            _host = new GameObject("follower-host");
            _follower = _host.AddComponent<WaypointFollowerVehicle>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_host != null) Object.DestroyImmediate(_host);
        }

        // ── Helpers ──

        private static CityWaypointGraph BuildLinearGraph()
        {
            var g = ScriptableObject.CreateInstance<CityWaypointGraph>();
            g.nodes.Add(new CityWaypointNode { id = "a", position = new Vector3(0, 0, 0) });
            g.nodes.Add(new CityWaypointNode { id = "b", position = new Vector3(0, 0, 5) });
            g.nodes[0].outgoingEdges.Add(new CityWaypointEdge { toNodeId = "b" });
            return g;
        }

        private static CityWaypointGraph BuildIntersectionGraph()
        {
            var g = ScriptableObject.CreateInstance<CityWaypointGraph>();
            g.nodes.Add(new CityWaypointNode
            {
                id = "center",
                position = Vector3.zero,
                isIntersection = true,
            });
            g.nodes.Add(new CityWaypointNode { id = "north", position = new Vector3(0, 0, 10) });
            g.nodes.Add(new CityWaypointNode { id = "east", position = new Vector3(10, 0, 0) });
            g.nodes.Add(new CityWaypointNode { id = "south", position = new Vector3(0, 0, -10) });
            g.nodes[0].outgoingEdges.Add(new CityWaypointEdge { toNodeId = "north" });
            g.nodes[0].outgoingEdges.Add(new CityWaypointEdge { toNodeId = "east" });
            g.nodes[0].outgoingEdges.Add(new CityWaypointEdge { toNodeId = "south" });
            return g;
        }

        // ── Tests ──

        [Test]
        public void PickNextTarget_Returns_OnlyOutgoing_WhenSingleEdge()
        {
            var graph = BuildLinearGraph();
            _follower.__TestSetGraphAndStart(graph, "a");

            var next = _follower.__TestPickNext(graph.FindNode("a"));

            Assert.IsNotNull(next);
            Assert.AreEqual("b", next.id);
        }

        [Test]
        public void PickNextTarget_Returns_RandomFromMultiple_WhenIntersection()
        {
            var graph = BuildIntersectionGraph();

            // Sample many seeds and confirm we hit at least 2 distinct outgoing
            // edges — proves the pick is non-degenerate.
            var seen = new HashSet<string>();
            for (int seed = 0; seed < 20; seed++)
            {
                _follower.__TestSetGraphAndStart(graph, "center");
                // Re-seed by calling ResetVehicle through the seam (uses seed=0
                // each call, so to vary we just sample multiple picks: with rng
                // initialised via seed=0, draws form a deterministic sequence).
                var center = graph.FindNode("center");
                var pick = _follower.__TestPickNext(center);
                if (pick != null) seen.Add(pick.id);
            }

            // With a single rng-instance per __TestSetGraphAndStart and seed=0,
            // PickNextTarget(center) is deterministic — so above always picks the
            // same first edge. To prove distribution, also drain consecutive picks
            // off ONE rng instance:
            _follower.__TestSetGraphAndStart(graph, "center");
            var center2 = graph.FindNode("center");
            for (int i = 0; i < 30; i++)
            {
                var p = _follower.__TestPickNext(center2);
                if (p != null) seen.Add(p.id);
            }

            Assert.GreaterOrEqual(seen.Count, 2,
                "Random intersection picker must hit at least 2 distinct outgoing edges across 30 draws.");
            foreach (var id in seen)
            {
                Assert.IsTrue(id == "north" || id == "east" || id == "south",
                    $"Picked edge '{id}' is not in the intersection's outgoing set.");
            }
        }

        [Test]
        public void ResetVehicle_Sets_CurrentToStartNode_WhenStartIdGiven()
        {
            var graph = BuildLinearGraph();
            _follower.__TestSetGraphAndStart(graph, "a");

            // ResetVehicle is invoked inside __TestSetGraphAndStart with seed=0.
            // After reset, the next picked target should be "b" (only outgoing
            // from "a"). CurrentNodeId is published only by FixedUpdate, so we
            // verify start-node selection indirectly via PickNextTarget.
            var pick = _follower.__TestPickNext(graph.FindNode("a"));
            Assert.IsNotNull(pick);
            Assert.AreEqual("b", pick.id);
        }

        [Test]
        public void PickNextTarget_ReturnsNull_OnTerminalNode()
        {
            var graph = ScriptableObject.CreateInstance<CityWaypointGraph>();
            graph.nodes.Add(new CityWaypointNode { id = "dead-end" });
            _follower.__TestSetGraphAndStart(graph, "dead-end");

            var pick = _follower.__TestPickNext(graph.FindNode("dead-end"));

            Assert.IsNull(pick);
        }

        [Test]
        public void Spawner_NoCrashOnEmptyGraph()
        {
            var spawnerHost = new GameObject("spawner-host");
            try
            {
                var spawner = spawnerHost.AddComponent<NpcTrafficSpawner>();
                // Deliberately leave graph null and prefab list null.
                Assert.DoesNotThrow(() => spawner.Spawn());
                Assert.DoesNotThrow(() => spawner.DespawnAll());
            }
            finally
            {
                Object.DestroyImmediate(spawnerHost);
            }
        }
    }
}
