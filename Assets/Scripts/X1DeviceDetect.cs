using UnityEngine;

/// <summary>
/// Единое и надёжное определение мобильного устройства для всей игры.
///
/// Почему одной проверки мало (что мы уже наступали на эти грабли):
///  - Application.isMobilePlatform в WebGL ВСЕГДА false, даже на iPhone/Android;
///  - jslib (MobileDetect.jslib) может попросту НЕ попасть в сборку: если у .jslib сломан
///    .meta (нет секции PluginImporter с WebGL:enabled:1), Unity плагин не линкует,
///    DllImport падает с исключением, а старый код молча возвращал false — панель не показывалась;
///  - userAgent в режиме «Версия для ПК» (Android Chrome) не содержит токенов Android/Mobile,
///    хотя тач-экран при этом есть и работает;
///  - Input.touchSupported / InputSystem.Touchscreen отражают ВВОД УСТРОЙСТВА и не зависят от userAgent.
///
/// Итог: «мобильное» = сработал ЛЮБОЙ слой. Результат кэшируется (безопасно звать каждый кадр).
/// </summary>
public static class X1DeviceDetect
{
#if UNITY_WEBGL && !UNITY_EDITOR
    [System.Runtime.InteropServices.DllImport("__Internal")]
    private static extern int IsMobileBrowser();
#endif

    // -1 = ещё не вычислено, 0 = десктоп, 1 = мобильное
    private static int _webglCache = -1;
    private static bool? _logged;

    /// <summary>true, если игру открыли на мобильном устройстве (телефон/планшет).</summary>
    public static bool IsMobileDevice()
    {
#if UNITY_EDITOR
        return true;
#else
        if (Application.isMobilePlatform) return true;
        if (Application.platform == RuntimePlatform.Android ||
            Application.platform == RuntimePlatform.IPhonePlayer) return true;
        if (SystemInfo.deviceType == DeviceType.Handheld) return true;
        if (Application.platform == RuntimePlatform.WebGLPlayer) return IsWebGLMobileBrowser();
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
            jsSaysMobile = IsMobileBrowser() == 1;
            jsAvailable = true;
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

        // «Точно на телефоне» = любой слой подтвердил тач. js=0 при живом тач-экране случается
        // (десктопный userAgent / «версия для ПК»), поэтому фолбэки — это полноценные детекторы,
        // а не декорация на случай падения jslib.
        bool result = jsSaysMobile || legacyTouch || newTouch;
        _webglCache = result ? 1 : 0;

        if (!_logged.HasValue)
        {
            _logged = true;
            Debug.Log(string.Format(
                "[X1DeviceDetect] Итог: мобильное={0} (jslib={1}, jsДоступен={2}, touchSupported={3}, touchscreen={4})",
                result, jsSaysMobile, jsAvailable, legacyTouch, newTouch));
        }

        return result;
#else
        return false;
#endif
    }
}
