using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public class GizmoWorldObjectsManager : NetworkBehaviour
{
    public static GizmoWorldObjectsManager Singleton { get; private set; }

    [SerializeField] private BuildingArea buildingArea;
    public BuildingArea BuildingArea => buildingArea;
    [SerializeField] private GameObject buildingAreaGridVisual;

    //Key: (Root) Position, Value: GizmoWorldObject
    private Dictionary<Vector2Int, GizmoWorldObject> gizmosInWorld;
    private Dictionary<Vector2Int, GlueGizmoWorldObjectModule> gluesInWorld;
    private HashSet<GizmoWorldObject> glueChildren; //Objects that are a child of a glue

    //Given by the map, these are the spaces taken up by the map
    [SerializeField] private Transform _preoccupiedWorldSpacesParent;
    private Vector2Int[] preoccupiedWorldSpaces;

    //Key: ClientId, Value: GizmoItem
    private Dictionary<ulong, GizmoItem> selectedGizmos;

    private List<PlayerGizmoPlacer> activeGizmoPlacers;
    public IReadOnlyList<PlayerGizmoPlacer> ActiveGizmoPlacers => activeGizmoPlacers;

    public event Action OnAllPlayersFinishedPlacing;

    private void Awake()
    {
        Singleton = this;

        gizmosInWorld = new(30);
        gluesInWorld = new(6);
        glueChildren = new(6);
        selectedGizmos = new(4);
        activeGizmoPlacers = new(4);
        GameManager.Singleton.OnStateChanged += ShowGrid;

        //Map preoccupied world spaces
        preoccupiedWorldSpaces = new Vector2Int[_preoccupiedWorldSpacesParent.childCount];
        for (int i = 0; i < preoccupiedWorldSpaces.Length; i++)
        {
            var child = _preoccupiedWorldSpacesParent.GetChild(i);
            
            //Cache
            var position = child.position;
            
            preoccupiedWorldSpaces[i] = new Vector2Int(Mathf.RoundToInt(position.x), Mathf.RoundToInt(position.y));
        }
    }

    public void AddActiveGizmoPlacer(ulong playerId)
    {
        activeGizmoPlacers.Add(GameManager.Singleton.Players[playerId].PlayerGizmoPlacer);
    }

    private void ShowGrid(GameManager.GameState obj)
    {
        switch (obj)
        {
            case GameManager.GameState.Placing:
                ShowGridClientRpc();
                return;
            case GameManager.GameState.Playing:
                HideGridClientRpc();
                return;
        }
    }

    [ClientRpc]
    private void ShowGridClientRpc()
    {
        buildingAreaGridVisual.SetActive(true);
    }

    [ClientRpc]
    private void HideGridClientRpc()
    {
        buildingAreaGridVisual.SetActive(false);
    }

    /// <summary>
    /// Makes the gizmo the clients selected on the server and makes it his selected in the <see cref="PlayerGizmoPlacer"/>
    /// </summary>
    /// <param name="clientId"></param>
    /// <param name="gizmo"></param>
    public void PlayerSelectedGizmoServerSided(ulong clientId, GizmoItem gizmo)
    {
        selectedGizmos[clientId] = gizmo;
        var player = GameManager.Singleton.Players[clientId];
        player.PlayerGizmoPlacer.PlayerSelectedGizmoClientRpc(gizmo.name);
    }


    [ServerRpc(RequireOwnership = false)]
    public void PlaceGizmoServerRpc(Vector2Int position, int rotationDeg, FixedString128Bytes gizmoName, ServerRpcParams e = default)
    {
        if (GameManager.Singleton.State != GameManager.GameState.Placing)
            return; //Game isn't in placing state

        if(!GizmoBoxManager.Singleton.LoadedGizmoItems.TryGetValue(gizmoName.Value, out var gizmoItem))
            return; // Wrong gizmo name

        if (selectedGizmos[e.Receive.SenderClientId] != gizmoItem)
            return; //Player doesn't have the gizmo

        if(rotationDeg % 90 != 0)
            return; //Rotation isn't a multiple of 90

        //Check if the position is within building area
        if(position.x < buildingArea.LeftBottom.x 
           || position.x > buildingArea.RightTop.x 
           || position.y < buildingArea.LeftBottom.y 
           || position.y > buildingArea.RightTop.y)
            return; //Out of bounds

        //Instantiate it so we can perform world checks, don't spawn it yet
        var gizmoInstance = Instantiate(gizmoItem.WorldPrefab.gameObject);
        var gizmoFromInstance = gizmoInstance.GetComponent<GizmoWorldObject>();

        //Place gizmo
        //First we have to initialize the gizmo with the rotation
        gizmoInstance.transform.position = new Vector3(position.x, position.y);
        gizmoFromInstance.SetRotationDeg(rotationDeg);
        gizmoFromInstance.CalculateOccupiedSpacesByRotation();
        var doesHaveGlueMod = gizmoFromInstance.TryGetComponent<GlueGizmoWorldObjectModule>(out var glueMod);
        if (doesHaveGlueMod)
        {
            //Glue:
            //Check that there is no glue on this place already
            //Check that there is an occupied space below
            //Check that glue isn't inside a block
            //Check that glue isn't connecting the map

            if (gluesInWorld.ContainsKey(position))
            {
                //There is already glue on this place
                Destroy(gizmoInstance);
                return;
            }

            var worldGlueBottom = position + glueMod.LocalGlueBottomOffset;
            var worldGlueTop = position + glueMod.LocalGlueTopOffset;

            var bottomGizmo = GetGizmoOccupyingSpace(worldGlueBottom);
            var topGizmo = GetGizmoOccupyingSpace(worldGlueTop);
            if (bottomGizmo == null && !IsSpaceOccupiedByMap(worldGlueBottom))
            {
                //No gizmo below
                Destroy(gizmoInstance);
                return; 
            }

            if (bottomGizmo == topGizmo)
            {
                if(bottomGizmo != null)
                {
                    //Glue is inside one block
                    Destroy(gizmoInstance);
                    return;
                }

                //Outside gizmos
                if (IsSpaceOccupiedByMap(worldGlueBottom) && IsSpaceOccupiedByMap(worldGlueTop))
                {
                    //Glue connects map
                    Destroy(gizmoInstance);
                    return;
                }
            }
        }
        else
        {
            //Not glue:
            //Check if no gizmo already occupies the space

            var gizmoSpaces = gizmoFromInstance.OccupiedSpacesRotationRelative;
            foreach (var space in gizmoSpaces)
            {
                var spaceToCheck = new Vector2Int(position.x + space.x, position.y + space.y);

                if (GetGizmoOccupyingSpace(spaceToCheck) != null || IsSpaceOccupiedByMap(spaceToCheck))
                {
                    Destroy(gizmoInstance);
                    return; //Space already occupied
                }
            }
        }

        //Add gizmo to corresponding dict
        if (doesHaveGlueMod)
        {
            gluesInWorld.Add(position, glueMod);
        }
        else
        {
            gizmosInWorld.Add(position, gizmoFromInstance);
        }

        //Objects are spawned on the server (so they exist on the clients as well)
        //And their logic is handled by the clients, not the server.
        gizmoFromInstance.NetworkObject.Spawn();

        //Unselect it
        selectedGizmos[e.Receive.SenderClientId] = null;

        //Remove gizmo placer from active
        var player = GameManager.Singleton.Players[e.Receive.SenderClientId];
        activeGizmoPlacers.Remove(player.PlayerGizmoPlacer);

        //Tell the clients the placement was successful
        player.PlayerGizmoPlacer.RespondPlacementWasSuccessfulClientRpc();
        
        //Refocus on active gizmo placers
        CameraManager.Singleton.FocusGizmoPlacersClientRpc(activeGizmoPlacers.NetRefs());

        //Add the gizmo to the player's list of placed gizmos
        GizmoAddedToWorldClientRpc(position, rotationDeg, gizmoInstance);

        //Check if all players are done placing
        if (selectedGizmos.All(g => g.Value == null))
        {
            OnAllPlayersFinishedPlacing?.Invoke();
        }
    }


    [ClientRpc]
    private void GizmoAddedToWorldClientRpc(Vector2Int position, int rotationDeg, NetworkObjectReference instanceNetRef)
    {
        //Calls the OnAddedToWorld event on the placed gizmo
        //Add the gizmo to the player's list of placed gizmos
        //Used so that the clients have the same list of gizmos as the server
        
        var instance = NetworkManager.SpawnManager.SpawnedObjects[instanceNetRef.NetworkObjectId].gameObject;
        var gizmo = instance.GetComponent<GizmoWorldObject>();
        var doesHaveGlueMod = gizmo.TryGetComponent<GlueGizmoWorldObjectModule>(out var glueMod);

        if (!IsHost)
        {
            //Host already added it and rotated it from server side
            gizmo.SetRotationDeg(rotationDeg);
            gizmo.CalculateOccupiedSpacesByRotation();

            //Add gizmo to corresponding dict
            if (doesHaveGlueMod)
            {
                gluesInWorld.Add(position, glueMod);
            }
            else
            {
                gizmosInWorld.Add(position, gizmo);
            }
        }
        
        if (doesHaveGlueMod)
        {
            //Glue:
            //Make glue the child of the object below it
            //There is always something below the glue, otherwise this method isn't called

            var localGlueBottom = glueMod.LocalGlueBottomOffset;
            var worldGlueBottom = position + localGlueBottom;
            var gizmoBelow = GetGizmoOccupyingSpace(worldGlueBottom);

            var localGlueTop = glueMod.LocalGlueTopOffset;
            var worldGlueTop = position + localGlueTop;
            var gizmoAbove = GetGizmoOccupyingSpace(worldGlueTop);

            if (gizmoAbove != null)
            {
                // There is a gizmo above glue
                
                TryConnectToGlue(gizmoAbove, position, instance.transform);
            }

            if (gizmoBelow != null)
            {
                //There is a gizmo below glue

                var glueParent = gizmoBelow.GetGlueParent(position);
                instance.transform.SetParent(glueParent, true);
            }
        }
        else
        {
            //Not glue:
            //Check if there is glue whoms top touches any of this objects occupying spaces
            var connectedGlues = gluesInWorld
                .Where(g => 
                    gizmo.OccupiedSpacesRotationRelative.Any(o 
                        => position + o == g.Key + g.Value.LocalGlueTopOffset));
            foreach (var connectedGlue in connectedGlues)
            {
                if (TryConnectToGlue(gizmo, connectedGlue.Key, connectedGlue.Value.transform))
                    break;
            }
        }

        gizmo.OnAddedToWorld.Invoke(); //Invoke on host aswell
    }

    /// <summary>
    /// Makes gizmo a child of the glue and adds to <see cref="glueChildren"/> dict
    /// </summary>
    /// <param name="gizmo"></param>
    /// <param name="gluePosition"></param>
    /// <param name="toGlue"></param>
    /// <returns>True if glued, False if could not glue</returns>
    private bool TryConnectToGlue(GizmoWorldObject gizmo, Vector2Int gluePosition, Transform toGlue)
    {
        var glueChild = gizmo.GetChildForGlue(gluePosition);
        if (glueChild == null)
            return false;

        // If the gizmo that we think should be a child to this glue
        // is already a child to another glue then the gizmo should
        // actually be the parent of the glue
        if (glueChildren.Contains(gizmo))
            return false; //Cannot re-glue a glued gizmo

        //Make it child of the glue
        glueChild.SetParent(toGlue, true);
        glueChildren.Add(gizmo);
        return true;
    }

    [CanBeNull]
    private GizmoWorldObject GetGizmoOccupyingSpace(Vector2Int worldSpace)
    {
        return gizmosInWorld.FirstOrDefault(gInW =>
                       gInW.Value.OccupiedSpacesRotationRelative.Any(gInWOS => gInWOS + gInW.Key == worldSpace)).Value;
    }

    private bool IsSpaceOccupiedByMap(Vector2Int worldSpace)
    {
        return preoccupiedWorldSpaces.Any(pws => pws == worldSpace);
    }


    [ClientRpc]
    public void FirePlayingStartedClientRpc()
    {
        foreach (var gizmoWorldObject in gizmosInWorld)
        {
            gizmoWorldObject.Value.OnPlayingStarted.Invoke();
        }
    }

    [ClientRpc]
    public void FirePlayingEndedClientRpc()
    {
        foreach (var gizmoWorldObject in gizmosInWorld)
        {
            gizmoWorldObject.Value.OnPlayingEnded.Invoke();
        }
    }

    private void OnDrawGizmos()
    {
        //Draw building area 
        Gizmos.color = Color.blue;

        Gizmos.DrawLine(new Vector2(buildingArea.LeftBottom.x, buildingArea.LeftBottom.y), new Vector2(buildingArea.RightTop.x, buildingArea.LeftBottom.y));
        Gizmos.DrawLine(new Vector2(buildingArea.RightTop.x, buildingArea.LeftBottom.y), new Vector2(buildingArea.RightTop.x, buildingArea.RightTop.y));
        Gizmos.DrawLine(new Vector2(buildingArea.RightTop.x, buildingArea.RightTop.y), new Vector2(buildingArea.LeftBottom.x, buildingArea.RightTop.y));
        Gizmos.DrawLine(new Vector2(buildingArea.LeftBottom.x, buildingArea.RightTop.y), new Vector2(buildingArea.LeftBottom.x, buildingArea.LeftBottom.y));

        //Draw preoccupied spaces
        Gizmos.color = Color.red;

        for (int i = 0; i < _preoccupiedWorldSpacesParent.childCount; i++)
        {
            var preoccupiedWorldSpaceTransform = _preoccupiedWorldSpacesParent.GetChild(i);
            var occupiedSpaceInt = new Vector3Int(Mathf.RoundToInt(preoccupiedWorldSpaceTransform.position.x), Mathf.RoundToInt(preoccupiedWorldSpaceTransform.position.y));
            Gizmos.DrawWireCube(occupiedSpaceInt, Vector3.one);
        }
    }
}

[Serializable]
public struct BuildingArea // Dont change this at runtime
{
    public Vector2Int LeftBottom;
    public Vector2Int RightTop;
}