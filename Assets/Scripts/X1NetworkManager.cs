using System;
using System.Collections;
using System.IO;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// NetworkManager проекта X1.
///
/// Главная задача — чтобы сервер и все клиенты всегда находились в одной игровой сцене
/// и игрок спавнился только после того, как сцена реально загружена:
///  - хост (редактор / standalone) грузит выбранную сцену через onlineScene и только потом создаёт игрока;
///  - клиент подключается, получает от сервера SceneMessage, грузит сцену и только потом делает Ready + AddPlayer;
///  - выделенный (headless) сервер сам грузит игровую сцену при старте:
///      ./Football_sv.x86_64 -batchmode -nographics -scene Football [-port 27777]
///    если аргумента нет — берётся defaultServerScene.
///
/// Раньше сцена грузилась вручную через SceneManager.LoadScene, а сервер оставался в сцене меню.
/// Из-за этого сетевые объекты сцены (BallSpawn, ScoreManager) никогда не спавнились,
/// а колбэк подключения терялся вместе с уничтоженным объектом меню — игрок "заходил", но префаб не появлялся.
/// </summary>
[AddComponentMenu("Network/X1 Network Manager")]
public class X1NetworkManager : NetworkManager
{
    [Header("X1")]
    [Tooltip("Сцена, которую грузит выделенный сервер, если не передан аргумент командной строки -scene <имя>")]
    public string defaultServerScene = "Lasertag";

    [Tooltip("Сколько секунд клиент ждёт сцену от сервера после подключения. Если сервер так и не прислал сцену — отключаемся с понятной ошибкой вместо вечного ожидания.")]
    public float sceneMessageTimeout = 15f;

    [Tooltip("Сколько раз автоматически переподключаться после неожиданного обрыва. " +
             "В WebGL браузер рвёт WebSocket, когда вкладка уходит в фоновый режим (Back-Forward Cache), " +
             "и без автопереподключения игра просто «зависает». 0 — не переподключаться.")]
    public int maxReconnectAttempts = 5;

    [Tooltip("Пауза перед очередной попыткой переподключения, секунды. Растёт с каждой попыткой.")]
    public float reconnectBaseDelay = 2f;

    private Coroutine _sceneWatchdog;
    private Coroutine _reconnectRoutine;

    // Какое подключение мы хотим держать — чтобы переподключиться после обрыва,
    // не заставляя игрока снова нажимать кнопку в меню.
    private string _desiredScene;
    private bool _desiredAsHost;
    private string _desiredAddress;
    private ushort _desiredPort;
    private int _reconnectAttempts;

    // Ставится в true при успешном StartGame и сбрасывается, когда игрок сам
    // нажал «выйти» — тогда автоматически подключаться обратно не нужно.
    private bool _autoReconnectEnabled;

    // Была ли вкладка не в фокусе — чтобы переподключение срабатывало только после
    // реального ухода в фон, а не на каждый тап по экрану.
    private bool _wasUnfocused;

    #region Запуск игры

    /// <summary>
    /// Запускает игру в указанной сцене: как хост (сервер + клиент) или как клиент.
    /// Сцена задаётся именем ("Football") или полным путём и должна быть в Build Settings.
    /// </summary>
    public bool StartGame(string sceneNameOrPath, bool asHost, string address = null, ushort port = 0)
    {
        if (NetworkServer.active || NetworkClient.active)
        {
            Debug.LogWarning("[X1NetworkManager] Сервер или клиент уже запущен — повторный запуск проигнорирован.");
            return false;
        }

        string scenePath = ResolveScenePath(sceneNameOrPath);
        if (scenePath == null)
        {
            Debug.LogError($"[X1NetworkManager] Сцена '{sceneNameOrPath}' не найдена в Build Settings. Добавь её в File → Build Settings.");
            return false;
        }

        // Mirror сам сделает всё остальное:
        //  хост   → ServerChangeScene(onlineScene) → SpawnObjects → локальный клиент → Ready/AddPlayer
        //  клиент → connect → SceneMessage от сервера → загрузка сцены → Ready/AddPlayer
        onlineScene = scenePath;

        // Если мы уже стоим в нужной сцене, Mirror сцену менять не будет и networkSceneName останется
        // равным offlineScene — тогда клиентам не отправится SceneMessage. Выставляем вручную.
        if (asHost && Mirror.Utils.IsSceneActive(scenePath))
            networkSceneName = scenePath;

        if (!string.IsNullOrWhiteSpace(address))
            networkAddress = address;

        ApplyPort(port);

        CancelReconnect();
        RememberDesiredConnection(scenePath, asHost, address, port);

        if (asHost)
        {
            Debug.Log($"[X1NetworkManager] Создаём хост, сцена '{Path.GetFileNameWithoutExtension(scenePath)}'...");
            StartHost();
        }
        else
        {
            Debug.Log($"[X1NetworkManager] Подключаемся к {networkAddress}:{CurrentPort()} (сцена '{Path.GetFileNameWithoutExtension(scenePath)}')...");
            StartClient();
        }

        return true;
    }

