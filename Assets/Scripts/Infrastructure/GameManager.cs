using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using Unity.VisualScripting;
using UnityEngine;
using Random = UnityEngine.Random;

public class GameManager : NetworkBehaviour
{
    public static GameManager Singleton { get; private set; }
    private bool isGameRunning;

    [SerializeField] private PlayerPresence playerPrefab;

    /// <summary>
    /// Synced on server & clients (clients can only read data... obviously)
    /// Initialized on game start
    /// Key: ClientId, Value: PlayerPresence
    /// </summary>
    private Dictionary<ulong, PlayerPresence> players;
    public IReadOnlyDictionary<ulong, PlayerPresence> Players => players;

    private List<ulong> playersFinished; //Synced on the server
    public IReadOnlyList<ulong> PlayersFinished => playersFinished;

    private List<ulong> playersDead;//Synced on the server

    /// <summary>
    /// Not finished and not dead players
    /// </summary>
    private IEnumerable<PlayerPresence> ActivePlayers => players.Values
        .Where(p => !playersFinished.Contains(p.OwnerClientId) && !playersDead.Contains(p.OwnerClientId));
    public GameState State { get; private set; } = GameState.WaitingForStart;
    public event Action<GameState> OnStateChanged;

    private void Awake()
    {
        Singleton = this;
        players = new(4);
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer)
            return;

        playersFinished = new(4);
        playersDead = new(4);

