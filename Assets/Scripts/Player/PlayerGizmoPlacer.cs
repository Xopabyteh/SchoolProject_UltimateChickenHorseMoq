using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Responsible for showing the players ghost gizmo when placing it
/// And telling the server where the player wants to place it.
///
/// One exists for each player and each player owns 1. (has ownership over the spawned object)
/// </summary>
public class PlayerGizmoPlacer : NetworkBehaviour
{
    [SerializeField] private InputActionReference placeGizmoInput;
    [SerializeField] private InputActionReference cursorPositionInput;
    [SerializeField] private InputActionReference rotateGizmoInput; //-1 = CounterClockwise, 1 = Clockwise

    public GizmoPlacerState State { get; private set; } = GizmoPlacerState.Idle;
    public Transform GizmoGhostTransform { get; private set; }
    private GizmoWorldObject gizmoGhostInstance;
    private string selectedGizmoName;
    //private int rotationDeg = 0; //Can only ever be 0, 90, 180, 270

    private const int k_SyncGhostEveryNTicks = 2;
    private int tickCounter = 0;

    [ClientRpc]
    public void PlayerSelectedGizmoClientRpc(FixedString128Bytes gizmoName)
    {
        this.selectedGizmoName = gizmoName.Value;
        var gizmo = GizmoBoxManager.Singleton.LoadedGizmoItems[gizmoName.Value];

        gizmoGhostInstance = Instantiate(gizmo.WorldPrefab);
        this.GizmoGhostTransform = gizmoGhostInstance.transform;
    }

    [ClientRpc]
    public void StartPlacingStateClientRpc()
    {
        this.State = GizmoPlacerState.Placing;
        //rotationDeg = 0;

        if (IsOwner)
        {
            cursorPositionInput.action.Enable();
            placeGizmoInput.action.Enable();
            rotateGizmoInput.action.Enable();
        }
    }

    public override void OnNetworkSpawn()
    {
        if (!IsOwner)
            return;

        TimeManager.Singleton.OnTick += OnTickOwner;
        placeGizmoInput.action.performed += TryPlaceGizmo;
        rotateGizmoInput.action.performed += RotateGizmo;
    }

    private void RotateGizmo(InputAction.CallbackContext obj)
    {
        var dir = -1 * (int) obj.ReadValue<float>(); //it is -1f to 1f, but we want 1 to -1 (to match Q,E: multiply by -1)

        var rotationDeg = dir * 90;
        gizmoGhostInstance.SetRotationDeg(gizmoGhostInstance.RotationDeg + rotationDeg);
    }

    private void OnTickOwner()
    {
        //Only runs on owner of this

        if (State != GizmoPlacerState.Placing)
            return;

        tickCounter++;

        var cursorPosition = this.cursorPositionInput.action.ReadValue<Vector2>();
        var cursorWorldPos = CameraManager.Singleton.MainCamera.ScreenToWorldPoint(cursorPosition);
        var cursorGridPos = new Vector2Int(Mathf.RoundToInt(cursorWorldPos.x), Mathf.RoundToInt(cursorWorldPos.y));
        //Clamp grid pos to building area
        var buildingArea = GizmoWorldObjectsManager.Singleton.BuildingArea;
        cursorGridPos.x = Mathf.Clamp(cursorGridPos.x, buildingArea.LeftBottom.x, buildingArea.RightTop.x);
        cursorGridPos.y = Mathf.Clamp(cursorGridPos.y, buildingArea.LeftBottom.y, buildingArea.RightTop.y);

        var ghostPos = new Vector3(cursorGridPos.x, cursorGridPos.y, 0);
        GizmoGhostTransform.position = ghostPos;

        if (tickCounter % k_SyncGhostEveryNTicks == 0)
        {
            SyncGhostPositionServerRpc(ghostPos, gizmoGhostInstance.RotationDeg);
        }
    }

    [ServerRpc]
    private void SyncGhostPositionServerRpc(Vector3 pos, int rotationDeg)
        => SyncGhostPositionClientRpc(pos, rotationDeg);

    [ClientRpc]
    private void SyncGhostPositionClientRpc(Vector3 pos, int rotationDeg)
    {
        if (IsOwner)
            return; //Owner already knows position

        if (State != GizmoPlacerState.Placing || GizmoGhostTransform == null)
            return; //Placed gizmo already, can't sync, placing and sync of position can be called at the same time if the user lags a lot

        GizmoGhostTransform.position = pos;
        gizmoGhostInstance.SetRotationDeg(rotationDeg);
        //GizmoGhostTransform.rotation = Quaternion.Euler(0, 0, rotationDeg);
    }

    private void TryPlaceGizmo(InputAction.CallbackContext obj)
    {
        if (this.State != GizmoPlacerState.Placing)
            return;

        var cursorPosition = this.cursorPositionInput.action.ReadValue<Vector2>();
        var cursorWorldPos = CameraManager.Singleton.MainCamera.ScreenToWorldPoint(cursorPosition);
        var gridPos = new Vector2Int(Mathf.RoundToInt(cursorWorldPos.x), Mathf.RoundToInt(cursorWorldPos.y));

        GizmoWorldObjectsManager.Singleton.PlaceGizmoServerRpc(gridPos, gizmoGhostInstance.RotationDeg, selectedGizmoName);
    }

    /// <summary>
    /// Called by the server when the placement was successful
    /// </summary>
    [ClientRpc]
    public void RespondPlacementWasSuccessfulClientRpc()
    {
        Destroy(GizmoGhostTransform.gameObject);
        GizmoGhostTransform = null;
        this.State = GizmoPlacerState.Idle;

        if (IsOwner)
        {
            cursorPositionInput.action.Disable();
            placeGizmoInput.action.Disable();
            rotateGizmoInput.action.Disable();
        }
    }

    public enum GizmoPlacerState
    {
        Idle,
        Placing
    }
}