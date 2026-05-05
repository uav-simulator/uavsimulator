using System;
using System.Collections.Generic;
using UnityEngine;

namespace UavSimulator.CityDemo
{
    [Serializable]
    public sealed class CityWaypointEdge
    {
        public string toNodeId;
        [Tooltip("Целевая скорость на этом ребре, м/с. 0 = use default.")]
        public float speedLimitMps;
        [Tooltip("Опциональный тэг для маршрутизатора: 'left', 'right', 'straight', 'merge'.")]
        public string maneuverTag;
    }

    [Serializable]
    public sealed class CityWaypointNode
    {
        public string id;
        public Vector3 position;
        [Tooltip("Опциональный yaw в градусах — направление 'въезда' в точку, для AI-controller'а.")]
        public float incomingYawDeg;
        [Tooltip("Если true — точка является интерсекшеном (несколько исходящих ребёр).")]
        public bool isIntersection;
        public List<CityWaypointEdge> outgoingEdges = new();
    }

    [CreateAssetMenu(menuName = "UavSimulator/City/Waypoint Graph", fileName = "CityWaypointGraph")]
    public sealed class CityWaypointGraph : ScriptableObject
    {
        [Tooltip("Уникальный идентификатор графа. По convention: 'city.<scene>.v<major>'.")]
        public string graphId = "city.polygon_demo.v1";
        public List<CityWaypointNode> nodes = new();

        public CityWaypointNode FindNode(string id)
        {
            for (var i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].id == id) return nodes[i];
            }
            return null;
        }
    }
}
