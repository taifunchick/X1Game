using UnityEngine;
using Mirror;
using StarterAssets;

public class MobileMultiTouchSetup : MonoBehaviour
{
    [Header("Canvas References")]
    [Tooltip("Очисти это поле в инспекторе, пусть ищет сам (None)")]
    public GameObject joystickCanvas;

    [Tooltip("Очисти это поле в инспекторе, пусть ищет сам (None)")]
    public GameObject touchZoneCanvas;

    [Header("Multi-Touch Look Settings")]
    [SerializeField] private float lookSensitivity = 6f;
    [SerializeField] private float joystickBufferRadius = 80f;

    private void Start() => AutoSetup();

    private void AutoSetup()
    {
        if (joystickCanvas == null) joystickCanvas = FindCanvasByName("Joysticks");
        if (touchZoneCanvas == null) touchZoneCanvas = FindCanvasByName("TouchZone");

        ApplyPlatformVisibility();
        DisableLookTouchZone(); // ВАЖНО: отключаем старую зону свайпа, чтобы работал MultiTouchMobileInput
        SetupMultiTouchInput();

        Debug.Log(string.Format(
            "[MobileMultiTouchSetup] Configuration complete. mobileUI={0}, platform={1}, isMobilePlatform={2}, webglMobileBrowser={3}, joystickCanvas={4}, touchZoneCanvas={5}",
            ShouldMobileUIBeActive(), Application.platform, Application.isMobilePlatform,
            IsWebGLMobileBrowser(),
            joystickCanvas != null ? joystickCanvas.name : "null",
            touchZoneCanvas != null ? touchZoneCanvas.name : "null"));
    }

    private void ApplyPlatformVisibility()
    {
        bool active = ShouldMobileUIBeActive();
        SetCanvasActive(joystickCanvas, active);
        SetCanvasActive(touchZoneCanvas, active);
    }

    private static bool ShouldMobileUIBeActive()
    {
#if UNITY_EDITOR
        return true;
#else
        // На настоящих мобильных платформах (Android / iOS сборки) всё просто.
        if (Application.isMobilePlatform) return true;

        if (Application.platform == RuntimePlatform.WebGLPlayer)
        {
            // В WebGL Application.isMobilePlatform всегда возвращает false, даже
            // когда сборка открыта на iPhone или Android — платформа там WebGLPlayer.
            // Поэтому единственный надёжный способ узнать, что мы на мобильном —
            // спросить браузер (userAgent + тач + тип указателя) через JS-плагин.
            // Никаких проверок размера экрана в пикселях: они неточны.
            return IsWebGLMobileBrowser();
        }

        return false;
#endif
    }

    private void SetCanvasActive(GameObject canvas, bool active)
    {
        if (canvas == null) return;
        GameObject root = ResolveCanvasRoot(canvas);
        if (root != null && root.activeSelf != active)
            root.SetActive(active);
    }

    private void DisableLookTouchZone()
    {
        GameObject root = ResolveCanvasRoot(touchZoneCanvas);
        if (root == null) return;
        if (!root.activeSelf) return;

        var transforms = root.GetComponentsInChildren<Transform>(true);
        foreach (var child in transforms)
        {
            if (child.name.Contains("Look") && child.gameObject.activeSelf)
            {
                child.gameObject.SetActive(false);
                Debug.Log("[MobileMultiTouchSetup] Disabled old TouchZone: " + child.name);
            }
        }
    }

    private void SetupMultiTouchInput()
    {
        MultiTouchMobileInput mtInput = FindObjectOfType<MultiTouchMobileInput>();
        if (mtInput == null)
        {
            var obj = new GameObject("MultiTouchMobileInput");
            mtInput = obj.AddComponent<MultiTouchMobileInput>();
        }

        mtInput.lookSensitivity = lookSensitivity;
        mtInput.moveJoystickBufferRadius = joystickBufferRadius;
        mtInput.cameraJoystickBufferRadius = joystickBufferRadius;
    }

    private GameObject ResolveCanvasRoot(GameObject target)
    {
        if (target == null) return null;
        if (target.GetComponent<Canvas>() != null) return target;
        Canvas canvas = target.GetComponentInParent<Canvas>();
        return canvas != null ? canvas.gameObject : null;
    }

    private GameObject FindCanvasByName(string namePart)
    {
        foreach (GameObject go in FindObjectsOfType<GameObject>(true))
        {
            if (go.name.Contains(namePart) && (go.GetComponent<Canvas>() != null || go.GetComponentInParent<Canvas>() != null))
                return go;
        }
        return null;
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    [System.Runtime.InteropServices.DllImport("__Internal")]
    private static extern int IsMobileBrowser();
#endif

    private static bool IsWebGLMobileBrowser()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        try { return IsMobileBrowser() == 1; }
        catch { return false; }
#else
        return false;
#endif
    }
}