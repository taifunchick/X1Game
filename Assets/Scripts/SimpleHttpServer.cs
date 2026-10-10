using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using Mirror;
using UnityEngine;

/// <summary>
/// Локальный HTTP-эндпоинт сервера.
///
/// Две задачи:
///  1. <c>GET /health</c> — проверка живости выделенного сервера. Отвечает ОТДЕЛЬНЫЙ поток,
///     поэтому эндпоинт работает даже тогда, когда главный поток Unity завис. Именно по полю
///     <c>stale</c> внешний мониторинг (Deploy/scripts/x1-unity-watchdog.sh) понимает, что
///     процесс «живой труп», и перезапускает сервис.
///  2. <c>POST /api/football</c> — старая интеграция с роботом (команды движения по нику).
///
/// Почему этот файл переписан. Прежняя версия была опасна для выделенного сервера:
///  - цикл приёма был <c>async void … while (true)</c> с пустым catch: если HttpListener
///    начинал отдавать ошибку сразу (порт занят, слушатель остановлен), цикл превращался
///    в бесконечную «вертушку» НА ГЛАВНОМ ПОТОКЕ без единого await. Сервер зависал,
///    оставаясь живым, и systemd его не перезапускал — ровно тот симптом, из-за которого
///    игроки переставали попадать в сцену;
///  - слушатель вешался на <c>http://+:8084/</c> (все интерфейсы, наружу);
///  - обработчик вызывал Unity API (<c>FindObjectsOfType</c>, <c>GetComponent</c>,
///    <c>StartCoroutine</c>) из продолжения на чужом потоке — в Unity это аварийное завершение;
///  - слушатель не закрывался при выгрузке сцены, поэтому после смены сцены порт оставался
///    занятым и новый экземпляр падал с исключением.
///
/// Теперь: приём на фоновом потоке, счётчик подряд идущих ошибок с остановкой цикла,
/// весь Unity-код — только в <see cref="Update"/> на главном потоке, слушатель закрывается
/// в <see cref="OnDestroy"/>.
/// </summary>
public class SimpleHttpServer : NetworkBehaviour
{
    [Header("Запуск")]
    [Tooltip("Включать HTTP-эндпоинт на выделенном сервере. Нужен для /health и для интеграции с роботом.")]
    [SerializeField] private bool enableOnServer = true;

    [Header("Адрес")]
    [Tooltip("Адрес для прослушивания. 127.0.0.1 — только локально (рекомендуется: nginx и мониторинг на той же машине). " +
             "Пусто или '+' — все интерфейсы (эндпоинт станет виден снаружи, потребуется открыть порт в файрволе).")]
    [SerializeField] private string bindAddress = "127.0.0.1";

    [Tooltip("Порт HTTP-эндпоинта. Не должен совпадать с игровым портом (27777).")]
    [SerializeField] private int port = 8084;

    [Tooltip("Сколько подряд идущих ошибок приёма допустимо до остановки цикла. Защита от бесконечной вертушки на главном потоке.")]
    [SerializeField] private int maxConsecutiveErrors = 20;

    [Tooltip("Очередь игровых команд, обрабатываемых на главном потоке. Переполнение = команду отбрасываем.")]
    [SerializeField] private int maxQueuedCommands = 128;

    private HttpListener _listener;
    private Thread _acceptThread;
    private volatile bool _running;
    private int _started;   // Interlocked: слушатель поднимаем максимум один раз

    private readonly ConcurrentQueue<Action> _mainThreadQueue = new ConcurrentQueue<Action>();
    private readonly ConcurrentQueue<MoveData> _robotCommands = new ConcurrentQueue<MoveData>();

    /// <summary>Сколько команд отброшено из-за переполнения очереди (видно в /health).</summary>
    private long _droppedCommands;

    /// <summary>Сколько запросов обработано (видно в /health).</summary>
    private long _requestsHandled;

    // ---------------- кэш состояния для /health ----------------
    //
    // Эти поля СНИМАЕТ Update() на главном потоке, а ЧИТАЕТ поток приёма HTTP.
    // Обращаться к NetworkServer, SceneManager и SystemInfo из чужого потока нельзя:
    // Unity API рассчитан на главный поток, и такой вызов в лучшем случае напишет в лог
    // ошибку, в худшем — аварийно завершит процесс. А /health как раз и должен отвечать,
    // когда главный поток лежит, поэтому ему нужен заранее снятый снимок состояния.
    private static volatile bool _cachedServerActive;
    private static volatile bool _cachedHeadlessGuard;
    private static volatile string _cachedScene = "";
    private static int _cachedConnections;
    private static int _cachedSpawned;

