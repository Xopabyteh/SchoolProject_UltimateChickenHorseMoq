using System.Linq;
using Unity.Netcode;
using UnityEngine;

public class PlayerCharacter : NetworkBehaviour
{
    [SerializeField] public PlayerMotor PlayerMotor; //Don't change at runtime
    public Collider2D PlayerCollider; //Don't change at runtime
    private const int k_GoalLayer = 8;
    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!IsServer)
            return;

        if (other.gameObject.layer != k_GoalLayer)
            return;

        //We hit the goal, finish the player
        if (GameManager.Singleton.PlayersFinished.Contains(this.OwnerClientId))
            return;

        PlayerMotor.RemovePlayerAuthorityServerSided();
        PlayerMotor.SetForImmobile();
        GameManager.Singleton.PlayerFinishedServerSided(OwnerClientId);
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            TimeManager.Singleton.OnAfterPhysicsTick += CheckIfOutOfBounds;
        }
    }

    private void CheckIfOutOfBounds()
    {
        //Ran only on the server

        //Check if we're within the game manager map bounds rect
        var withinBounds = MapManager.Singleton.DeathBounds.Contains(transform.position);
        if(withinBounds)
            return;

        //We're out of bounds, kill the player
        KillPlayerServerSided();
    }

    private void KillPlayerServerSided()
    {
        PlayerMotor.RemovePlayerAuthorityServerSided();
        PlayerMotor.SetForImmobile(); 
        GameManager.Singleton.PlayerDiedServerSided(OwnerClientId);
    }

    [ServerRpc(RequireOwnership = true)]
    public void KillPlayerServerRpc()
    {
        KillPlayerServerSided();
    }
}