    #endregion

    #region Выделенный сервер

    public override void Start()
    {
        if (ShouldAutoStartDedicatedServer())
            ConfigureDedicatedServer();

        // base.Start() вызовет StartServer() для headless-сборки,
        // а StartServer() сам загрузит onlineScene через ServerChangeScene.
        base.Start();
    }

    private bool ShouldAutoStartDedicatedServer()
    {
        if (headlessStartMode != HeadlessStartOptions.AutoStartServer)
            return false;
        if (Application.isEditor && !editorAutoStart)
            return false;
        return Mirror.Utils.IsHeadless();
    }

    private void ConfigureDedicatedServer()
    {
        string scenePath = null;

        // Если передан явный аргумент командной строки -scene <имя>, используем его
        string requestedScene = CommandLineArg("-scene");
        if (!string.IsNullOrWhiteSpace(requestedScene))
        {
            scenePath = ResolveScenePath(requestedScene);
            if (scenePath == null)
                Debug.LogWarning($"[X1NetworkManager] Сцена '{requestedScene}' из аргумента -scene не найдена в Build Settings. Проверяем onlineScene / defaultServerScene.");
        }

        if (scenePath == null && !string.IsNullOrWhiteSpace(onlineScene))
            scenePath = ResolveScenePath(onlineScene);

        if (scenePath == null)
            scenePath = ResolveScenePath(defaultServerScene);

        if (scenePath == null)
        {
            Debug.LogError($"[X1NetworkManager] Выделенный сервер: ни одна игровая сцена ('{onlineScene}', '{defaultServerScene}', '{requestedScene}') не найдена в Build Settings. " +
                           "Сервер останется в сцене меню, и игроки НЕ смогут заспавниться. Проверь Build Settings и поле onlineScene в X1NetworkManager.");
        }
        else
        {
            onlineScene = scenePath;

            // Сборка сервера может начинаться сразу с игровой сцены (без меню). Тогда смены сцены не будет,
            // и нужно вручную сообщить Mirror, какую сцену присылать подключающимся клиентам.
            if (Mirror.Utils.IsSceneActive(scenePath))
                networkSceneName = scenePath;
        }

        string portArg = CommandLineArg("-port");
        if (!string.IsNullOrWhiteSpace(portArg))
        {
            if (ushort.TryParse(portArg, out ushort port) && port != 0)
                ApplyPort(port);
            else
                Debug.LogWarning($"[X1NetworkManager] Некорректный порт в аргументе -port: '{portArg}'");
        }

        Debug.Log($"[X1NetworkManager] Выделенный сервер: сцена '{onlineScene}', порт {CurrentPort()}, макс. игроков {maxConnections}");
    }

    #endregion

    #region Колбэки сервера

    public override void OnServerSceneChanged(string sceneName)
    {
        base.OnServerSceneChanged(sceneName);
        Debug.Log($"[X1NetworkManager] Сервер загрузил сцену '{sceneName}'. Ожидаем игроков.");
    }

    public override void OnServerConnect(NetworkConnectionToClient conn)
    {
        base.OnServerConnect(conn);
        Debug.Log($"[X1NetworkManager] Клиент подключился: connId={conn.connectionId}, address={conn.address}");

        // К этому моменту Mirror уже отправил клиенту SceneMessage (если сервер в игровой сцене).
        // Если сервер почему-то остался в сцене меню — клиент никогда не станет ready. Предупредим об этом.
        if (string.IsNullOrEmpty(networkSceneName) || networkSceneName == offlineScene)
        {
            Debug.LogWarning("[X1NetworkManager] Сервер находится в сцене меню (onlineScene не задана). " +
                             "Клиенты не получат игровую сцену и игроки не заспавнятся. Запусти игру через StartGame() или задай -scene для выделенного сервера.");
        }
    }

