using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using XRMultiplayer;

public class PublicHubLoadingManager : MonoBehaviour
{
    [SerializeField] private string destinationScene = "MainPublicScene";
    [SerializeField] private float maxWaitTime = 20f;

    private IEnumerator Start()
    {
        Debug.Log("LOADING: Started");

        yield return StartCoroutine(ConnectAndLoad());
    }

    private IEnumerator ConnectAndLoad()
    {
        var authManager = FindAnyObjectByType<AuthenticationManager>();

        if (authManager == null)
        {
            Debug.LogError("LOADING: No AuthenticationManager found in LoadingScene.");
            yield break;
        }

        Debug.Log("LOADING: Authenticating...");

        var authTask = authManager.Authenticate();

        while (!authTask.IsCompleted)
            yield return null;

        if (!authTask.Result)
        {
            Debug.LogError("LOADING: Authentication failed.");
            yield break;
        }

        Debug.Log("LOADING: Authentication successful.");
        Debug.Log("LOADING: Calling QuickJoinLobby.");

        XRINetworkGameManager.Instance.QuickJoinLobby();

        float timer = 0f;

        while (!XRINetworkGameManager.Connected.Value && timer < maxWaitTime)
        {
            timer += Time.deltaTime;
            yield return null;
        }

        Debug.Log("LOADING: Connected = " + XRINetworkGameManager.Connected.Value);

        if (!XRINetworkGameManager.Connected.Value)
        {
            Debug.LogError("LOADING: Failed to connect before timeout.");
            yield break;
        }

        Debug.Log("LOADING: Loading " + destinationScene);

        SceneManager.LoadScene(destinationScene, LoadSceneMode.Single);
    }
}