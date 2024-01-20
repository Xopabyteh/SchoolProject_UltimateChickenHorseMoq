using Unity.Netcode;
using UnityEngine;
using UnityEngine.Serialization;

public class MapManager : NetworkBehaviour
{
    public static MapManager Singleton { get; set; }

    [SerializeField] private Rect mapBounds;
    public Rect MapBounds => mapBounds; //Don't change this at runtime
    private const float k_DeathBoundsGrow = 3f;
    public Rect DeathBounds => new Rect(mapBounds.x - k_DeathBoundsGrow, mapBounds.y - k_DeathBoundsGrow,
        mapBounds.width + k_DeathBoundsGrow * 2, mapBounds.height + k_DeathBoundsGrow * 2);

    [SerializeField] private Transform[] spawnPoints;
    public Transform[] SpawnPoints => spawnPoints;

    [SerializeField] private Transform goalFlag;
    public Transform GoalFlag => goalFlag;

    void Awake()
    {
        Singleton = this;

        if (spawnPoints.Length != 4)
            Debug.LogError("There must be exactly 4 spawn points in the scene!", this);
    }

    private void OnDrawGizmos()
    {
        //Draw map bounds and death bounds
        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(mapBounds.center, mapBounds.size);

        Gizmos.color = Color.red;
        Gizmos.DrawWireCube(DeathBounds.center, DeathBounds.size);

        //Draw spawn points
        foreach (var spawnPoint in spawnPoints)
        {
            Gizmos.color = Color.blue;
            Gizmos.DrawWireSphere(spawnPoint.position, 0.5f);
        }
    }
}
