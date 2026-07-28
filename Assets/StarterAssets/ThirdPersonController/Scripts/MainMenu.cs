using UnityEngine;
using UnityEngine.SceneManagement;
using Mirror;

public class MenuController : MonoBehaviour
{
    [Header("Настройки сцен")]
    [Tooltip("Точное название сцены футбола в Build Settings")]
    [SerializeField] private string footballSceneName = "FootballScene";
    
    [Tooltip("Точное название сцены лазертага в Build Settings")]
    [SerializeField] private string lasertagSceneName = "LasertagScene";

    [Header("Настройки сети")]
    [Tooltip("IP адрес сервера (оставь localhost для тестов)")]
    [SerializeField] private string serverAddress = "localhost";

    [Tooltip("ВКЛ для главного редактора (Хост). ВЫКЛ для ParrelSync клона и WebGL билда (Клиент).")]
    [SerializeField] private bool startAsHost = true;

    private NetworkManager _networkManager;

    private void Start()
    {
        _networkManager = NetworkManager.singleton;
        
        if (_networkManager != null)
        {
            _networkManager.networkAddress = serverAddress;
        }
        else
        {
            Debug.LogWarning("NetworkManager не найден на сцене! Убедись, что он существует.");
        }
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
        SceneManager.sceneLoaded += OnSceneLoadedStartClient;
        SceneManager.LoadScene(sceneName);
    }

    private void OnSceneLoadedStartClient(Scene scene, LoadSceneMode mode)
    {
        SceneManager.sceneLoaded -= OnSceneLoadedStartClient;

        if (_networkManager != null && !NetworkClient.active)
        {
            // Проверяем нашу галочку из инспектора
            if (startAsHost)
            {
                Debug.Log($"Сцена {scene.name} загружена. Создаем сервер (Хост)...");
                _networkManager.StartHost();
            }
            else
            {
                Debug.Log($"Сцена {scene.name} загружена. Подключаемся как клиент к {_networkManager.networkAddress}...");
                _networkManager.StartClient();
            }
        }
    }

    public void DisconnectAndReturnToMenu()
    {
        // Правильное отключение в зависимости от того, кем мы были
        if (NetworkServer.active && NetworkClient.isConnected)
        {
            _networkManager.StopHost();
        }
        else if (NetworkClient.isConnected)
        {
            _networkManager.StopClient();
        }
        
        SceneManager.LoadScene("MainMenu");
    }
}