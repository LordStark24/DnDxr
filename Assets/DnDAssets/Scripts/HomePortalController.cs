using UnityEngine;
using XRMultiplayer;

public class HomePortalController : MonoBehaviour
{
    [SerializeField] private GameObject portalToMainPublicArea;

    private void Awake()
    {
        if (portalToMainPublicArea != null)
            portalToMainPublicArea.SetActive(false);
    }

    private void OnEnable()
    {
        XRINetworkGameManager.Connected.Subscribe(OnConnectedChanged);
    }

    private void OnDisable()
    {
        XRINetworkGameManager.Connected.Unsubscribe(OnConnectedChanged);
    }

    private void OnConnectedChanged(bool connected)
    {
        if (portalToMainPublicArea != null)
            portalToMainPublicArea.SetActive(connected);
    }
}