    public override void OnServerAddPlayer(NetworkConnectionToClient conn)
    {
        // Сначала штатные NetworkStartPosition, потом точки с тегом "Respawn" (их используют сцены проекта).
        Transform startPos = GetStartPosition();
        if (startPos == null)
            startPos = GetRandomRespawnPoint();

        GameObject player = startPos != null
            ? Instantiate(playerPrefab, startPos.position, startPos.rotation)
            : Instantiate(playerPrefab);

        player.name = $"{playerPrefab.name} [connId={conn.connectionId}]";
        NetworkServer.AddPlayerForConnection(conn, player);

        Debug.Log($"[X1NetworkManager] Игрок для connId={conn.connectionId} создан в сцене '{SceneManager.GetActiveScene().name}' " +
                  $"в точке {player.transform.position}. Всего игроков: {NetworkServer.connections.Count}");
    }

    public override void OnServerDisconnect(NetworkConnectionToClient conn)
    {
        Debug.Log($"[X1NetworkManager] Клиент отключился: connId={conn.connectionId}");
        base.OnServerDisconnect(conn);
    }

    private static Transform GetRandomRespawnPoint()
    {
        GameObject[] points = GameObject.FindGameObjectsWithTag("Respawn");
        if (points == null || points.Length == 0)
            return null;
        return points[UnityEngine.Random.Range(0, points.Length)].transform;
    }

    #endregion

    #region Колбэки клиента

    public override void OnClientConnect()
    {
        // База сама решает: если onlineScene не задана или уже активна — сразу Ready + AddPlayer,
        // иначе ждёт SceneMessage от сервера и делает Ready + AddPlayer в OnClientSceneChanged.
        base.OnClientConnect();

        if (clientLoadedScene)
        {
            Debug.Log("[X1NetworkManager] Подключились к серверу, ждём от него игровую сцену...");
            StopSceneWatchdog();
            _sceneWatchdog = StartCoroutine(WaitForServerScene());
        }
    }

    public override void OnClientChangeScene(string newSceneName, SceneOperation sceneOperation, bool customHandling)
    {
        base.OnClientChangeScene(newSceneName, sceneOperation, customHandling);

        if (NetworkServer.active)
            return; // хост грузит сцену сам

        if (sceneOperation == SceneOperation.Normal && ResolveScenePath(newSceneName) == null)
        {
            // Иначе Mirror зависнет с isLoadingScene = true навсегда.
            Debug.LogError($"[X1NetworkManager] Сервер прислал сцену '{newSceneName}', которой нет в Build Settings клиента. " +
                           "Версии сервера и клиента не совпадают. Отключаемся.");
            // Отключаемся на следующем кадре: NetworkClient.Shutdown() сбросит isLoadingScene уже после того,
            // как Mirror выставит его в ClientChangeScene.
            StartCoroutine(StopClientNextFrame());
            return;
        }

        Debug.Log($"[X1NetworkManager] Сервер прислал сцену '{newSceneName}', загружаем...");
    }

    private IEnumerator StopClientNextFrame()
    {
        yield return null;
        StopClient();
    }

    public override void OnClientSceneChanged()
    {
        // База: Ready + AddPlayer (если игрока ещё нет). Именно здесь клиент запрашивает свой префаб.
        base.OnClientSceneChanged();
        Debug.Log($"[X1NetworkManager] Сцена '{SceneManager.GetActiveScene().name}' загружена на клиенте, запросили игрока у сервера.");
    }

    public override void OnClientDisconnect()
    {
        StopSceneWatchdog();
        Debug.Log("[X1NetworkManager] Отключились от сервера.");
        base.OnClientDisconnect();

        ScheduleReconnect("Соединение потеряно: вкладка браузера ушла в фоновый режим или сервер закрыл соединение.");
    }

    public override void OnClientError(TransportError error, string reason)
    {
        base.OnClientError(error, reason);
        Debug.LogWarning($"[X1NetworkManager] Ошибка соединения: {error} — {reason}");
    }

    public override void OnStopClient()
    {
        StopSceneWatchdog();
        CancelReconnect();
        base.OnStopClient();
    }