    private void Start()
    {
        if (!enableOnServer)
            return;

        if (!isServer)
            return;

        // Сцена может содержать несколько экземпляров, а Mirror может пересоздать объект при смене сцены.
        if (Interlocked.Exchange(ref _started, 1) != 0)
            return;

        TryStartListener();
    }

    private void TryStartListener()
    {
        string host = string.IsNullOrWhiteSpace(bindAddress) ? "+" : bindAddress.Trim();

        if (port <= 0 || port > 65535)
        {
            Debug.LogError($"[HttpServ] Некорректный порт {port}. HTTP-эндпоинт не запущен.");
            return;
        }

        string prefix = string.Format(CultureInfo.InvariantCulture, "http://{0}:{1}/", host, port);

        try
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add(prefix);
            _listener.Start();
        }
        catch (Exception e)
        {
            // Не роняем сервер: игровой порт важнее, а /health — вспомогательный.
            Debug.LogError($"[HttpServ] Не удалось поднять HTTP на {prefix}: {e.Message}. " +
                           "Проверьте, не занят ли порт предыдущим процессом (ss -ltnp | grep " + port + ").");
            SafeCloseListener();
            _listener = null;
            return;
        }

        _running = true;
        _acceptThread = new Thread(AcceptLoop)
        {
            IsBackground = true,
            Name = "X1HttpAccept",
            // Приоритет ниже игрового: эндпоинт не должен отнимать CPU у сервера.
            Priority = ThreadPriority.BelowNormal
        };
        _acceptThread.Start();

