using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;


public class PointCountTableManager : NetworkBehaviour
{
    public static PointCountTableManager Singleton { get; private set; }

    //Server concerns:

    [Header("Point stats")]
    [SerializeField] private int pointsToWin = 16;

    [SerializeField] private int pointsForFinish = 4; //Points for finishing
    [SerializeField] private int pointsForUnderdog = 4; //Points for when you have less than a half of the best player

    //You can only get points either for being single or for being first, not both
    [SerializeField] private int pointsForSingle = 3; //Points for being the only one to finish
    [SerializeField] private int pointsForFirst = 2; //Points for being the first to finish

    [SerializeField] private int pointsForTrap = 1; //Points for killing someone. You can only get points for traps if someone finished the game

    private HashSet<ulong> playersWithFinishedAnimation;
    public event Action OnAllPlayersFinishedAnimation;

    //Shared concerns:
    //Key: ClientId, Value: Points
    private Dictionary<ulong, int> playerPoints;

    public ulong? WinnerClientId;

    //Client concerns:
    [Header("UI")] 
    [SerializeField] private RectTransform ProfilesParent;
    [SerializeField] private GameObject ProfilePrefab;

    [SerializeField] private RectTransform PointChartsParent;

    [SerializeField] private GameObject PointChartPrefab;

    //Key: ClientId, Value: PointChartParent
    private Dictionary<ulong, RectTransform> pointCharts;
    [SerializeField] private RectTransform PointChartItemPrefab;

    [SerializeField] private CanvasGroup PointsTableCG;

    private void Awake()
    {
        Singleton = this;
    }

    /// <summary>
    /// Call this on game start to initialize internal collections. Also calls the clients to initialize their table (and collections).
    /// </summary>
    public void InitializeCollectionsServerSided()
    {
        if (!IsServer)
            return;

        playersWithFinishedAnimation = new(GameManager.Singleton.Players.Count);
        playerPoints = new(GameManager.Singleton.Players.Count);
        foreach (var player in GameManager.Singleton.Players)
        {
            playerPoints.Add(player.Key, 0);
        }

        InitializeTableClientRpc(GameManager.Singleton.Players.Keys.ToArray());
    }

    [ClientRpc]
    private void InitializeTableClientRpc(ulong[] clientIds)
    {
        playerPoints = new(clientIds.Length);
        foreach (var clientId in clientIds)
        {
            playerPoints.Add(clientId, 0);
        }

        pointCharts = new(clientIds.Length);
        foreach (var clientId in clientIds)
        {
            //Point chart
            var pointChart = Instantiate(PointChartPrefab, PointChartsParent).GetComponent<RectTransform>();
            pointCharts.Add(clientId, pointChart);

            //Profile
            Instantiate(ProfilePrefab, ProfilesParent);
        }
    }

    /// <summary>
    /// Counts the points of all players and then tells the clients to play out the point animation.
    /// Raises an event when all players finish seeing the animation. <b>Sets the <see cref="WinnerClientId"/> parameter if some1 won</b>.
    /// Keeps the track of the points of all players.
    /// </summary>
    /// <param name="pointRecords"></param>
    public void StartCountingProcessServerSided(PlayerPointsRecord[] pointRecords)
    {
        if (!IsServer)
            return;

        //Pre-count all points and create animation DTOs
        var underdogs = GetUnderdogClients();
        var didAnyoneFinish = pointRecords.Any(p => p.DidFinish);

        var animationDTOs = new PlayerPointRecordAnimationDTO[pointRecords.Length];

        for (var i = 0; i < pointRecords.Length; i++)
        {
            var pointRecord = pointRecords[i];
            var isClientUnderdog = underdogs.Contains(pointRecord.ClientId);
            var pointsSum = 0;

            if (pointRecord.DidFinish)
            {
                pointsSum += pointsForFinish;
                if (isClientUnderdog)
                {
                    pointsSum += pointsForUnderdog;
                }

                if (pointRecord.WasSingle)
                {
                    pointsSum += pointsForSingle;
                }
                else if (pointRecord.WasFirst)
                {
                    pointsSum += pointsForFirst;
                }
            }

            if (didAnyoneFinish)
            {
                pointsSum += pointRecord.PlayersKilled * pointsForTrap;
            }

            playerPoints[pointRecord.ClientId] += pointsSum;

            animationDTOs[i] = new(pointRecord.ClientId, pointRecord.DidFinish, pointRecord.WasSingle,
                pointRecord.WasFirst, pointRecord.PlayersKilled, isClientUnderdog);
        }

        //Check if someone won, the winner is the one with the most points
        var bestPlayer = playerPoints.Aggregate((l, r) => l.Value > r.Value ? l : r);
        if (bestPlayer.Value >= pointsToWin)
        {
            WinnerClientId = bestPlayer.Key;
        }

        StartCountingPointsClientRpc(animationDTOs);
    }

    private ulong[] GetUnderdogClients()
    {
        var bestPlayerPoints = playerPoints.Values.Max();
        return playerPoints
            .Where(p => p.Value < bestPlayerPoints / 2)
            .Select(x => x.Key)
            .ToArray();
    }

