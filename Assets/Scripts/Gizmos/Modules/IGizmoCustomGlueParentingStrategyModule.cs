using JetBrains.Annotations;
using UnityEngine;

public interface IGizmoCustomGlueParentingStrategyModule
{
    public Transform GetGlueParent(Vector2Int glueWorldPosition);
    [CanBeNull] public Transform GetChildForGlue(Vector2Int glueWorldPosition);
}