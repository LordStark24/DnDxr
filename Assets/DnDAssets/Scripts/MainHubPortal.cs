using UnityEngine;
using UnityEngine.SceneManagement;

public class MainHubPortal : MonoBehaviour
{
    [SerializeField] private string loadingSceneName = "LoadingScene";
    private bool isLoading;

    private void OnTriggerEnter(Collider other)
    {
        Debug.Log("Portal triggered by: " + other.name);

        if (isLoading)
            return;

        isLoading = true;

        SceneManager.LoadScene(loadingSceneName, LoadSceneMode.Single);
    }
}