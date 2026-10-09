using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Сообщает SafeCursor, что пользователь что-то нажал.
///
/// Браузеры разрешают захват мыши только сразу после настоящего пользовательского
/// действия, поэтому без этого <c>SafeCursor.Lock()</c> не сможет захватить курсор
/// и игра останется с невидимым курсором и неработающим обзором на ПК.
///
/// Вешается автоматически при старте игры, не требует настройки в сцене.
/// </summary>
[DefaultExecutionOrder(-5000)]
public class InputGestureBootstrap : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Install()
    {
        // Не дублируем, если объект уже есть в сцене вручную.
        if (FindAnyObjectByType<InputGestureBootstrap>() != null)
            return;

        var go = new GameObject("InputGestureBootstrap");
        DontDestroyOnLoad(go);
        go.AddComponent<InputGestureBootstrap>();
    }

    private void Update()
    {
#if ENABLE_INPUT_SYSTEM
        // Любое действие с мышью, тачем или клавишей считаем жестом.
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            SafeCursor.NotifyUserGesture();
            return;
        }

        if (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame)
        {
            SafeCursor.NotifyUserGesture();
            return;
        }

        if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
        {
            SafeCursor.NotifyUserGesture();
            return;
        }

        if (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame)
        {
            SafeCursor.NotifyUserGesture();
        }
#else
        if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) || Input.anyKeyDown)
            SafeCursor.NotifyUserGesture();
#endif
    }
}