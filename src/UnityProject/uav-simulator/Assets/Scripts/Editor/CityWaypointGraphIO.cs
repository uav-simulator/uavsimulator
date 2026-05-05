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
    }
}
#endif
