using UnityEngine;

namespace UavSimulator.CityDemo
{
    /// <summary>
    /// Behaviour-обёртка для отображения <see cref="CityWaypointGraph"/> в Scene view.
    /// Добавляется как компонент на любой GameObject (например, root трассы).
    /// На runtime никаких побочных действий не выполняет.
    /// </summary>
    public sealed class CityWaypointGraphGizmo : MonoBehaviour
    {
        public CityWaypointGraph graph;
        public Color nodeColor = new(0.2f, 0.9f, 0.4f);
        public Color edgeColor = new(0.9f, 0.7f, 0.2f);
        public Color intersectionColor = new(1.0f, 0.3f, 0.2f);
        public float nodeRadius = 0.3f;

        private void OnDrawGizmos()
        {
            if (graph == null) return;
            foreach (var node in graph.nodes)
            {
                Gizmos.color = node.isIntersection ? intersectionColor : nodeColor;
                Gizmos.DrawSphere(node.position, nodeRadius);
                Gizmos.color = edgeColor;
                foreach (var edge in node.outgoingEdges)
                {
                    var target = graph.FindNode(edge.toNodeId);
                    if (target == null) continue;
                    Gizmos.DrawLine(node.position, target.position);
                    var arrowEnd = Vector3.Lerp(node.position, target.position, 0.85f);
                    Gizmos.DrawSphere(arrowEnd, nodeRadius * 0.4f);
                }
            }
        }
    }
}
