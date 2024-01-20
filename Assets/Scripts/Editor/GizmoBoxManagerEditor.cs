using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Editor
{
    [CustomEditor(typeof(GizmoBoxManager))]
    public class GizmoBoxManagerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            var gizmoBoxManager = (GizmoBoxManager) target;

            if (gizmoBoxManager.GizmoBoxItemsClient == null)
                return;

            //Spacing
            GUILayout.Space(20);

            var sortedBoxItems = gizmoBoxManager.GizmoBoxItemsClient
                .OrderBy(i => i.Value.name);

            //For each gizmo box item on client, create a button with its name
            foreach (var gizmoBoxItem in sortedBoxItems)
            {
                if (GUILayout.Button(gizmoBoxItem.Value.name))
                {
                    gizmoBoxManager.PlayerSelectGizmoServerRpc(gizmoBoxItem.Key);
                }
            }
        }
    }
}