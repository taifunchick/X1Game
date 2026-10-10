using System;
using UnityEngine;

/// <summary>
/// Управление логированием на выделенном сервере.
///
/// Зачем это нужно. Unity пишет лог СИНХРОННО: каждый <c>Debug.Log</c> на headless-сервере —
/// это запись в файл/stdout в главном потоке. Игра логирует много и часто (попадание, выбор
/// команды, подключение, контакт мяча), поэтому при активном матче лог сам по себе становится
/// заметной нагрузкой, а «потоп» из исключений способен полностью остановить сервер.
///
/// Дополнительно: <c>Debug.unityLogger.filterLogType</c> ведёт себя неочевидно (порядок
/// значений <c>LogType</c> не совпадает с интуитивной «важностью»), поэтому здесь используется
/// собственная обёртка над <see cref="ILogHandler"/> — она глушит ровно Info-сообщения
/// (<see cref="LogType.Log"/>) и ничего больше. Предупреждения, ошибки и исключения
/// остаются в логе всегда.
///
/// Важно про интерполяцию: фильтр отбрасывает сообщение УЖЕ внутри <c>Debug.Log</c>, а строка
/// <c>$"..."</c> формируется ДО вызова. Поэтому в горячих местах проверку нужно делать самому:
/// <code>if (X1Log.InfoEnabled) Debug.Log($"...");</code>
/// </summary>
public static class X1Log
{
    private static bool _infoSuppressed;
    private static ILogHandler _originalHandler;

    /// <summary>true, если Info-сообщения (Debug.Log) доходят до лога.</summary>
    public static bool InfoEnabled => !_infoSuppressed;

    /// <summary>Уже применено глушение Info-сообщений.</summary>
    public static bool IsSuppressed => _infoSuppressed;

    /// <summary>
    /// Отключить Info-сообщения (Debug.Log). Предупреждения, ошибки и исключения продолжают писаться.
    /// Повторный вызов ничего не делает.
    /// </summary>
    public static void SuppressInfo()
    {
        if (_infoSuppressed)
            return;

        var logger = Debug.unityLogger;
        if (logger == null)
            return;

        try
        {
            _originalHandler = logger.logHandler;
            logger.logHandler = new InfoSuppressingLogHandler(_originalHandler);

            // Стек-трейсы для Info не нужны вовсе: их генерация дороже самой записи.
            Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);

            _infoSuppressed = true;
        }
        catch (Exception e)
        {
            Debug.LogWarning("[X1Log] Не удалось ограничить логирование: " + e.Message);
        }
    }

    /// <summary>Вернуть полное логирование (например, по аргументу -verbose).</summary>
    public static void Restore()
    {
        if (!_infoSuppressed)
            return;

        try
        {
            if (_originalHandler != null)
                Debug.unityLogger.logHandler = _originalHandler;

            Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.ScriptOnly);
        }
        catch { }

        _infoSuppressed = false;
    }

    /// <summary>Debug.Log, который ничего не делает и ничего не выделяет, если Info глушится.</summary>
    public static void Info(string message)
    {
        if (_infoSuppressed)
            return;

        Debug.Log(message);
    }

    /// <summary>
    /// Обёртка, отбрасывающая только <see cref="LogType.Log"/>.
    /// Всё остальное передаётся штатному обработчику Unity без изменений.
    /// </summary>
    private sealed class InfoSuppressingLogHandler : ILogHandler
    {
        private readonly ILogHandler _inner;

        public InfoSuppressingLogHandler(ILogHandler inner) => _inner = inner;

        public void LogFormat(LogType logType, UnityEngine.Object context, string format, params object[] args)
        {
            if (logType == LogType.Log)
                return;

            _inner?.LogFormat(logType, context, format, args);
        }

        public void LogException(Exception exception, UnityEngine.Object context)
        {
            _inner?.LogException(exception, context);
        }
    }
}
