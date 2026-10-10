using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

/// <summary>
/// Сторож выделенного сервера X1. Создаётся автоматически, в сцене ничего настраивать не нужно.
///
/// Зачем он нужен. Симптом, который он лечит: «сервер завис, игроки не попадают в сцену,
/// systemctl его не перезапускает». Причина в том, что зависший процесс ЖИВ: systemd видит
/// работающий PID и при <c>Restart=on-failure</c> ничего не делает. Единственный способ
/// отличить «живой, но клин заклинил главный поток» от «работает» — следить за тем, что
/// главный поток Unity реально крутит кадры.
///
/// Что делает:
///  1. Считает heartbeat главного потока (каждый Update).
///  2. Отдельный фоновый поток раз в 0.5 с сверяет heartbeat. Если главный поток не подал
///     признаков жизни дольше <see cref="StallTimeoutSeconds"/> — процесс убивается сам,
///     и systemd поднимает его заново (Restart=always). Это и есть «перезапуск на наверняка».
///  3. Раз в <see cref="StatsIntervalSeconds"/> пишет в лог строку диагностики:
///     сколько игроков, FPS, самый долгий кадр, память, количество сборок мусора.
///     По ней видно, ЧТО именно происходит перед падением (CPU, память или GC).
///  4. Ловит «потоп» исключений: если в логе сотни ошибок в секунду — именно они, а не игра,
///     съедают сервер (синхронная запись в лог-файл блокирует главный поток).
///  5. Пишет файл heartbeat для внешнего мониторинга (см. Deploy/scripts/x1-unity-watchdog.sh).
///  6. Отдаёт данные для HTTP-эндпоинта /health (см. SimpleHttpServer).
///
/// Настройки можно менять аргументами командной строки, БЕЗ пересборки:
///   -watchdog 60      через сколько секунд «молчания» главного потока убивать процесс (0 — выключить)
///   -stats 30         как часто писать строку диагностики в лог (0 — не писать)
///   -heartbeat /path  куда писать файл heartbeat (пусто — не писать)
///   -verbose          не глушить Debug.Log на сервере (по умолчанию Info-логи отключаются:
///                     на headless-сервере они пишутся в файл синхронно и сами по себе
///                     способны уронить производительность)
/// </summary>
public sealed class X1ServerWatchdog : MonoBehaviour
{
    /// <summary>Работает только в headless-сборке (выделенный сервер).</summary>
    public static bool ShouldRun => !Application.isEditor && Mirror.Utils.IsHeadless();

    public static X1ServerWatchdog Instance { get; private set; }

    // ---------------- настройки ----------------

    /// <summary>Секунд без единого кадра, после которых процесс убивает сам себя. 0 — слежение выключено.</summary>
    public static float StallTimeoutSeconds = 45f;

    /// <summary>Как часто писать строку диагностики в лог. 0 — не писать.</summary>
    public static float StatsIntervalSeconds = 30f;

    /// <summary>Порог «потопа» сообщений в лог, штук в секунду.</summary>
    public const int LogFloodThreshold = 60;

    /// <summary>Путь к файлу heartbeat. Пусто — файл не пишется.</summary>
    public static string HeartbeatPath = "";

    /// <summary>Не глушить Debug.Log (Info) на сервере.</summary>
    public static bool VerboseLogging;

    // ---------------- состояние ----------------

    // Метка времени последнего кадра главного потока (в тиках Stopwatch).
    // Читается из фонового потока, поэтому только через Interlocked.
    private long _lastFrameTicks;

    private long _frames;
    private long _maxFrameTicks;          // самый долгий кадр с момента старта
    private long _maxFrameTicksWindow;    // самый долгий кадр за последний интервал статистики
    private long _logMessages;            // всего сообщений в лог
    private long _logMessagesWindow;
    private long _logErrorsWindow;
    private string _lastLogSample = "";
    private bool _floodReported;