    [ServerRpc(RequireOwnership = false)]
    private void PlayerFinishedAnimationServerRpc(ServerRpcParams e = default)
    {
        var clientId = e.Receive.SenderClientId;
        if (playersWithFinishedAnimation.Contains(clientId))
            return;

        playersWithFinishedAnimation.Add(clientId);

        //If all players finished animation, raise the event and clear the collection
        if (playersWithFinishedAnimation.Count == GameManager.Singleton.Players.Count)
        {
            playersWithFinishedAnimation.Clear();
            OnAllPlayersFinishedAnimation?.Invoke();
        }
    }

    [ClientRpc]
    private void StartCountingPointsClientRpc(PlayerPointRecordAnimationDTO[] pointRecords)
    {
        PointsTableCG.alpha = 1;
        StartCoroutine(PlayPointsAnimationEnumerator(pointRecords));
    }

    private IEnumerator PlayPointsAnimationEnumerator(PlayerPointRecordAnimationDTO[] pointRecords)
    {
        //We count points in phases
        //Phase 1: add points for finish to everyone
        //Phase 2: add points for underdogs
        //Phase 3: add points for being the only one to finish or for being the first to finish
        //Phase 4: add points for traps

        //Then we tell the server that this client is done

        const float phaseDuration = 1f;
        const float nextPhaseDelay = 0.1f;

        var wasSingle = pointRecords.Any(p => p.WasSingle);
        var didAnyoneFinish = pointRecords.Any(p => p.DidFinish);

        void CreateChartItem(ulong clientId, float pointValue)
        {
            var pointChart = pointCharts[clientId];
            var pointChartItemInstance =
                Instantiate(PointChartItemPrefab.gameObject, pointChart);
            var rectTransform = pointChartItemInstance.GetComponent<RectTransform>();
            var chartItemWidth = pointValue / (float) pointsToWin * pointChart.rect.width;
            rectTransform.sizeDelta = new Vector2(chartItemWidth, rectTransform.sizeDelta.y);
        }

        //Phase 1 - add points for finish to everyone
        foreach (var pointRecord in pointRecords)
        {
            if (!pointRecord.DidFinish)
                continue;

            CreateChartItem(pointRecord.ClientId, pointsForFinish);
        }

        yield return new WaitForSeconds(nextPhaseDelay);

        //Phase 2 - add points for underdogs
        foreach (var pointRecord in pointRecords)
        {
            if (!pointRecord.WasUnderdog)
                continue;

            CreateChartItem(pointRecord.ClientId, pointsForUnderdog);
        }

        yield return new WaitForSeconds(nextPhaseDelay);

        //Phase 3 - add points for being the only one to finish
        if (wasSingle)
        {
            foreach (var pointRecord in pointRecords)
            {
                if (!pointRecord.WasSingle)
                    continue;

                CreateChartItem(pointRecord.ClientId, pointsForSingle);
            }
        }
        else
        {
            foreach (var pointRecord in pointRecords)
            {
                if (!pointRecord.WasFirst)
                    continue;

                CreateChartItem(pointRecord.ClientId, pointsForFirst);
            }
        }
        yield return new WaitForSeconds(nextPhaseDelay);

        //Phase 4 - add points for traps
        foreach (var pointRecord in pointRecords)
        {
            for (int i = 0; i < pointRecord.PlayersKilled; i++)
            {
                CreateChartItem(pointRecord.ClientId, pointsForTrap);
                yield return new WaitForSeconds(nextPhaseDelay / 2f);
            }

        }

        yield return new WaitForSeconds(nextPhaseDelay);

        PointsTableCG.alpha = 0;
        PlayerFinishedAnimationServerRpc();
    }

    public readonly struct PlayerPointsRecord
    {
        public readonly ulong ClientId;

        public readonly bool DidFinish;
        public readonly bool WasSingle;
        public readonly bool WasFirst;
        public readonly int PlayersKilled;
        //IsUnderdog is calculated separately

        public PlayerPointsRecord(bool didFinish, bool wasSingle, bool wasFirst, int playersKilled, ulong clientId)
        {
            DidFinish = didFinish;
            WasSingle = wasSingle;
            WasFirst = wasFirst;
            PlayersKilled = playersKilled;
            ClientId = clientId;
        }
    }

    /// <summary>
    /// This is a DTO for the <see cref="PlayerPointsRecord"/>. It is used for simplifying the animation process.
    /// </summary>
    private struct PlayerPointRecordAnimationDTO : INetworkSerializable
    {
        public ulong ClientId;

        public bool DidFinish;
        public bool WasSingle;
        public bool WasFirst;
        public int PlayersKilled;
        public bool WasUnderdog;

        public PlayerPointRecordAnimationDTO(ulong clientId, bool didFinish, bool wasSingle, bool wasFirst,
            int playersKilled, bool wasUnderdog)
        {
            ClientId = clientId;
            DidFinish = didFinish;
            WasSingle = wasSingle;
            WasFirst = wasFirst;
            PlayersKilled = playersKilled;
            WasUnderdog = wasUnderdog;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ClientId);
            serializer.SerializeValue(ref DidFinish);
            serializer.SerializeValue(ref WasSingle);
            serializer.SerializeValue(ref WasFirst);
            serializer.SerializeValue(ref PlayersKilled);
            serializer.SerializeValue(ref WasUnderdog);
        }
    }
}