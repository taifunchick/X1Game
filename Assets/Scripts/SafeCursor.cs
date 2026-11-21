using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

/// <summary>Requests WebGL pointer lock from a real browser gesture, not a Unity frame.</summary>
public static class SafeCursor
{
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern int X1WebGL_IsPointerLocked();
    [DllImport("__Internal")] private static extern void X1WebGL_SetPointerLockEnabled(int enabled);
#endif

    public static bool IsLocked
    {
        get
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return X1WebGL_IsPointerLocked() == 1;
#else
            return Cursor.lockState == CursorLockMode.Locked;
#endif
        }
    }

    public static void Lock()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        if (!X1DeviceDetect.IsMobileDevice())
            X1WebGL_SetPointerLockEnabled(1);
#else
        Cursor.lockState = CursorLockMode.Locked;
#endif
    }

    public static void Unlock()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        X1WebGL_SetPointerLockEnabled(0);
#else
        Cursor.lockState = CursorLockMode.None;
#endif
        Cursor.visible = true;
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