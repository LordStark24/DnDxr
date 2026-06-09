using UnityEngine;
using XRMultiplayer;

public class PublicHubAutoConnect : MonoBehaviour
{
    private void Start()
    {
        XRINetworkGameManager.Instance.CreateNewLobby(
            "MainPublicHub",
            false,
            XRINetworkGameManager.maxPlayers
        );
    }
}