    private Thread _watchdogThread;
    private volatile bool _running;
    private int _panicked;                // Interlocked-флаг: паникуем только один раз

    private double _nextStatsTime;
    private double _nextHeartbeatWrite;
    private double _windowStart;
    private long _windowFrames;

    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();

    // ---------------- публичная статистика (для /health) ----------------

    /// <summary>Сколько секунд назад главный поток крутил кадр. Растёт, если сервер завис.</summary>
    public static double SecondsSinceLastFrame
    {
        get
        {
            var self = Instance;
            if (self == null) return -1d;
            long last = Interlocked.Read(ref self._lastFrameTicks);
            if (last == 0) return -1d;
            return (System.Diagnostics.Stopwatch.GetTimestamp() - last) / (double)System.Diagnostics.Stopwatch.Frequency;
        }
    }

    /// <summary>Время работы процесса, секунд.</summary>
    public double UptimeSeconds => _clock.Elapsed.TotalSeconds;

    /// <summary>Средний FPS за последний интервал статистики.</summary>
    public double AverageFps { get; private set; }

    /// <summary>Самый долгий кадр за всё время, мс.</summary>
    public double WorstFrameMs => TicksToMs(Interlocked.Read(ref _maxFrameTicks));

    /// <summary>Всего кадров с момента старта.</summary>
    public long Frames => Interlocked.Read(ref _frames);

    // Последний снимок диагностики. Обновляется раз в секунду НА ГЛАВНОМ ПОТОКЕ, а читается
    // фоновым потоком сторожа в момент паники: BuildStatusLine() обращается к SceneManager и
    // NetworkServer, а Unity API с чужого потока вызывать нельзя.
    private static volatile string _lastStatusLine = "";

    /// <summary>Последний снимок диагностики (безопасно читать из любого потока).</summary>
    public static string LastStatusLine => _lastStatusLine;

    /// <summary>Строка диагностики одной строкой. Вызывать ТОЛЬКО из главного потока.</summary>
    public string BuildStatusLine()
    {
        string scene = SceneManager.GetActiveScene().name;
        int players = 0;
        int connections = 0;
        try
        {
            if (NetworkServer.active)
            {
                connections = NetworkServer.connections.Count;
                foreach (var kv in NetworkServer.connections)
                    if (kv.Value.identity != null) players++;
            }
        }
        catch { /* диагностика не должна ронять сервер */ }

        return string.Format(
            CultureInfo.InvariantCulture,
            "scene={0} players={1} connections={2} fps={3:F1} worstFrameMs={4:F1} uptime={5:hh\\:mm\\:ss} managedMemMB={6:F0} systemMemMB={7:F0} gc0={8} gc1={9} gc2={10} logMsgs={11} stale={12:F2}s",
            scene,
            players,
            connections,
            AverageFps,
            WorstFrameMs,
            TimeSpan.FromSeconds(UptimeSeconds),
            GC.GetTotalMemory(false) / 1048576.0,
            (double)SystemInfo.systemMemorySize,   // ОЗУ машины в МБ — с чем сравнивать managedMemMB
            GC.CollectionCount(0),
            GC.CollectionCount(1),
            GC.CollectionCount(2),
            Interlocked.Read(ref _logMessages),
            SecondsSinceLastFrame);
    }

    // ---------------- установка ----------------

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        if (!ShouldRun)
            return;

        if (Instance != null)
            return;

        ParseCommandLine();

