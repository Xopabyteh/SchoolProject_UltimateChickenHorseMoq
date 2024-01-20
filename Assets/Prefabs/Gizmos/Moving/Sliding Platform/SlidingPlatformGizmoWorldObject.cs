using UnityEngine;

public class SlidingPlatformGizmoWorldObject : GizmoWorldObject, IGizmoUsesCustomRotationModule
{
    [SerializeField] private Transform sliderPivot;
    public new void SetRotationDeg(int rotationDeg)
    {
        RotationDeg = rotationDeg;

        // ReSharper disable once PossibleLossOfFraction
        // rotationDeg is in multiples of 90
        sliderPivot.rotation = Quaternion.Euler(0,0, rotationDeg / 2);
    }

    public new void CalculateOccupiedSpacesByRotation()
    {
        //Don't rotate, as platform doesn't rotate, only slider pivot
        OccupiedSpacesRotationRelative = occupiedSpaces0Deg;
    }
}