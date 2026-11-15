using UnityEngine;
using Mirror;
using StarterAssets;

public class MobileMultiTouchSetup : MonoBehaviour
{
    [Header("Canvas References")]
    [Tooltip("Канвас с джойстиками. В инспекторе ОЧИСТИ это поле, пусть ищет сам.")]
    public GameObject joystickCanvas;

    [Tooltip("Канвас с TouchZone. В инспекторе ОЧИСТИ это поле, пусть ищет сам.")]
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
        SetupMultiTouchInput();

        Debug.Log("[MobileMultiTouchSetup] Configuration complete.");
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
        if (Application.isMobilePlatform) return true;
        if (Application.platform == RuntimePlatform.WebGLPlayer)
        {
            if (IsWebGLMobileBrowser()) return true;
            return Input.touchSupported;
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
            if (go.name.Contains(namePart) &&
                (go.GetComponent<Canvas>() != null || go.GetComponentInParent<Canvas>() != null))
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