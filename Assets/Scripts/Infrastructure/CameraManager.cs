using System.Linq;
using Cinemachine;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Serialization;

public class CameraManager : NetworkBehaviour
{
    public static CameraManager Singleton { get; private set; }

    [SerializeField] private Camera mainCamera;
    public Camera MainCamera => mainCamera;

    [SerializeField] private CinemachineVirtualCamera virtualMainCamera; //Players
    [SerializeField] private CinemachineTargetGroup virtualMainTargetGroup; //Players
    private CinemachineConfiner2D virtualMainConfiner;

    [SerializeField] private CinemachineVirtualCamera virtualCameraWholeMapView;
    [SerializeField] private CinemachineVirtualCamera virtualCameraGoalView;

    [SerializeField] private CinemachineVirtualCamera virtualCameraGizmoPlacersView;
    [SerializeField] private CinemachineTargetGroup virtualTargetGroupGizmoPlacers;
    private CinemachineConfiner2D virtualConfinerGizmoPlacers;

    [SerializeField] private PolygonCollider2D confiningCollider;

    void Awake()
    {
        Singleton = this;

        SetupVCamWholeMapView();
        SetupVCamFinishView();

        SetupConfines();
    }

    private void SetupConfines()
    {
        //Generate collider from map bounds
        var mapBounds = MapManager.Singleton.MapBounds;
        var points = new[]
        {
            new Vector2(mapBounds.xMin, mapBounds.yMin),
            new Vector2(mapBounds.xMax, mapBounds.yMin),
            new Vector2(mapBounds.xMax, mapBounds.yMax),
            new Vector2(mapBounds.xMin, mapBounds.yMax)
        };
        confiningCollider.points = points;

        virtualMainConfiner = virtualMainCamera.GetComponent<CinemachineConfiner2D>();
        virtualConfinerGizmoPlacers = virtualCameraGizmoPlacersView.GetComponent<CinemachineConfiner2D>();

        virtualMainConfiner.m_BoundingShape2D = confiningCollider;
        virtualConfinerGizmoPlacers.m_BoundingShape2D = confiningCollider;
    }

    private void SetupVCamWholeMapView()
    {
        var virtualCameraWholeMapViewTransform = virtualCameraWholeMapView.transform; //Cache

        //Recalculate whole map view camera ortho size to cover entire map bounds
        // (regardless of overshooting one side)
        var mapBounds = MapManager.Singleton.MapBounds;
        var mapBoundsSize = mapBounds.size;
        var mapBoundsSizeMax = Mathf.Max(mapBoundsSize.x, mapBoundsSize.y);
        var orthoSize = mapBoundsSizeMax / 2f;
        virtualCameraWholeMapView.m_Lens.OrthographicSize = orthoSize;

        //Move to center of map bounds
        var mapBoundsCenter = mapBounds.center;
        virtualCameraWholeMapViewTransform.position = new Vector3(mapBoundsCenter.x, mapBoundsCenter.y,
                       virtualCameraWholeMapViewTransform.position.z);
    }

    private void SetupVCamFinishView()
    {
        var virtualCameraFinishViewTransform = virtualCameraGoalView.transform; //Cache

        //Move to end flag pos
        var endFlagPos = MapManager.Singleton.GoalFlag.position;
        virtualCameraFinishViewTransform.position = new Vector3(endFlagPos.x, endFlagPos.y,
                       virtualCameraFinishViewTransform.position.z);
    }

    [ClientRpc]
    public void FocusGoalClientRpc()
    {
        FocusGoal();
    }

    public void FocusGoal()
    {
        virtualCameraGoalView.SetTopPriority();
    }

    /// <param name="activePlayersNetRefs">Must be of type <see cref="PlayerPresence"/></param>
    [ClientRpc]
    public void FocusActivePlayersClientRpc(NetworkBehaviourReference[] activePlayersNetRefs)
    {
        var players = activePlayersNetRefs
            .Select(p => p.TryGet(out PlayerPresence player) ? player.PlayerCharacter.transform : null)
            .Where(p => p != null);

        virtualMainCamera.SetTopPriority();

        virtualMainTargetGroup.m_Targets = players
            .Select(p => new CinemachineTargetGroup.Target
            {
                target = p,
                radius = 5f,
                weight = 1f
            })
            .ToArray();
    }

    [ClientRpc]
    public void FocusMapClientRpc()
    {
        FocusMap();
    }

    public void FocusMap()
    {
        virtualCameraWholeMapView.SetTopPriority();
    }


    /// <param name="gizmoPlacersNetRefs">Must be of type <see cref="PlayerGizmoPlacer"/></param>
    [ClientRpc]
    public void FocusGizmoPlacersClientRpc(NetworkBehaviourReference[] gizmoPlacersNetRefs)
    {
        var gizmoPlacers = gizmoPlacersNetRefs
            .Select(p => p.TryGet(out PlayerGizmoPlacer gizmoPlacer) ? gizmoPlacer : null)
            .Where(p => p != null)
            .ToArray();

        virtualCameraGizmoPlacersView.SetTopPriority();
        
        virtualTargetGroupGizmoPlacers.m_Targets = gizmoPlacers
            .Select(p => new CinemachineTargetGroup.Target
            {
                target = p.GizmoGhostTransform,
                radius = 2f,
                weight = p.OwnerClientId == NetworkManager.Singleton.LocalClientId ? 2f : 1f
            })
            .ToArray();
    }
}
public static class CameraExtensions 
{
    private static int _topPriority = 11;
    public static void SetTopPriority(this CinemachineVirtualCamera vCam)
    {
        _topPriority++;
        vCam.Priority = _topPriority;
    }
}

