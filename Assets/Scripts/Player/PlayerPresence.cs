using System;
using Unity.Netcode;
using UnityEngine;

public class PlayerPresence : NetworkBehaviour
{
    /// <summary>
    /// On owner client
    /// </summary>
    public static PlayerPresence LocalPlayer;

    /// <summary>
    /// On server & clients (all clients)
    /// </summary>
    public PlayerCharacter PlayerCharacter { get; private set; }
    [SerializeField] private PlayerCharacter[] playerCharacterPrefabs;

    public PlayerGizmoPlacer PlayerGizmoPlacer { get; private set; }
    [SerializeField] private PlayerGizmoPlacer playerGizmoPlacerPrefab;

    public override void OnNetworkSpawn()
    {
        if (!IsOwner)
            return;
        
        LocalPlayer = this;
    }

    /// <summary>
    /// Instantiates a new player character with ownership of this client
    /// </summary>
    /// <param name="characterIndex">Index of the character prefab in the playerCharacterPrefabs array, the players picked character</param>
    public void SpawnPlayerCharacterServerSided(int characterIndex)
    {
        var playerCharacterPrefab = playerCharacterPrefabs[characterIndex];
        var instance = Instantiate(playerCharacterPrefab.gameObject);
        PlayerCharacter = instance.GetComponent<PlayerCharacter>();
        PlayerCharacter.NetworkObject.SpawnWithOwnership(OwnerClientId);
        SyncCharacterRefClientRpc(PlayerCharacter);
    }

    [ClientRpc]
    private void SyncCharacterRefClientRpc(NetworkBehaviourReference characterNetRef)
    {
        if (IsHost)
            return; //Already has this from server

        var res = characterNetRef.TryGet(out PlayerCharacter character);
        if (!res)
            throw new Exception("Wtf");

        PlayerCharacter = character;
    }


    /// <summary>
    /// Instantiates a new player gizmo placer with ownership of this client
    /// </summary>
    public void SpawnPlayerGizmoPlacerServerSided()
    {
        var instance = Instantiate(playerGizmoPlacerPrefab.gameObject);
        PlayerGizmoPlacer = instance.GetComponent<PlayerGizmoPlacer>();
        PlayerGizmoPlacer.NetworkObject.SpawnWithOwnership(OwnerClientId);
    }
}