        var go = new GameObject("[X1ServerWatchdog]");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<X1ServerWatchdog>();
    }

    /// <summary>Читает настройки из аргументов командной строки (см. справку класса).</summary>
    private static void ParseCommandLine()
    {
        string watchdog = X1NetworkManager.CommandLineArg("-watchdog");
        if (float.TryParse(watchdog, NumberStyles.Float, CultureInfo.InvariantCulture, out float w) && w >= 0f)
            StallTimeoutSeconds = w;

        string stats = X1NetworkManager.CommandLineArg("-stats");
        if (float.TryParse(stats, NumberStyles.Float, CultureInfo.InvariantCulture, out float s) && s >= 0f)
            StatsIntervalSeconds = s;

        string heartbeat = X1NetworkManager.CommandLineArg("-heartbeat");
        if (!string.IsNullOrWhiteSpace(heartbeat))
            HeartbeatPath = heartbeat.Trim();

        VerboseLogging = X1NetworkManager.HasCommandLineFlag("-verbose");

        // Значения по умолчанию, если аргументов нет.
        if (string.IsNullOrWhiteSpace(heartbeat))
        {
            try
            {
                HeartbeatPath = Path.Combine(Directory.GetCurrentDirectory(), "server.heartbeat");
            }
            catch
            {
                HeartbeatPath = "";
            }
        }
    }

    private void Awake()
    {
        Interlocked.Exchange(ref _lastFrameTicks, System.Diagnostics.Stopwatch.GetTimestamp());

        Application.logMessageReceivedThreaded += OnLogMessage;

        // Смена сцены занимает секунды — сбрасываем heartbeat, чтобы не было ложной паники.
        SceneManager.sceneLoaded += OnSceneEvent;
        SceneManager.sceneUnloaded += OnSceneUnloadedEvent;
        SceneManager.activeSceneChanged += OnActiveSceneChanged;

        _nextStatsTime = UptimeSeconds + StatsIntervalSeconds;
        _windowStart = UptimeSeconds;

        _running = true;
        _watchdogThread = new Thread(WatchdogLoop)
        {
            IsBackground = true,
            Name = "X1ServerWatchdog"
        };
        _watchdogThread.Start();

        Console.WriteLine($"[X1ServerWatchdog] запущен. stallTimeout={StallTimeoutSeconds:0.#}s stats={StatsIntervalSeconds:0.#}s heartbeat='{HeartbeatPath}' verbose={VerboseLogging} pid={GetCurrentPid()}");

        // Стартовая строка пишется ДО включения фильтра — иначе её саму и не будет видно.
        Debug.Log($"[X1ServerWatchdog] запущен. stallTimeout={StallTimeoutSeconds:0.#} c, " +
                  $"статистика каждые {StatsIntervalSeconds:0.#} c, heartbeat='{HeartbeatPath}', " +
                  $"verbose={VerboseLogging}.");

        // На headless-сервере Debug.Log пишется в лог синхронно. Один Debug.Log на каждый выстрел,
        // попадание или контакт мяча при 30–60 тиках в секунду — это уже ощутимая нагрузка на диск,
        // а «потоп» из сообщений способен полностью остановить главный поток.
        // Поэтому Info-логи на сервере по умолчанию глушатся; предупреждения и ошибки остаются.
        // Вернуть полное логирование: запустить сервер с аргументом -verbose.
        if (!VerboseLogging)
        {
            X1Log.SuppressInfo();

            // Стек-трейсы для Warning не нужны: именно их генерация дороже всего при потоке сообщений.
            Application.SetStackTraceLogType(LogType.Warning, StackTraceLogType.None);
        }
        else
        {
            X1Log.Restore();
        }
    }

    private void OnDestroy()
    {
        _running = false;
        Application.logMessageReceivedThreaded -= OnLogMessage;
        SceneManager.sceneLoaded -= OnSceneEvent;
        SceneManager.sceneUnloaded -= OnSceneUnloadedEvent;
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;

        try { _watchdogThread?.Interrupt(); } catch { }
        if (Instance == this) Instance = null;
    }

    private void OnSceneEvent(Scene scene, LoadSceneMode mode) => Touch();
    private void OnSceneUnloadedEvent(Scene scene) => Touch();
    private void OnActiveSceneChanged(Scene from, Scene to) => Touch();

    /// <summary>Сбросить heartbeat (главный поток точно жив).</summary>
    private void Touch() => Interlocked.Exchange(ref _lastFrameTicks, System.Diagnostics.Stopwatch.GetTimestamp());

    // ---------------- главный поток ----------------

    private void Update()
    {
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        long prev = Interlocked.Exchange(ref _lastFrameTicks, now);

        Interlocked.Increment(ref _frames);
        _windowFrames++;

        long frameTicks = now - prev;
        if (frameTicks > Interlocked.Read(ref _maxFrameTicks))
            Interlocked.Exchange(ref _maxFrameTicks, frameTicks);
        if (frameTicks > _maxFrameTicksWindow)
            _maxFrameTicksWindow = frameTicks;

        double uptime = UptimeSeconds;

        if (StatsIntervalSeconds > 0f && uptime >= _nextStatsTime)
        {
            double window = uptime - _windowStart;
            AverageFps = window > 0.001 ? _windowFrames / window : 0d;
            _windowFrames = 0;
            _windowStart = uptime;
            _nextStatsTime = uptime + StatsIntervalSeconds;

            long msgs = Interlocked.Read(ref _logMessagesWindow);
            long errs = Interlocked.Read(ref _logErrorsWindow);
            Interlocked.Exchange(ref _logMessagesWindow, 0);
            Interlocked.Exchange(ref _logErrorsWindow, 0);

            double worstWindowMs = TicksToMs(_maxFrameTicksWindow);
            _maxFrameTicksWindow = 0;

            Debug.Log($"[X1ServerWatchdog] {BuildStatusLine()} | logMsgs/window={msgs} logErrors/window={errs} worstFrameMs/window={worstWindowMs:F1}");

            if (msgs > LogFloodThreshold * StatsIntervalSeconds)
            {
                string sample = _lastLogSample;
                Debug.LogError($"[X1ServerWatchdog] ПОТОП СООБЩЕНИЙ В ЛОГ: {msgs} шт. за {StatsIntervalSeconds:0} c. " +
                               "Синхронная запись в лог-файл блокирует главный поток и сама по себе «вешает» сервер. " +
                               $"Последнее сообщение: {sample}");
            }
        }

        // Файл heartbeat — раз в секунду. Пишется атомарно (tmp + move), чтобы внешний
        // мониторинг никогда не прочитал половину строки.
        if (uptime >= _nextHeartbeatWrite)
        {
            _nextHeartbeatWrite = uptime + 1.0;

            // Снимок для паники: если главный поток сейчас заклинит, фоновый поток сторожа
            // возьмёт эту строку и напишет её в лог вместо того, чтобы лезть в Unity API.
            try { _lastStatusLine = BuildStatusLine(); } catch { }

            if (!string.IsNullOrEmpty(HeartbeatPath))
                WriteHeartbeatFile();
        }
    }

    private void WriteHeartbeatFile()
    {
        try
        {
            string tmp = HeartbeatPath + ".tmp";
            File.WriteAllText(tmp, string.Format(
                CultureInfo.InvariantCulture,
                "{0:F3} {1} {2}\n",
                DateTime.UtcNow.ToString("o"),
                Interlocked.Read(ref _frames),
                NetworkServer.active ? NetworkServer.connections.Count : -1));

            if (File.Exists(HeartbeatPath)) File.Delete(HeartbeatPath);
            File.Move(tmp, HeartbeatPath);
        }
        catch (Exception e)
        {
            // Файл heartbeat — удобство, а не обязанность. Один раз предупредили и больше не пишем.
            Debug.LogWarning($"[X1ServerWatchdog] Не удалось записать heartbeat '{HeartbeatPath}': {e.Message}. Файл heartbeat отключён.");
            HeartbeatPath = "";
        }
    }

    // ---------------- учёт лога ----------------

    private void OnLogMessage(string condition, string stackTrace, LogType type)
    {
        Interlocked.Increment(ref _logMessages);
        Interlocked.Increment(ref _logMessagesWindow);

        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
        {
            Interlocked.Increment(ref _logErrorsWindow);

            if (!_floodReported && Interlocked.Read(ref _logErrorsWindow) > LogFloodThreshold)
            {
                _floodReported = true;
                // Пишем в stdout напрямую: Debug.Log может быть уже непригоден, если лог-поток забит.
                Console.WriteLine("[X1ServerWatchdog] ВНИМАНИЕ: поток ошибок в логе. Последнее: " + condition);
            }
        }

        // Держим короткий образец последнего сообщения для диагностики.
        if (condition != null)
            _lastLogSample = condition.Length > 400 ? condition.Substring(0, 400) : condition;
    }

    // ---------------- фоновый поток: детектор зависания ----------------

    private void WatchdogLoop()
    {
        // На старте даём серверу время загрузить сцену: первая загрузка Lasertag на слабом VPS
        // может занять десятки секунд, и это не зависание.
        Thread.Sleep(TimeSpan.FromSeconds(Math.Min(30f, StallTimeoutSeconds * 0.5f)));

        while (_running)
        {
            try
            {
                Thread.Sleep(500);
            }
            catch (ThreadInterruptedException)
            {
                return;
            }

            if (!_running || StallTimeoutSeconds <= 0f)
                continue;

            long last = Interlocked.Read(ref _lastFrameTicks);
            if (last == 0)
                continue;

            double stall = (System.Diagnostics.Stopwatch.GetTimestamp() - last) / (double)System.Diagnostics.Stopwatch.Frequency;
            if (stall >= StallTimeoutSeconds)
                Panic(stall);
        }
    }

    /// <summary>
    /// Главный поток не отвечает — процесс надо убить, иначе systemd (Restart=on-failure)
    /// будет вечно держать «живой труп», а игроки не смогут зайти в сцену.
    /// </summary>
    private void Panic(double stallSeconds)
    {
        // Паникуем ровно один раз.
        if (Interlocked.Exchange(ref _panicked, 1) != 0)
            return;

        string reason = string.Format(
            CultureInfo.InvariantCulture,
            "[X1ServerWatchdog] FATAL: главный поток Unity не обновлялся {0:F1} c (порог {1:F1} c). " +
            "Сервер завис. Завершаем процесс, чтобы systemd поднял его заново. " +
            "Последняя статистика: {2}",
            stallSeconds, StallTimeoutSeconds, SafeStatusLine());

        // Только Console: Debug.Log на зависшем главном потоке может сам заблокироваться.
        try { Console.Error.WriteLine(reason); Console.Error.Flush(); } catch { }
        try { Console.WriteLine(reason); Console.Out.Flush(); } catch { }

        // Маркер для внешнего мониторинга и для разбора «почему сервер перезапустился».
        try
        {
            string path = string.IsNullOrEmpty(HeartbeatPath)
                ? Path.Combine(Directory.GetCurrentDirectory(), "server.stalled")
                : HeartbeatPath + ".stalled";
            File.WriteAllText(path, reason + Environment.NewLine);
        }
        catch { }

        // Завершаемся с кодом 2 (не 0), чтобы systemd с Restart=always точно поднял нас снова.
        var killer = new Thread(() =>
        {
            Thread.Sleep(3000);
            try { System.Diagnostics.Process.GetCurrentProcess().Kill(); } catch { }
        })
        {
            IsBackground = true
        };
        killer.Start();

        Environment.Exit(2);
    }

    /// <summary>
    /// Вызывается из фонового потока, поэтому НЕ пересобирает строку, а берёт последний снимок.
    /// </summary>
    private static string SafeStatusLine()
    {
        string line = _lastStatusLine;
        return string.IsNullOrEmpty(line) ? "n/a" : line;
    }

    // ---------------- утилиты ----------------

    private static double TicksToMs(long ticks) =>
        ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

    private static int GetCurrentPid()
    {
        try { return System.Diagnostics.Process.GetCurrentProcess().Id; }
        catch { return -1; }
    }
}
