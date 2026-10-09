using System.IO;
using Mirror;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Главное меню: выбор команды лазертага (Meteor/Red и Vympel/Blue).
///
/// Футбола в меню больше нет: сцена Football осталась в проекте и в Build Settings,
/// но перейти на неё из меню нельзя. Обе кнопки ведут в сцену Lasertag,
/// запоминая выбранную команду (см. TeamSelection).
///
/// Сцену НЕ грузим сами через SceneManager.LoadScene — это делает Mirror через onlineScene
/// (см. X1NetworkManager.StartGame).
/// </summary>
public class MainMenu : MonoBehaviour
{
    [Header("Настройки сцен")]
    [Tooltip("Имя сцены лазертага в Build Settings")]
    [SerializeField] private string lasertagSceneName = "Lasertag";

    [Header("Настройки сети")]
    [Tooltip("Адрес сервера для клиентов (localhost для локальных тестов, sv.x1team.ru для прода). Пусто — берётся Network Address из NetworkManager.")]
    [SerializeField] private string serverAddress = "localhost";

    [Tooltip("Порт сервера лазертага. 0 — порт транспорта по умолчанию.")]
    [SerializeField] private ushort lasertagPort = 0;

    [Tooltip("Главный редактор запускает хост, ParrelSync-клоны и WebGL автоматически подключаются клиентами. " +
             "Для standalone-сборок можно переопределить аргументами -host / -client.")]
    [SerializeField] private bool startAsHost = true;

    [Header("UI (необязательно)")]
    [Tooltip("Текст для статуса подключения. Можно не назначать.")]
    [SerializeField] private TMP_Text statusText;

    private void Start()
    {
        if (NetworkManager.singleton == null)
            Debug.LogWarning("MainMenu: NetworkManager не найден на сцене! Убедись, что он существует.");
    }

    /// <summary>Кнопка красной команды (Meteor): запоминаем команду и идём в Lasertag.</summary>
    public void OnRedTeamButtonClicked()
    {
        TeamSelection.Select(TeamSelection.Red);
        StartGame(lasertagSceneName, lasertagPort);
    }

    /// <summary>Кнопка синей команды (Vympel): запоминаем команду и идём в Lasertag.</summary>
    public void OnBlueTeamButtonClicked()
    {
        TeamSelection.Select(TeamSelection.Blue);
        StartGame(lasertagSceneName, lasertagPort);
    }

    /// <summary>
    /// Старого футбола больше нет: метод оставлен, чтобы не ронять сцену,
    /// если где-то осталась ссылка на OnFootballButtonClicked. Ничего не запускает.
    /// </summary>
    public void OnFootballButtonClicked()
    {
        Debug.LogWarning("MainMenu: режим футбола убран из меню.");
    }

    public void OnLasertagButtonClicked()
    {
        StartGame(lasertagSceneName, lasertagPort);
    }

    private void StartGame(string sceneName, ushort port)
    {
        NetworkManager manager = NetworkManager.singleton;
        if (manager == null)
        {
            SetStatus("NetworkManager не найден на сцене!", true);
            return;
        }

        if (NetworkServer.active || NetworkClient.active)
        {
            // После обрыва клиент мог остаться в состоянии «подключается» — тогда новый
            // старт блокировался бы навсегда. Сбрасываем состояние перед проверкой.
            if (manager is X1NetworkManager X1Manager)
                X1Manager.PrepareForManualConnect();
        }

        if (NetworkServer.active || NetworkClient.active)
        {
            SetStatus("Подключение уже выполняется...");
            return;
        }

        string scenePath = X1NetworkManager.ResolveScenePath(sceneName);
        if (scenePath == null)
        {
            SetStatus($"Сцена '{sceneName}' не добавлена в Build Settings!", true);
            return;
        }

        bool host = ShouldStartHost();
        string address = string.IsNullOrWhiteSpace(serverAddress) ? manager.networkAddress : serverAddress;

        SetStatus(host ? $"Создаём хост: {sceneName}..." : $"Подключаемся к {address}...");

        if (manager is X1NetworkManager x1Manager)
        {
            x1Manager.StartGame(scenePath, host, address, port);
            return;
        }

        // Запасной путь для обычного NetworkManager: тот же принцип — сцену грузит Mirror через onlineScene.
        Debug.LogWarning("MainMenu: на объекте NetworkManager стоит обычный NetworkManager, а не X1NetworkManager. " +
                         "Замени компонент, чтобы выделенный сервер сам грузил игровую сцену.");
        manager.onlineScene = scenePath;
        manager.networkAddress = address;
        if (port != 0 && Transport.active is PortTransport portTransport)
            portTransport.Port = port;

        if (host)
            manager.StartHost();
        else
            manager.StartClient();
    }

    private bool ShouldStartHost()
    {
        // Явные аргументы командной строки важнее настроек (для standalone-сборок).
        if (X1NetworkManager.HasCommandLineFlag("-client"))
            return false;
        if (X1NetworkManager.HasCommandLineFlag("-host"))
            return true;

#if UNITY_WEBGL && !UNITY_EDITOR
        return false; // браузер не может принимать подключения
#elif UNITY_EDITOR
        // ParrelSync помечает клоны этим файлом. Не тянем Editor-сборку в рантайм-код.
        bool isParrelSyncClone = File.Exists(Path.Combine(Directory.GetParent(Application.dataPath).FullName, ".clone"));
        return startAsHost && !isParrelSyncClone;
#else
        return startAsHost;
#endif
    }

    public void DisconnectAndReturnToMenu()
    {
        NetworkManager manager = NetworkManager.singleton;

        // Выход по кнопке — это осознанное решение игрока, автопереподключение тут не нужно.
        // Также сбрасываем сохранённый выбор команды, чтобы следующий сеанс начинался чисто.
        TeamSelection.Clear();

        if (manager is X1NetworkManager x1Manager)
            x1Manager.DisableAutoReconnect();

        if (manager != null && (NetworkServer.active || NetworkClient.active))
        {
            if (NetworkServer.active && NetworkClient.active)
                manager.StopHost();
            else if (NetworkClient.active)
                manager.StopClient();
            else
                manager.StopServer();

            // Если offlineScene задана, Mirror сам вернёт нас в меню.
            if (!string.IsNullOrWhiteSpace(manager.offlineScene))
                return;
        }

        SceneManager.LoadScene("MainMenu");
    }

    private void SetStatus(string message, bool isError = false)
    {
        if (statusText != null)
            statusText.text = message;

        if (isError)
            Debug.LogError("MainMenu: " + message);
        else
            Debug.Log("MainMenu: " + message);
    }
}
