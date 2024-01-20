using UnityEngine;
using UnityEngine.Serialization;

[CreateAssetMenu(fileName = "Gizmo", menuName = "Gizmo", order = 0)]
public class GizmoItem : ScriptableObject
{
    public GizmoWorldObject WorldPrefab;

    ///// <summary>
    ///// The object displayed when the player is placing the gizmo in the world.
    ///// </summary>
    //public GizmoGhost GhostPrefab;

    /// <summary>
    /// The items that will be displayed in the box
    /// </summary>
    public GizmoBoxItem BoxItemPrefab;
}