    private IEnumerator WaitForServerScene()
    {
        float deadline = Time.realtimeSinceStartup + sceneMessageTimeout;
        while (Time.realtimeSinceStartup < deadline)
        {
            // Сцена пришла / грузится / уже ready — сторож больше не нужен.
            if (!NetworkClient.isConnected || NetworkClient.ready || NetworkClient.isLoadingScene || loadingSceneAsync != null)
                yield break;
            yield return null;
        }

        if (NetworkClient.isConnected && !NetworkClient.ready)
        {
            Debug.LogError($"[X1NetworkManager] Сервер за {sceneMessageTimeout:0} сек. не прислал игровую сцену. " +
                           "Скорее всего сервер запущен без '-scene Lasertag' или собран из старой версии проекта, " +
                           "поэтому он остался в сцене меню и не знает, какую сцену прислать клиенту.");

            StopClient();

            // Сервер мог просто перезапускаться — клиент переподключится сам,
            // как только тот снова поднимется.
            ScheduleReconnect("Сервер не прислал игровую сцену.");
        }
    }

    private void StopSceneWatchdog()
    {
        if (_sceneWatchdog != null)
        {
            StopCoroutine(_sceneWatchdog);
            _sceneWatchdog = null;
        }
    }

    #endregion

    #region Автопереподключение

    private void RememberDesiredConnection(string scenePath, bool asHost, string address, ushort port)
    {
        _desiredScene = scenePath;
        _desiredAsHost = asHost;
        _desiredAddress = string.IsNullOrWhiteSpace(address) ? networkAddress : address;
        _desiredPort = port;
        _reconnectAttempts = 0;
        _autoReconnectEnabled = true;
    }

    /// <summary>Игрок сам вышел из игры — больше не подключаемся автоматически.</summary>
    public void DisableAutoReconnect()
    {
        _autoReconnectEnabled = false;
        _desiredScene = null;
        TeamSelection.Clear();
        CancelReconnect();
    }

    public void CancelReconnect()
    {
        if (_reconnectRoutine == null)
            return;

        StopCoroutine(_reconnectRoutine);
        _reconnectRoutine = null;
    }

    private void ScheduleReconnect(string reason)
    {
        // Хост и выделенный сервер не переподключаем: там обрыв означает падение.
        if (!_autoReconnectEnabled || _desiredScene == null || _desiredAsHost)
            return;

        if (_reconnectAttempts >= maxReconnectAttempts)
        {
            Debug.LogWarning($"[X1NetworkManager] {reason} Больше не пытаемся переподключиться " +
                             $"({maxReconnectAttempts} попыток). Вернитесь в меню и нажмите подключение ещё раз.");
            _autoReconnectEnabled = false;
            return;
        }

        CancelReconnect();
        _reconnectRoutine = StartCoroutine(ReconnectRoutine(reason));
    }

    private IEnumerator ReconnectRoutine(string reason)
    {
        _reconnectAttempts++;
        float delay = reconnectBaseDelay * _reconnectAttempts;

        Debug.Log($"[X1NetworkManager] {reason} Повторное подключение через {delay:0.#} сек. " +
                  $"(попытка {_reconnectAttempts}/{maxReconnectAttempts}).");

        // Сначала даём Mirror полностью остановиться, иначе StartGame вернёт false.
        float waitDeadline = Time.realtimeSinceStartup + 10f;
        while ((NetworkServer.active || NetworkClient.active) && Time.realtimeSinceStartup < waitDeadline)
            yield return null;

        float deadline = Time.realtimeSinceStartup + delay;
        while (Time.realtimeSinceStartup < deadline)
            yield return null;

        if (Application.internetReachability == NetworkReachability.NotReachable)
        {
            Debug.LogWarning("[X1NetworkManager] Сети нет — переподключение отменено.");
            _reconnectRoutine = null;
            yield break;
        }

        // После обрыва Mirror мог оставить нас в игровой сцене — вернёмся в меню,
        // иначе клиент подключится поверх старой сцены.
        yield return ReturnToOfflineScene();

        string scene = _desiredScene;
        bool host = _desiredAsHost;
        string address = _desiredAddress;
        ushort port = _desiredPort;

        // Обнуляем заранее: StartGame() сам вызывает CancelReconnect().
        _reconnectRoutine = null;

        StartGame(scene, host, address, port);
    }

    /// <summary>Возвращает клиента в offlineScene, если Mirror её настроил.</summary>
    private IEnumerator ReturnToOfflineScene()
    {
        if (string.IsNullOrWhiteSpace(offlineScene))
            yield break;

        string path = ResolveScenePath(offlineScene);
        if (path == null || path == SceneManager.GetActiveScene().path)
            yield break;

        Debug.Log($"[X1NetworkManager] Возвращаемся в '{offlineScene}' перед переподключением.");

        AsyncOperation load = SceneManager.LoadSceneAsync(path);
        while (load != null && !load.isDone)
            yield return null;
    }

    private void OnApplicationPause(bool paused) => TrackVisibility(!paused);