        Debug.Log($"[HttpServ] Слушаю {prefix} (health: {prefix}health). Поток приёма отдельный, главный поток не блокируется.");
    }

    /// <summary>
    /// Цикл приёма. Работает на своём потоке.
    /// КЛЮЧЕВОЕ: при повторяющихся ошибках цикл ОСТАНАВЛИВАЕТСЯ, а не крутится вхолостую.
    /// </summary>
    private void AcceptLoop()
    {
        int consecutiveErrors = 0;

        while (_running)
        {
            HttpListenerContext context = null;

            try
            {
                // Синхронный GetContext: он блокирует СВОЙ поток, а не главный.
                // GetContextAsync в прежней версии возвращал управление на главный поток
                // и при ошибке давал бесконечный цикл без ожидания.
                context = _listener.GetContext();
                consecutiveErrors = 0;
            }
            catch (Exception e)
            {
                if (!_running)
                    return; // штатная остановка

                consecutiveErrors++;

                if (consecutiveErrors >= Math.Max(1, maxConsecutiveErrors))
                {
                    Debug.LogError($"[HttpServ] {consecutiveErrors} ошибок приёма подряд, цикл остановлен: {e.Message}. " +
                                   "HTTP-эндпоинт больше не работает, игровой сервер продолжает работать.");
                    return;
                }

                // Пауза обязательна: без неё ошибки шли бы вплотную и грузили CPU.
                try { Thread.Sleep(250); } catch (ThreadInterruptedException) { return; }
                continue;
            }

            if (context == null)
                continue;

            try
            {
                Handle(context);
                Interlocked.Increment(ref _requestsHandled);
            }
            catch (Exception e)
            {
                Debug.LogError("[HttpServ] Ошибка обработки запроса: " + e.Message);
                TryRespond(context, 500, "{\"error\":\"internal\"}");
            }
        }
    }

    /// <summary>Обработка запроса. Unity API здесь вызывать НЕЛЬЗЯ — это не главный поток.</summary>
    private void Handle(HttpListenerContext context)
    {
        string method = context.Request.HttpMethod ?? "GET";
        string path = context.Request.Url?.AbsolutePath ?? "/";

        // CORS — прежняя интеграция звонила сюда из браузера.
        context.Response.Headers["Access-Control-Allow-Origin"] = "*";
        context.Response.Headers["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS";
        context.Response.Headers["Access-Control-Allow-Headers"] = "Content-Type";

        if (string.Equals(method, "OPTIONS", StringComparison.OrdinalIgnoreCase))
        {
            TryRespond(context, 204, "");
            return;
        }

        if (string.Equals(path, "/health", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(path, "/healthz", StringComparison.OrdinalIgnoreCase))
        {
            TryRespond(context, HealthStatusCode(), BuildHealthJson());
            return;
        }

        if (string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(path, "/api/football", StringComparison.OrdinalIgnoreCase))
        {
            HandleFootball(context);
            return;
        }

        TryRespond(context, 404, "{\"error\":\"404 Not Found\"}");
    }

    /// <summary>
    /// 200 — главный поток жив. 503 — главный поток не обновлялся слишком долго:
    /// это и есть «сервис висит», и внешний мониторинг должен его перезапустить.
    /// </summary>
    private static int HealthStatusCode()
    {
        if (!_cachedHeadlessGuard)
            return 200;

        double stale = X1ServerWatchdog.SecondsSinceLastFrame;
        if (stale < 0) return 200;

        return stale > Math.Max(5f, X1ServerWatchdog.StallTimeoutSeconds * 0.5f) ? 503 : 200;
    }

    private static string BuildHealthJson()
    {
        var sb = new StringBuilder(512);
        sb.Append('{');
        AppendField(sb, "ok", HealthStatusCode() == 200);
        sb.Append(',');

        double stale = X1ServerWatchdog.SecondsSinceLastFrame;
        AppendField(sb, "mainThreadStaleSeconds", stale < 0 ? -1d : Math.Round(stale, 3));
        sb.Append(',');

        var watchdog = X1ServerWatchdog.Instance;
        if (watchdog != null)
        {
            AppendField(sb, "uptimeSeconds", Math.Round(watchdog.UptimeSeconds, 1)); sb.Append(',');
            AppendField(sb, "fps", Math.Round(watchdog.AverageFps, 1)); sb.Append(',');
            AppendField(sb, "worstFrameMs", Math.Round(watchdog.WorstFrameMs, 1)); sb.Append(',');
            AppendField(sb, "frames", watchdog.Frames); sb.Append(',');
        }

        int connections = _cachedConnections;
        int spawned = _cachedSpawned;
        string scene = _cachedScene;
        bool serverActive = _cachedServerActive;

        AppendField(sb, "serverActive", serverActive); sb.Append(',');
        AppendField(sb, "connections", connections); sb.Append(',');
        AppendField(sb, "spawnedObjects", spawned); sb.Append(',');
        AppendField(sb, "scene", scene); sb.Append(',');
        AppendField(sb, "managedMemoryMB", Math.Round(GC.GetTotalMemory(false) / 1048576.0, 1));
        sb.Append('}');
        return sb.ToString();
    }

    private static void AppendField(StringBuilder sb, string name, object value)
    {
        sb.Append('"').Append(name).Append("\":");

        switch (value)
        {
            case string s:
                sb.Append('"').Append(EscapeJson(s)).Append('"');
                break;
            case bool b:
                sb.Append(b ? "true" : "false");
                break;
            case IFormattable f:
                sb.Append(f.ToString(null, CultureInfo.InvariantCulture));
                break;
            default:
                sb.Append('"').Append(EscapeJson(Convert.ToString(value, CultureInfo.InvariantCulture))).Append('"');
                break;
        }
    }

    private static string EscapeJson(string s)
    {
        if (string.IsNullOrEmpty(s)) return s ?? "";
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", " ");
    }

    private void HandleFootball(HttpListenerContext context)
    {
        string contentType = context.Request.ContentType ?? "";
        if (contentType.IndexOf("json", StringComparison.OrdinalIgnoreCase) < 0)
        {
            TryRespond(context, 415, "{\"error\":\"Invalid content type\"}");
            return;
        }

        MoveData data;
        try
        {
            using (var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding))
            {
                string json = reader.ReadToEnd();

                // Ограничение размера: иначе один запрос с телом на гигабайт съест память сервера.
                if (json.Length > 64 * 1024)
                {
                    TryRespond(context, 413, "{\"error\":\"Payload too large\"}");
                    return;
                }

                data = Newtonsoft.Json.JsonConvert.DeserializeObject<MoveData>(json);
            }
        }
        catch (Exception e)
        {
            TryRespond(context, 400, "{\"error\":\"Bad JSON\"}");
            Debug.LogWarning("[HttpServ] Не удалось разобрать JSON: " + e.Message);
            return;
        }

        if (data == null || string.IsNullOrWhiteSpace(data.username))
        {
            TryRespond(context, 400, "{\"message\":\"BadNickname\"}");
            return;
        }

        // Команду не выполняем здесь: RoboMove.StartCoroutine и FindObjectsOfType можно
        // вызывать только из главного потока. Ставим в очередь — Update её разберёт.
        if (_robotCommands.Count >= Math.Max(1, maxQueuedCommands))
        {
            Interlocked.Increment(ref _droppedCommands);
            TryRespond(context, 503, "{\"message\":\"ServerBusy\"}");
            return;
        }

        // Команда поставлена в очередь и будет выполнена в Update на главном потоке.
        // Код ответа и тело оставлены прежними (200 + "JSON file received"), чтобы не сломать
        // внешнего вызывающего. Проверка ника теперь тоже асинхронная: если игрок не найдётся,
        // это попадёт в лог сервера, а не в ответ.
        _robotCommands.Enqueue(data);
        TryRespond(context, 200, "{\"message\":\"JSON file received\"}");
    }

    private void Update()
    {
        if (!isServer)
            return;

        RefreshHealthCache();

        // Разбираем игровые команды строго на главном потоке.
        while (_robotCommands.TryDequeue(out MoveData data))
            ApplyRobotCommand(data);

        // Прочие отложенные действия (если появятся).
        int budget = 32;
        while (budget-- > 0 && _mainThreadQueue.TryDequeue(out Action action))
        {
            try { action(); }
            catch (Exception e) { Debug.LogError("[HttpServ] Ошибка отложенной команды: " + e); }
        }
    }

    /// <summary>Снимок состояния для /health. Только главный поток.</summary>
    private void RefreshHealthCache()
    {
        _cachedServerActive = NetworkServer.active;
        _cachedHeadlessGuard = X1ServerWatchdog.ShouldRun;

        if (_cachedServerActive)
        {
            _cachedConnections = NetworkServer.connections.Count;
            _cachedSpawned = NetworkServer.spawned.Count;
        }
        else
        {
            _cachedConnections = 0;
            _cachedSpawned = 0;
        }

        _cachedScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
    }

    /// <summary>Главный поток: найти робота по нику и отправить его двигаться.</summary>
    private void ApplyRobotCommand(MoveData data)
    {
        // Ищем только активные объекты сцены. Метод вызывается из Update, то есть
        // строго на главном потоке — иначе Unity аварийно завершил бы процесс.
        var names = FindObjectsByType<PlayerName>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);

        GameObject robo = null;
        for (int i = 0; i < names.Length; i++)
        {
            PlayerName playerName = names[i];
            if (playerName == null) continue;
            if (playerName.Name == data.username)
            {
                robo = playerName.gameObject;
                break;
            }
        }

        if (robo == null)
        {
            Debug.LogWarning($"[HttpServ] Игрок '{data.username}' не найден — команда движения отброшена.");
            return;
        }

        var roboMove = robo.GetComponent<RoboMove>();
        if (roboMove == null)
        {
            Debug.LogWarning($"[HttpServ] У '{data.username}' нет RoboMove.");
            return;
        }

        MoveRobotCmd(roboMove, data.time, data.z, data.x, data.jump);
    }

    [Server]
    private void MoveRobotCmd(RoboMove roboMove, float time, int z, int x, int jump)
    {
        roboMove.Move(time, z, x, jump);
    }

    private static void TryRespond(HttpListenerContext context, int statusCode, string body)
    {
        try
        {
            byte[] buffer = Encoding.UTF8.GetBytes(body ?? "");
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = buffer.Length;
            context.Response.OutputStream.Write(buffer, 0, buffer.Length);
            context.Response.OutputStream.Close();
        }
        catch
        {
            // Клиент мог уже отвалиться — это не ошибка сервера.
        }
    }

    private void OnDestroy()
    {
        // Обязательно: без этого после выгрузки сцены порт остаётся занятым,
        // и следующий экземпляр не может подняться.
        _running = false;

        try { _acceptThread?.Interrupt(); } catch { }

        SafeCloseListener();

        // Разблокируем поток приёма, если он ждёт GetContext: Close() прерывает ожидание.
        try
        {
            if (_acceptThread != null && _acceptThread.IsAlive && !_acceptThread.Join(1000))
                _acceptThread = null;
        }
        catch { }

        Interlocked.Exchange(ref _started, 0);
    }

    private void SafeCloseListener()
    {
        if (_listener == null)
            return;

        try
        {
            if (_listener.IsListening)
                _listener.Stop();
            _listener.Close();
        }
        catch (Exception e)
        {
            Debug.LogWarning("[HttpServ] Ошибка при остановке слушателя: " + e.Message);
        }
        finally
        {
            _listener = null;
        }
    }

    private void OnApplicationQuit() => OnDestroy();
}

[System.Serializable]
public class MoveData
{
    public string username;
    public float time;
    public int z;
    public int x;
    public int jump;
}
