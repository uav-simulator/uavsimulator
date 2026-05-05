#if UNITY_EDITOR
using System.IO;
using UavSimulator.CityDemo;
using UnityEditor;
using UnityEngine;

namespace UavSimulator.Editor
{
    public static class CityWaypointGraphIO
    {
        [MenuItem("Tools/UavSimulator/City Waypoints/Export Selected to JSON...")]
        public static void ExportSelected()
        {
            var graph = Selection.activeObject as CityWaypointGraph;
            if (graph == null)
            {
                EditorUtility.DisplayDialog("Export Waypoints", "Select a CityWaypointGraph asset first.", "OK");
                return;
            }
            var path = EditorUtility.SaveFilePanel("Export waypoints", "", $"{graph.graphId}.json", "json");
            if (string.IsNullOrEmpty(path)) return;
            File.WriteAllText(path, JsonUtility.ToJson(graph, true));
            EditorUtility.DisplayDialog("Export Waypoints", $"Saved to {path}", "OK");
        }

        [MenuItem("Tools/UavSimulator/City Waypoints/Import from JSON...")]
        public static void ImportFromJson()
        {
            var path = EditorUtility.OpenFilePanel("Import waypoints", "", "json");
            if (string.IsNullOrEmpty(path)) return;
            var graph = ScriptableObject.CreateInstance<CityWaypointGraph>();
            JsonUtility.FromJsonOverwrite(File.ReadAllText(path), graph);
            var savePath = EditorUtility.SaveFilePanelInProject(
                "Save imported graph", $"{graph.graphId}", "asset",
                "Choose where in the project to save the imported graph.");
            if (string.IsNullOrEmpty(savePath)) return;
            AssetDatabase.CreateAsset(graph, savePath);
            AssetDatabase.SaveAssets();
            EditorUtility.DisplayDialog("Import Waypoints", $"Created {savePath}", "OK");
        }

        [MenuItem("Tools/UavSimulator/City Waypoints/Validate Selected")]
        public static void ValidateSelected()
        {
            var graph = Selection.activeObject as CityWaypointGraph;
            if (graph == null) { EditorUtility.DisplayDialog("Validate", "Select a CityWaypointGraph.", "OK"); return; }
            var issues = 0;
            var ids = new System.Collections.Generic.HashSet<string>();
            foreach (var n in graph.nodes)
            {
                if (string.IsNullOrEmpty(n.id)) { Debug.LogWarning("Empty node id"); issues++; }
                if (!ids.Add(n.id)) { Debug.LogWarning($"Duplicate node id: {n.id}"); issues++; }
                foreach (var e in n.outgoingEdges)
                {
                    if (graph.FindNode(e.toNodeId) == null)
                    {
                        Debug.LogWarning($"Node {n.id} edge points to missing node {e.toNodeId}");
                        issues++;
                    }
                }
            }
            EditorUtility.DisplayDialog("Validate Waypoints",
                issues == 0 ? "Graph valid." : $"Found {issues} issues, see Console.",
                "OK");
        }

        [MenuItem("Tools/UavSimulator/City Waypoints/Generate Demo Scaffold")]
        public static void GenerateDemoScaffold()
        {
            var graph = ScriptableObject.CreateInstance<CityWaypointGraph>();
            graph.graphId = "city.polygon_demo.v1";

            CityWaypointNode N(string id, Vector3 pos, bool isIntersection = false)
            {
                var node = new CityWaypointNode
                {
                    id = id,
                    position = pos,
                    isIntersection = isIntersection,
                };
                graph.nodes.Add(node);
                return node;
            }

            CityWaypointEdge E(string toNodeId, float speedLimitMps, string maneuverTag) =>
                new() { toNodeId = toNodeId, speedLimitMps = speedLimitMps, maneuverTag = maneuverTag };

            var northIn = N("n_north_in", new Vector3(0f, 0.2f, +20f));
            var southIn = N("n_south_in", new Vector3(0f, 0.2f, -20f));
            var eastIn = N("n_east_in", new Vector3(+20f, 0.2f, 0f));
            var westIn = N("n_west_in", new Vector3(-20f, 0.2f, 0f));
            var center = N("n_center", new Vector3(0f, 0.2f, 0f), isIntersection: true);
            N("n_north_out", new Vector3(0f, 0.2f, +25f));
            N("n_east_out", new Vector3(+25f, 0.2f, 0f));
            N("n_west_out", new Vector3(-25f, 0.2f, 0f));
            N("n_south_out", new Vector3(0f, 0.2f, -25f));

            northIn.outgoingEdges.Add(E("n_center", 5.0f, "straight"));
            southIn.outgoingEdges.Add(E("n_center", 5.0f, "straight"));
            eastIn.outgoingEdges.Add(E("n_center", 5.0f, "straight"));
            westIn.outgoingEdges.Add(E("n_center", 5.0f, "straight"));

            center.outgoingEdges.Add(E("n_north_out", 4.0f, "straight"));
            center.outgoingEdges.Add(E("n_east_out", 3.0f, "right"));
            center.outgoingEdges.Add(E("n_west_out", 3.0f, "left"));

            const string dir = "Assets/Resources/UavSimulator/CityWaypoints";
            const string path = dir + "/city.polygon_demo.v1.asset";
            System.IO.Directory.CreateDirectory(dir);
            AssetDatabase.CreateAsset(graph, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[CityWaypoints] Demo scaffold created at {path}");
        }
    }
}
#endif
