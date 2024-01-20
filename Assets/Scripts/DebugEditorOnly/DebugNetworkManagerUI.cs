using Unity.Netcode;
using UnityEngine;

public class DebugNetworkManagerUI : MonoBehaviour
{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private void OnGUI()
    {
        if (GUI.Button(new Rect(0, 0, 100, 50), "Start host"))
        {
            NetworkManager.Singleton.StartHost();
            Destroy(this);
        }
        if (GUI.Button(new Rect(0, 50, 100, 50), "Start client"))
        {
            NetworkManager.Singleton.StartClient();
            Destroy(this);
        }
    }
#endif
}