        GizmoBoxManager.Singleton.OnAllPlayersFinishedShoppingServer += StartPlacingState;
        PointCountTableManager.Singleton.OnAllPlayersFinishedAnimation += OnAllPlayersFinishedPointCounting;
        GizmoWorldObjectsManager.Singleton.OnAllPlayersFinishedPlacing += StartPlayingState;
    }

    private void OnAllPlayersFinishedPointCounting()
    {
        var winner = PointCountTableManager.Singleton.WinnerClientId;
        if (winner is null)
        {
            StartGizmosBoxState();
            return;
        }

        //There is a winner, end the game
        //Todo:
        State = GameState.End;
        OnStateChanged?.Invoke(State);
    }

    private void StartPlacingState()
    {
        State = GameState.Placing;
        OnStateChanged?.Invoke(State);

        //Tell the players its time to place
        foreach (var player in players.Values)
        {
            GizmoWorldObjectsManager.Singleton.AddActiveGizmoPlacer(player.OwnerClientId);
            player.PlayerGizmoPlacer.StartPlacingStateClientRpc();
        }

        //Focus camera on gizmo placers
        CameraManager.Singleton.FocusGizmoPlacersClientRpc(GizmoWorldObjectsManager.Singleton.ActiveGizmoPlacers.NetRefs());
    }

    public void StartGameServerSided()
    {
        if (!IsServer)
            return;

        //Initialize players
        for (var i = 0; i < NetworkManager.Singleton.ConnectedClientsList.Count; i++)
        {
            var client = NetworkManager.Singleton.ConnectedClientsList[i];

            var playerInstance = Instantiate(playerPrefab);
            var player = playerInstance.GetComponent<PlayerPresence>();
            player.NetworkObject.SpawnAsPlayerObject(client.ClientId);

            player.SpawnPlayerCharacterServerSided(i);
            player.SpawnPlayerGizmoPlacerServerSided();

            players.Add(client.ClientId, player);
        }

        //Sync player refs
        SyncPlayersRefClientRpc(players.Select(p => (NetworkBehaviourReference)p.Value).ToArray());

        PointCountTableManager.Singleton.InitializeCollectionsServerSided();

        //Start timer
        TimeManager.Singleton.StartTimerServerSided();

        StartPlayingState();
        StartGameClientRpc();
    }

    [ClientRpc]
    private void SyncPlayersRefClientRpc(NetworkBehaviourReference[] playersNetRef)
    {
        if (IsHost)
            return;

        foreach (var player in playersNetRef)
        {
            var res = player.TryGet(out PlayerPresence playerPresence);
            if (!res)
                throw new Exception("Wtf");

            players.Add(playerPresence.OwnerClientId, playerPresence);
        }
    }

    [ClientRpc]
    private void StartGameClientRpc()
    {
        isGameRunning = true;
    }

    public void PlayerFinishedServerSided(ulong clientId)
    {
        if (playersDead.Contains(clientId))
            return;

        if (playersFinished.Contains(clientId))
            return;

        playersFinished.Add(clientId);

        //If all clients finished, end playing
        if (playersFinished.Count + playersDead.Count == NetworkManager.Singleton.ConnectedClientsList.Count)
        {
            StartPlayingEndedState();
            return;
        } 

        //Re-focus camera
        CameraManager.Singleton.FocusActivePlayersClientRpc(ActivePlayers.NetRefs());
    }

    public void PlayerDiedServerSided(ulong clientId)
    {
        if (playersFinished.Contains(clientId))
            return;

        if (playersDead.Contains(clientId))
            return;

        playersDead.Add(clientId);

        //If all clients finished, end playing
        if (playersFinished.Count + playersDead.Count == NetworkManager.Singleton.ConnectedClientsList.Count)
        {
            StartPlayingEndedState();
            return;
        }

        //Re-focus active players
        CameraManager.Singleton.FocusActivePlayersClientRpc(ActivePlayers.NetRefs());
    }

    private void StartPlayingState()
    {
        //Reset finished and dead collections
        //Move players to spawn points
        //Give them the ability to move

        State = GameState.Playing;
        OnStateChanged?.Invoke(State);

        playersFinished.Clear();
        playersDead.Clear();

        //Spawn players at random spawn points and enable movement
        var availableSpawnPoints = new List<Transform>(MapManager.Singleton.SpawnPoints);
        foreach (var player in players.Values)
        {
            var spawnPointI = Random.Range(0, availableSpawnPoints.Count);
            var spawnPoint = availableSpawnPoints[spawnPointI];
            availableSpawnPoints.RemoveAt(spawnPointI);

            player.PlayerCharacter.PlayerMotor.SetForMovement();
            player.PlayerCharacter.PlayerMotor.SetStateServerSided(new(spawnPoint.position, Vector2.zero, true));
            player.PlayerCharacter.PlayerMotor.EnablePlayerAuthorityServerSided();
        }

        //Re-focus camera
        CameraManager.Singleton.FocusActivePlayersClientRpc(players.Values.NetRefs());

        //Start gizmos
        GizmoWorldObjectsManager.Singleton.FirePlayingStartedClientRpc();
    }

    private void StartPlayingEndedState()
    {
        State = GameState.PlayingEnded;
        OnStateChanged?.Invoke(State);

        //Stop gizmos
        GizmoWorldObjectsManager.Singleton.FirePlayingEndedClientRpc();

        //Disable movement
        foreach (var player in players.Values)
        {
            player.PlayerCharacter.PlayerMotor.RemovePlayerAuthorityServerSided();
        }

        //Focus finish
        CameraManager.Singleton.FocusGoalClientRpc();

        //Move to point counting
        StartPointCountingState();
    }

    private void StartPointCountingState()
    {
        State = GameState.PointCounting;
        OnStateChanged?.Invoke(State);

        //Create records
        var pointRecords = new PointCountTableManager.PlayerPointsRecord[players.Count];
        var wasSingleWinner = playersFinished.Count == 1;
        ulong? clientFinishedFirst = playersFinished.Count > 0
            ? playersFinished[0]
            : null;

        for (var i = 0; i < pointRecords.Length; i++)
        {
            var forClient = players.Values.AsReadOnlyList()[i].OwnerClientId;

            pointRecords[i] = new(
                playersFinished.Contains(forClient),
                wasSingleWinner,
                clientFinishedFirst.HasValue && clientFinishedFirst.Value == forClient,
                0, //Todo: implement playersKilled
                forClient
            );
        }

        //Count and show table
        PointCountTableManager.Singleton.StartCountingProcessServerSided(pointRecords);
    }

    /// <summary>
    /// Sets the state and opens the gizmo box
    /// </summary>
    private void StartGizmosBoxState()
    {
        State = GameState.GizmosBox;
        OnStateChanged?.Invoke(State);

        CameraManager.Singleton.FocusMapClientRpc();
        GizmoBoxManager.Singleton.OpenServerSided(-1);
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private void OnGUI()
    {
        if (IsServer)
        {
            if (!isGameRunning && GUI.Button(new Rect(0, 0, 100, 50), "Start game"))
            {
                StartGameServerSided();
            }

            if (isGameRunning && GUI.Button(new Rect(0, 0, 100, 50), "Open box D"))
            {
                //Debug open gizmo box
                //  1. End playing
                //  -> Skip point counting
                //  2. Open gizmo box

                //End playing:

                State = GameState.PlayingEnded;
                OnStateChanged?.Invoke(State);

                //Stop gizmos
                GizmoWorldObjectsManager.Singleton.FirePlayingEndedClientRpc();

                //Disable movement
                foreach (var player in players.Values)
                {
                    player.PlayerCharacter.PlayerMotor.RemovePlayerAuthorityServerSided();
                }


                //Open box:

                State = GameState.GizmosBox;
                OnStateChanged?.Invoke(State);
                
                GizmoBoxManager.Singleton.OpenWithDebugServerSided();
            }
        }
    }
#endif

    public enum GameState
    {
        WaitingForStart,
        Playing,
        PlayingEnded,
        PointCounting,
        GizmosBox,
        Placing,
        End
    }
}

public static class NetworkingExtensions 
{
    /// <summary>
    /// Helper method to create network behaviour refs from an object array
    /// </summary>
    public static NetworkBehaviourReference[] NetRefs<T>(this IEnumerable<T> objects)
        where T : NetworkBehaviour
        => objects.Select(o => (NetworkBehaviourReference)o).ToArray();
}