using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DG.Tweening;
using Unity.Collections;
using Unity.Netcode;
using Unity.VisualScripting;
using UnityEngine;
using Random = UnityEngine.Random;

/// <summary>
/// Loads gizmos from resources. Shows the box with gizmos, let's players select gizmos from the box and then makes it their selected in the <see cref="GizmoWorldObjectsManager"/>
/// </summary>
public class GizmoBoxManager : NetworkBehaviour
{
    public static GizmoBoxManager Singleton { get; private set; }

    private Dictionary<string, GizmoItem> loadedGizmoItems;
    public IReadOnlyDictionary<string, GizmoItem> LoadedGizmoItems => loadedGizmoItems;

    private const int k_MaxGizmosInBox = 7; //Inclusive
    private const int k_MinGizmosInBox = 4;

    //Key: GizmoId, Value: GizmoBoxItem
    private Dictionary<int, GizmoBoxItem> gizmoBoxItemsClient; //The gizmo box items that are currently displayed
    public IReadOnlyDictionary<int, GizmoBoxItem> GizmoBoxItemsClient => gizmoBoxItemsClient;

    //Key: GizmoId, Value: GizmoBoxItem
    private Dictionary<int, string> gizmoBoxItemsServer;
    private HashSet<ulong> clientsThatSelectedGizmo;
    
    //[SerializeField] private RectTransform gizmosBoxParent; // The parent of gizmo box item objects

    [SerializeField] private RectTransform closedBox;
    [SerializeField] private RectTransform openBox; //Parent of gizmo box item objects
    [SerializeField] private GameObject boxPoofParticlesPrefab;
    [SerializeField] private GameObject itemPoofParticlesPrefab; 

    public event Action OnAllPlayersFinishedShoppingServer; 

    private void Awake()
    {
        Singleton = this;

        LoadGizmos();
        gizmoBoxItemsClient = new(k_MaxGizmosInBox);
        gizmoBoxItemsServer = new(k_MaxGizmosInBox);
        clientsThatSelectedGizmo = new(4);
    }

    /// <summary>
    /// Load gizmos from resources into the <see cref="loadedGizmoItems"/> dictionary
    /// and also registers the gizmo world prefabs with the <see cref="NetworkManager"/>
    /// </summary>
    /// <exception cref="FileLoadException"></exception>
    private void LoadGizmos()
    {
        loadedGizmoItems = new();
        var gizmos = Resources.LoadAll<GizmoItem>("Gizmos");

        if (gizmos is null)
            throw new FileLoadException("Couldn't load gizmos from resources");

        foreach (var gizmo in gizmos)
        {
            loadedGizmoItems.Add(gizmo.name, gizmo);
            NetworkManager.Singleton.AddNetworkPrefab(gizmo.WorldPrefab.gameObject);
        }
    }


