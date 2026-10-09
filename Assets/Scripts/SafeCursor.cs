using System;
using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

/// <summary>
/// Безопасная работа с захватом мыши.
///
/// Проблема: обычный <c>Cursor.lockState = CursorLockMode.Locked</c> в WebGL вызывает
/// внутри Unity <c>Module.requestPointerLock()</c> и НЕ подписывается на отказ промиса.
/// Браузер отклоняет запрос без жеста пользователя («A user gesture is required…»)
/// или сразу после выхода по ESC («Pointer lock cannot be acquired immediately after the
/// user has exited the lock»). Необработанный rejection попадает в обработчик ошибок
/// Emscripten, который глушит главный цикл — игра зависает и рвётся WebSocket.
///
/// Здесь запрос уходит в JS-плагин X1WebGL.jslib, который ловит отказ сам.
/// Дополнительно соблюдаем паузу после выхода из захвата и на ПК/WebGL вне
/// пользовательского жеста не дёргаем захват заново.
/// </summary>
public static class SafeCursor
{
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern int X1WebGL_IsPointerLocked();
    [DllImport("__Internal")] private static extern int X1WebGL_TryPointerLock();
    [DllImport("__Internal")] private static extern void X1WebGL_ExitPointerLock();
#endif

    /// <summary>Пауза после выхода из захвата, секунды. Защита от «immediately after the user has exited».</summary>
    private const float MinTimeBetweenLockRequests = 1.0f;

    /// <summary>Сколько ждать пользовательский жест перед попыткой захвата, секунды.</summary>
    private const float GestureValidityWindow = 10f;

    private static float _lastExitTime = -999f;
    private static float _lastLockRequestTime = -999f;
    private static float _lastGestureTime = -999f;

    private static bool _locked;

    /// <summary>Вызывать из обработчика клика/тапа, чтобы разрешить захват мыши.</summary>
    public static void NotifyUserGesture()
    {
        _lastGestureTime = Time.unscaledTime;
    }

    public static bool IsLocked
    {
        get
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return X1WebGL_IsPointerLocked() == 1;
#else
            return _locked;
#endif
        }
    }

    /// <summary>
    /// Захватить курсор (как CursorLockMode.Locked).
    /// На WebGL захват реально произойдёт только если был свежий пользовательский жест.
    /// </summary>
    public static void Lock()
    {
        if (_locked && IsLocked)
            return;

        _locked = true;

#if UNITY_WEBGL && !UNITY_EDITOR
        float now = Time.unscaledTime;

        // Только после жеста пользователя: иначе браузер всё равно откажет,
        // а лишний необработанный rejection нам не нужен.
        if (now - _lastGestureTime > GestureValidityWindow)
            return;

        // Сразу после выхода из захвата повторный запрос браузер запрещает.
        if (now - _lastExitTime < MinTimeBetweenLockRequests)
            return;

        if (now - _lastLockRequestTime < MinTimeBetweenLockRequests)
            return;

        _lastLockRequestTime = now;
        X1WebGL_TryPointerLock();
        Cursor.visible = false;
#else
        Cursor.lockState = CursorLockMode.Locked;
#endif
    }

    /// <summary>Освободить курсор (как CursorLockMode.None).</summary>
    public static void Unlock()
    {
        if (!_locked)
            return;

        _locked = false;
        _lastExitTime = Time.unscaledTime;

#if UNITY_WEBGL && !UNITY_EDITOR
        X1WebGL_ExitPointerLock();
        Cursor.visible = true;
#else
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
#endif
    }

    /// <summary>Установить состояние: true — захватить, false — освободить.</summary>
    public static void SetLocked(bool locked)
    {
        if (locked)
            Lock();
        else
            Unlock();
    }

    /// <summary>
    /// Найти Infima Character локального игрока — нужно, чтобы снять его флаг cursorLocked.
    /// Без этого персонаж продолжает гейтить ввод движения/взгляда при освобождённом курсоре.
    /// </summary>
    public static void SetInfimaCursorLocked(bool locked)
    {
        var character = FindLocalCharacter();
        if (character == null)
            return;

        if (locked)
            character.LockCursor();
        else
            character.UnlockCursor();
    }

#if UNITY_2021_1_OR_NEWER
    private static InfimaGames.LowPolyShooterPack.Character FindLocalCharacter()
    {
        foreach (var character in UnityEngine.Object.FindObjectsByType<InfimaGames.LowPolyShooterPack.Character>(
                     FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            // Локальный игрок — тот, у кого включён PlayerInput и нет второго локального.
            var networkIdentity = character.GetComponent<Mirror.NetworkIdentity>();
            if (networkIdentity != null && networkIdentity.isLocalPlayer)
                return character;
        }

        return null;
    }
#else
    private static InfimaGames.LowPolyShooterPack.Character FindLocalCharacter()
    {
        foreach (var character in UnityEngine.Object.FindObjectsOfType<InfimaGames.LowPolyShooterPack.Character>())
        {
            var networkIdentity = character.GetComponent<Mirror.NetworkIdentity>();
            if (networkIdentity != null && networkIdentity.IsLocalPlayer)
                return character;
        }

        return null;
    }
#endif
}