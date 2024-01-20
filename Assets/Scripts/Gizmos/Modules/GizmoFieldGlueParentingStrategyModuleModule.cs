using JetBrains.Annotations;
using UnityEngine;

public class GizmoFieldGlueParentingStrategyModuleModule : MonoBehaviour, IGizmoCustomGlueParentingStrategyModule
{
    [SerializeField] [Header("X glued to this")] private Transform glueParent;
    [SerializeField] [Header("This glued to X <nullable>")] [CanBeNull] private Transform childForGlue;
    public Transform GetGlueParent(Vector2Int glueWorldPosition)
    {
        return glueParent;
    }

    [CanBeNull]
    public Transform GetChildForGlue(Vector2Int glueWorldPosition)
    {
        return childForGlue;
    }
}