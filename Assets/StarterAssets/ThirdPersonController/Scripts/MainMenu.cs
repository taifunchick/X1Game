using System.IO;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenu : MonoBehaviour
{
    [Header("Настройки сцен")]
    [Tooltip("Точное название сцены футбола в Build Settings")]
    [SerializeField] private string footballSceneName = "FootballScene";

    [Tooltip("Точное название сцены лазертага в Build Settings")]
    [SerializeField] private string lasertagSceneName = "LasertagScene";

    [Header("Настройки сети")]
    [Tooltip("IP адрес сервера (оставь localhost для тестов)")]
    [SerializeField] private string serverAddress = "localhost";

    [Tooltip("Главный редактор запускает хост, ParrelSync-клоны и WebGL автоматически подключаются клиентами.")]
    [SerializeField] private bool startAsHost = true;

    private NetworkManager networkManager;
    private bool connectionPending;

    private void Start()
    {
        networkManager = NetworkManager.singleton;
        if (networkManager == null)
        {
            Debug.LogWarning("NetworkManager не найден на сцене! Убедись, что он существует.");
            return;
        }

        networkManager.networkAddress = serverAddress;
    }

    public void OnFootballButtonClicked()
    {
        LoadSceneAndConnect(footballSceneName);
    }

    public void OnLasertagButtonClicked()
    {
        LoadSceneAndConnect(lasertagSceneName);
    }

    private void LoadSceneAndConnect(string sceneName)
    {
        if (connectionPending || NetworkClient.active || NetworkServer.active)
            return;

        connectionPending = true;
        SceneManager.sceneLoaded += OnGameSceneLoaded;
        SceneManager.LoadScene(sceneName);
    }

    private void OnGameSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        SceneManager.sceneLoaded -= OnGameSceneLoaded;
        connectionPending = false;

        // The manager is DontDestroyOnLoad, but reacquiring it also supports custom managers.
        networkManager = NetworkManager.singleton;
        if (networkManager == null || NetworkClient.active || NetworkServer.active)
            return;

        networkManager.networkAddress = serverAddress;
        if (ShouldStartHost())
        {
            Debug.Log($"Сцена {scene.name} загружена. Создаем хост...");
            networkManager.StartHost();
        }
        else
        {
            Debug.Log($"Сцена {scene.name} загружена. Подключаемся к {networkManager.networkAddress}...");
            networkManager.StartClient();
        }
    }

    private bool ShouldStartHost()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        return false;
#elif UNITY_EDITOR
        // ParrelSync marks clones with this file. Avoid an Editor assembly dependency in runtime code.
        bool isParrelSyncClone = File.Exists(Path.Combine(Directory.GetParent(Application.dataPath).FullName, ".clone"));
        return startAsHost && !isParrelSyncClone;
#else
        return startAsHost;
#endif
    }

    public void DisconnectAndReturnToMenu()
    {
        networkManager = NetworkManager.singleton;
        if (networkManager != null)
        {
            if (NetworkServer.active && NetworkClient.active)
                networkManager.StopHost();
            else if (NetworkClient.active)
                networkManager.StopClient();
            else if (NetworkServer.active)
                networkManager.StopServer();
        }

        SceneManager.LoadScene("MainMenu");
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnGameSceneLoaded;
    }
}