    /// <summary>
    /// Initialize gizmos on the server and tell clients to draw them, so that they can select them
    /// </summary>
    /// <param name="round">The game round we are at</param>
    public void OpenServerSided(int round)
    {
        var itemsNames = new string[Random.Range(k_MinGizmosInBox, k_MaxGizmosInBox + 1)]; //+1 to make it inclusive

        var itemNamesSerializable = new FixedString128Bytes[itemsNames.Length];
        //var positions = new PoissonDiscSampler(1, 1, 0.5f)
        //    .Samples()
        //    .Take(itemsNames.Length)
        //    .ToArray();
        var positions = new Vector2[itemsNames.Length];
        var ids = new int[itemsNames.Length];
        var idCounter = 0;

        for (int i = 0; i < itemsNames.Length; i++)
        {
            //Select a random gizmo
            itemsNames[i] = loadedGizmoItems.Keys.AsReadOnlyList()[Random.Range(0, loadedGizmoItems.Count)];
            itemNamesSerializable[i] = itemsNames[i];

            //Position
            positions[i] = new Vector2(Random.Range(0f, 1f), Random.Range(0f, 1f));

            //Id
            ids[i] = idCounter;
            idCounter++;

            //Add to server list
            gizmoBoxItemsServer.Add(ids[i], itemsNames[i]);
        }

        //Send to all clients
        OpenClientRpc(itemNamesSerializable, positions, ids);
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public void OpenWithDebugServerSided()
    {
        var itemsNames = LoadedGizmoItems.Keys.ToArray();

        var itemNamesSerializable = new FixedString128Bytes[itemsNames.Length];
        var positions = new Vector2[itemsNames.Length];
        var ids = new int[itemsNames.Length];
        var idCounter = 0;

        for (int i = 0; i < itemsNames.Length; i++)
        {
            //Serialize
            itemNamesSerializable[i] = itemsNames[i];

            //Position (distributed evenly along a grid)
            positions[i] = new Vector2((i % (float)itemsNames.Length) / itemsNames.Length, i / (float)itemsNames.Length);

            //Id
            ids[i] = idCounter;
            idCounter++;

            //Add to server list
            gizmoBoxItemsServer.Add(ids[i], itemsNames[i]);
        }

        //Send to all clients
        OpenClientRpc(itemNamesSerializable, positions, ids);
    }
#endif

    /// <summary>
    /// Show the box and fill it with gizmos, that clients can select. This is only "rendering" and runs on the clients.
    /// </summary>
    /// <param name="gizmoNames"></param>
    /// <param name="positionNormalized">A Position where it's components range from 0 to 1, 0 being left or bottom and 1 being right or top</param>
    /// <param name="gizmoIds"></param>
    [ClientRpc]
    private void OpenClientRpc(FixedString128Bytes[] gizmoNames, Vector2[] positionNormalized, int[] gizmoIds)
    {
        //Pre create instance
        for (int i = 0; i < gizmoNames.Length; i++)
        {
            var gizmoItem = loadedGizmoItems[gizmoNames[i].Value];
            var gizmoId = gizmoIds[i];

            var gizmosBoxRect = openBox.rect; //Cache
            const float boxPaddingV = 200;
            const float boxPaddingH = 360;
            var localPosition = new Vector2(
                Mathf.Lerp(gizmosBoxRect.xMin + boxPaddingH, gizmosBoxRect.xMax - boxPaddingH, positionNormalized[i].x),
                Mathf.Lerp(gizmosBoxRect.yMin + boxPaddingV, gizmosBoxRect.yMax - boxPaddingV, positionNormalized[i].y));
            //var rotation = Quaternion.Euler(0, 0, Random.Range(-20f, 20f));

            var gizmoInstance = Instantiate(gizmoItem.BoxItemPrefab.gameObject, openBox);
            var gizmoInstanceRect = gizmoInstance.GetComponent<RectTransform>();
            
            gizmoInstanceRect.anchoredPosition = localPosition;
            //gizmoInstanceRect.rotation = rotation;
            gizmoInstanceRect.localScale = Vector3.one * 0.5f;

            var gizmoFromInstance = gizmoInstance.GetComponent<GizmoBoxItem>();
            gizmoFromInstance.Init(gizmoId);
            gizmoBoxItemsClient.Add(gizmoId, gizmoFromInstance);
        }

        //Show the box animation
        StartCoroutine(ShowBoxOnClientAnimationEnumerator());
    }

    /// <summary>
    /// Show the box visual and play opening animation
    /// </summary>
    private IEnumerator ShowBoxOnClientAnimationEnumerator()
    {
        //First play box shake animation
        //Then poof
        //Then display open box with gizmos

        closedBox.gameObject.SetActive(true);

        yield return closedBox
            .DOShakeRotation(1.4f)
            .Play()
            .WaitForCompletion();

        yield return new WaitForSeconds(.2f);

        Instantiate(boxPoofParticlesPrefab, closedBox.position, Quaternion.identity);

        closedBox.gameObject.SetActive(false);
        openBox.gameObject.SetActive(true);
    }

    /// <summary>
    /// The client calls this when he wants to select an item from the box.
    /// It is removed from the server collection, made the players selected gizmo and calls <see cref="RemoveGizmoFromScreenClientRpc"/>
    /// </summary>
    /// <param name="gizmoId">The id of the <see cref="GizmoBoxItem"/></param>
    /// <param name="e"></param>
    [ServerRpc(RequireOwnership = false)]
    public void PlayerSelectGizmoServerRpc(int gizmoId, ServerRpcParams e = default)
    {
        var senderClientId = e.Receive.SenderClientId;

        if(clientsThatSelectedGizmo.Contains(senderClientId))
            return; //This client already selected a gizmo

        if(!gizmoBoxItemsServer.TryGetValue(gizmoId, out var boxItemName))
            return; //Someone already selected this item or it doesn't exist

        //Set the item as the players selected gizmo
        GizmoWorldObjectsManager.Singleton.PlayerSelectedGizmoServerSided(senderClientId, loadedGizmoItems[boxItemName]);

        //Remove the item from the box
        gizmoBoxItemsServer.Remove(gizmoId);
        RemoveGizmoFromScreenClientRpc(gizmoId);

        //Make the item the players selected
        clientsThatSelectedGizmo.Add(senderClientId);

        //Check if all players have selected their gizmo and hide the box
        if (clientsThatSelectedGizmo.Count == NetworkManager.Singleton.ConnectedClientsList.Count)
        {
            //Reset so that the box can be opened again
            clientsThatSelectedGizmo.Clear();
            gizmoBoxItemsServer.Clear();

            //Hide the box
            HideBoxClientRpc();

            OnAllPlayersFinishedShoppingServer?.Invoke();
        }
    }

    /// <summary>
    /// Hide the box visual and play closing animation
    /// </summary>
    [ClientRpc]
    private void HideBoxClientRpc()
    {
        // Delete all gizmo box item objects
        foreach (var boxItem in gizmoBoxItemsClient.Values)
        {
            Destroy(boxItem.gameObject);
        }

        // Clear the list so we can get correct view next time
        gizmoBoxItemsClient.Clear();

        // Hide the box
        openBox.gameObject.SetActive(false);
    }

    /// <summary>
    /// Remove the gizmo from the box on the client, so that other players don't try to select it anymore
    /// </summary>
    /// <param name="gizmoId"></param>
    [ClientRpc]
    private void RemoveGizmoFromScreenClientRpc(int gizmoId)
    {
        var item = gizmoBoxItemsClient[gizmoId];
        
        //Show poof particles
        Instantiate(itemPoofParticlesPrefab, item.transform.position, Quaternion.identity);
        Destroy(item.gameObject);

        gizmoBoxItemsClient.Remove(gizmoId);
    }
}

