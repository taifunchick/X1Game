using UnityEngine;

/// <summary>Общий детектор для мобильной панели и ввода. В WebGL результат кэшируется.</summary>
public static class X1DeviceDetect
{
#if UNITY_WEBGL && !UNITY_EDITOR
    [System.Runtime.InteropServices.DllImport("__Internal")]
    private static extern int IsMobileBrowser();
#endif

    // -1 = ещё не вычислено, 0 = десктоп, 1 = мобильное
    private static int _webglCache = -1;

    /// <summary>true, если игру открыли на мобильном устройстве (телефон/планшет).</summary>
    public static bool IsMobileDevice()
    {
#if UNITY_EDITOR
        return true;
#else
        if (Application.platform == RuntimePlatform.WebGLPlayer) return IsWebGLMobileBrowser();
        if (Application.isMobilePlatform) return true;
        if (Application.platform == RuntimePlatform.Android ||
            Application.platform == RuntimePlatform.IPhonePlayer) return true;
        if (SystemInfo.deviceType == DeviceType.Handheld) return true;
        return false;
#endif
    }

    private static bool IsWebGLMobileBrowser()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        if (_webglCache != -1) return _webglCache == 1;

        // Слой 1: JS-плагин MobileDetect.jslib (userAgent токены + iPadOS + pointer:coarse).
        bool jsSaysMobile = false;
        bool jsAvailable = false;
        try
        {
            int browserDevice = IsMobileBrowser();
            jsSaysMobile = browserDevice == 1;
            jsAvailable = browserDevice >= 0;
        }
        catch (System.Exception e)
        {
            // jslib не слинкован (типичный признак: сломанный .jslib.meta без PluginImporter/WebGL).
            Debug.LogWarning("[X1DeviceDetect] jslib IsMobileBrowser() НЕДОСТУПЕН в сборке: " + e.Message
                + " — полагаюсь на C#-фолбэки (touchSupported / InputSystem).");
        }

        // Слой 2: старый Input Manager. На WebGL touchSupported берётся из возможностей браузера.
        bool legacyTouch = false;
        try { legacyTouch = Input.touchSupported; }
        catch (System.Exception e) { Debug.LogWarning("[X1DeviceDetect] Input.touchSupported недоступен: " + e.Message); }

        // Слой 3: новая Input System — зарегистрированное тач-устройство.
        bool newTouch = false;
        try { newTouch = UnityEngine.InputSystem.Touchscreen.current != null; }
        catch (System.Exception e) { Debug.LogWarning("[X1DeviceDetect] InputSystem.Touchscreen недоступен: " + e.Message); }

        // A touchscreen alone doesn't make a desktop PC a phone. Use Unity's
        // touch capabilities only if the browser-side classification is unavailable.
        bool result = jsAvailable ? jsSaysMobile : legacyTouch || newTouch;
        _webglCache = result ? 1 : 0;

        Debug.Log($"[X1DeviceDetect] mobile={result}, browserDetection={jsAvailable}, " +
            $"touchSupported={legacyTouch}, touchscreen={newTouch}");

        return result;
#else
        return false;
#endif
    }
}
