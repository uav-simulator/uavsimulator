using NUnit.Framework;
using UavSimulator.CityDemo;
using UnityEngine;

namespace UavSimulator.Tests.EditMode
{
    public class CityWaypointGraphTests
    {
        [Test]
        public void FindNode_ReturnsNode_WhenIdExists()
        {
            var graph = ScriptableObject.CreateInstance<CityWaypointGraph>();
            graph.nodes.Add(new CityWaypointNode { id = "n1" });
            graph.nodes.Add(new CityWaypointNode { id = "n2" });
            Assert.IsNotNull(graph.FindNode("n1"));
            Assert.IsNotNull(graph.FindNode("n2"));
        }

        [Test]
        public void FindNode_ReturnsNull_WhenIdMissing()
        {
            var graph = ScriptableObject.CreateInstance<CityWaypointGraph>();
            graph.nodes.Add(new CityWaypointNode { id = "n1" });
            Assert.IsNull(graph.FindNode("missing"));
        }

        [Test]
        public void NodeWithMultipleOutgoingEdges_IsValid()
        {
            var node = new CityWaypointNode { id = "intersection", isIntersection = true };
            node.outgoingEdges.Add(new CityWaypointEdge { toNodeId = "out1" });
            node.outgoingEdges.Add(new CityWaypointEdge { toNodeId = "out2" });
            node.outgoingEdges.Add(new CityWaypointEdge { toNodeId = "out3" });
            Assert.AreEqual(3, node.outgoingEdges.Count);
        }
    }
}
