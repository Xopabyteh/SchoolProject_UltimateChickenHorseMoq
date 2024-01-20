using JetBrains.Annotations;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;

/// <summary>
/// Knows about the spaces the gizmo occupies.
/// </summary>
public class GizmoWorldObject : NetworkBehaviour
{
    /// <summary>
    /// Fired on the Clients from <see cref="GizmoWorldObjectsManager"/> when playing phase starts
    /// </summary>
    public UnityEvent OnPlayingStarted;
    /// <summary>
    /// Fired on the Clients from <see cref="GizmoWorldObjectsManager"/> when playing phase ends
    /// </summary>
    public UnityEvent OnPlayingEnded;
    /// <summary>
    /// Fired on the Clients from <see cref="GizmoWorldObjectsManager"/> when actually placed down.
    /// Awake is called even when created through the gizmo placer
    /// </summary>
    public UnityEvent OnAddedToWorld;

    /// <summary>
    /// The spaces the gizmo occupies when it's rotated 0 degrees.
    /// Only set this in the inspector.
    /// </summary>
    [SerializeField] protected Vector2Int[] occupiedSpaces0Deg;

    /// <summary>
    /// The spaces <see cref="occupiedSpaces0Deg"/> rotated by <see cref="RotationDeg"/>.
    /// Don't change anywhere except in <see cref="CalculateOccupiedSpacesByRotation"/>.
    /// </summary>
    public Vector2Int[] OccupiedSpacesRotationRelative { get; protected set; }

    public int RotationDeg { get; protected set; }

    /// <summary>
    /// Initializes <see cref="OccupiedSpacesRotationRelative"/> field"/>
    /// </summary>
    public void CalculateOccupiedSpacesByRotation()
    {
        if (this.TryGetComponent(out IGizmoUsesCustomRotationModule rotM))
        {
            rotM.CalculateOccupiedSpacesByRotation();
            return;
        }

        OccupiedSpacesRotationRelative = RotateOccupiedSpaces(this.RotationDeg);
    }

    protected Vector2Int[] RotateOccupiedSpaces(int degrees) 
    {
        // Thank you ChatGPT for this code snippet
        Vector2Int[] Rotate90Degrees(Vector2Int[] spaces)
        {
            var rotated = new Vector2Int[spaces.Length];

            for (int i = 0; i < spaces.Length; i++)
            {
                // Rotate each occupied space 90 degrees
                rotated[i] = new Vector2Int(-spaces[i].y, spaces[i].x);
            }

            return rotated;
        }

        var numRotations = (degrees / 90) % 4; // Calculate the number of 90-degree rotations (0, 1, 2, or 3)

        var rotatedSpaces = occupiedSpaces0Deg;

        for (int i = 0; i < numRotations; i++)
        {
            rotatedSpaces = Rotate90Degrees(rotatedSpaces);
        }

        return rotatedSpaces;
    }
    /// <summary>
    /// When overriding, don't forget to set <see cref="RotationDeg"/>
    /// </summary>
    /// <param name="rotationDeg">In multiples of 90</param>
    public void SetRotationDeg(int rotationDeg)
    {
        if (this.TryGetComponent(out IGizmoUsesCustomRotationModule rotM))
        {
            rotM.SetRotationDeg(rotationDeg);
            return;
        }

        RotationDeg = rotationDeg;
        transform.rotation = Quaternion.Euler(0, 0, rotationDeg);
    }

    /// <summary>
    /// When glue is about to be placed on this object, this method is called.
    /// The transform received from here will become the parent of the glue
    /// </summary>
    /// <param name="glueWorldPosition"></param>
    /// <returns>New glue parent</returns>
    public Transform GetGlueParent(Vector2Int glueWorldPosition)
    {
        if (this.TryGetComponent<GizmoFieldGlueParentingStrategyModuleModule>(out var customGlueParentingStrategyMod))
        {
            //Custom impl
            return customGlueParentingStrategyMod.GetGlueParent(glueWorldPosition);
        }

        //Base impl
        return transform;
    }

    /// <summary>
    /// When this object should be connected to a glue (this object was placed on glue, or glue was placed bellow this),
    /// this method is called. The transform received will be re-parented to the glue. If returned transform is null,
    /// no re-parenting will happen and the glue will not "stick"
    /// </summary>
    /// <param name="glueWorldPosition"></param>
    /// <returns></returns>
    [CanBeNull]
    public Transform GetChildForGlue(Vector2Int glueWorldPosition)
    {
        if (this.TryGetComponent<GizmoFieldGlueParentingStrategyModuleModule>(out var customGlueParentingStrategyMod))
        {
            //Custom impl
            return customGlueParentingStrategyMod.GetChildForGlue(glueWorldPosition);
        }

        //Base impl
        return transform;
    }

    private void OnDrawGizmos()
    {
        //Draw occupied spaces rotated
        if (OccupiedSpacesRotationRelative != null)
        {
            Gizmos.color = Color.red;
            var worldPosition = transform.position; //Cache

            foreach (var space in OccupiedSpacesRotationRelative)
            {
                Gizmos.DrawWireCube(new Vector2(worldPosition.x + space.x, worldPosition.y + space.y), Vector3.one);
            }
        }
        //Draw occupied spaces not rotated
        else
        {
            Gizmos.color = Color.green;
            var worldPosition = transform.position; //Cache

            foreach (var space in occupiedSpaces0Deg)
            {
                Gizmos.DrawWireCube(new Vector2(worldPosition.x + space.x, worldPosition.y + space.y), Vector3.one);
            }
        }
    }
}