    private void OnApplicationFocus(bool hasFocus) => TrackVisibility(hasFocus);

    /// <summary>
    /// Помнит, что вкладка/окно действительно уходили не в фокус. Это отличает
    /// «вернулись из фона» от обычного клика внутри игры — иначе переподключение
    /// срабатывало бы на каждом тапе по экрану и выбрасывало бы из матча.
    /// </summary>
    private void TrackVisibility(bool focused)
    {
        if (!focused)
        {
            _wasUnfocused = true;
            return;
        }

        if (_wasUnfocused)
        {
            _wasUnfocused = false;
            HandleBrowserResume();
        }
    }

    /// <summary>
    /// В WebGL при возврате на вкладку браузер отдаёт уже мёртвый WebSocket
    /// («Page entered Back-Forward Cache»). Mirror узнаёт об этом не сразу, поэтому
    /// проверяем соединение сами и переподключаемся.
    /// </summary>
    private void HandleBrowserResume()
    {
        if (!_autoReconnectEnabled || _desiredScene == null || _desiredAsHost)
            return;

        // Переподключение уже идёт — не плодим новые попытки.
        if (_reconnectRoutine != null)
            return;

        if (NetworkClient.isConnected)
            return;

        // Клиент ещё не успел стартовать (например, пришли сюда сразу после обрыва
        // до первого OnClientDisconnect) — тогда переподключением займётся сам обрыв.
        if (!NetworkClient.active && !NetworkServer.active)
            return;

        Debug.LogWarning("[X1NetworkManager] Вернулись на вкладку, а соединения нет — переподключаемся.");

        if (NetworkClient.active || NetworkServer.active)
            StopClient();

        ScheduleReconnect("Соединение не пережило уход вкладки в фоновый режим.");
    }

    /// <summary>
    /// Подготовка к подключению по кнопке в меню.
    ///
    /// После обрыва клиент может остаться в состоянии «подключается» (или висеть
    /// автопереподключение) — тогда обычная проверка в MainMenu блокирует новый старт
    /// и игрок не может зайти снова. Здесь всё это гасим, чтобы кнопка работала.
    /// </summary>
    public void PrepareForManualConnect()
    {
        CancelReconnect();
        _autoReconnectEnabled = false;
        _desiredScene = null;

        if (NetworkServer.active || NetworkClient.active)
        {
            Debug.Log("[X1NetworkManager] Сбрасываем предыдущее подключение перед новым стартом.");
            StopHost();

            if (NetworkServer.active || NetworkClient.active)
                StopClient();
        }
    }

    #endregion

    #region Утилиты

    /// <summary>
    /// Возвращает путь сцены из Build Settings по имени ("Football") или пути. null — если сцены нет в сборке.
    /// Работаем с путями, потому что Mirror сравнивает onlineScene/offlineScene как строки.
    /// </summary>
    public static string ResolveScenePath(string sceneNameOrPath)
    {
        if (string.IsNullOrWhiteSpace(sceneNameOrPath))
            return null;

        int count = SceneManager.sceneCountInBuildSettings;
        for (int i = 0; i < count; i++)
        {
            string path = SceneUtility.GetScenePathByBuildIndex(i);
            if (string.IsNullOrEmpty(path))
                continue;

            if (path == sceneNameOrPath ||
                string.Equals(Path.GetFileNameWithoutExtension(path), sceneNameOrPath, StringComparison.OrdinalIgnoreCase))
                return path;
        }

        return null;
    }

    /// <summary>Значение аргумента командной строки: "-scene Football" или "-scene=Football". null — если аргумента нет.</summary>
    public static string CommandLineArg(string name)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        return null; // в браузере командной строки нет
#else
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return i + 1 < args.Length ? args[i + 1] : string.Empty;

            if (args[i].StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
                return args[i].Substring(name.Length + 1);
        }
        return null;
#endif
    }

    public static bool HasCommandLineFlag(string name) => CommandLineArg(name) != null;

    private void ApplyPort(ushort port)
    {
        if (port == 0)
            return;

        Transport t = Transport.active != null ? Transport.active : transport;
        if (t is PortTransport portTransport)
            portTransport.Port = port;
        else
            Debug.LogWarning($"[X1NetworkManager] Транспорт {t} не поддерживает смену порта.");
    }

    private string CurrentPort()
    {
        Transport t = Transport.active != null ? Transport.active : transport;
        return t is PortTransport portTransport ? portTransport.Port.ToString() : "?";
    }

    #endregion
}
