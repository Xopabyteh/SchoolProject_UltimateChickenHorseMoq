using Unity.Netcode;

/// <summary>
/// The item displayed in the box. It can be clicked on to select it.
/// </summary>
public class GizmoBoxItem : NetworkBehaviour
{
    public int GizmoId { get; private set; }

    public void Init(int gizmoId)
    {
        this.GizmoId = gizmoId;
    }

    public void SelectGizmo()
    {
        //Called locally from the client when he clicks on the item
        GizmoBoxManager.Singleton.PlayerSelectGizmoServerRpc(this.GizmoId);
    }
}