using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using Mirror;
using StarterAssets;

[DefaultExecutionOrder(1000)] 
public class MultiTouchMobileInput : MonoBehaviour
{
    [Header("References")]
    public StarterAssetsInputs starterAssetsInputs;

    [Header("Look Settings")]
    public float lookSensitivity = 6f;

    [Header("Joystick Exclusion Zones")]
    public RectTransform moveJoystickRect;
    public float moveJoystickBufferRadius = 80f;
    public RectTransform cameraJoystickRect;
    public float cameraJoystickBufferRadius = 80f;

    [Header("Debug")]
    [SerializeField] private bool debugLog = false;

    public static MultiTouchMobileInput Instance { get; private set; }

    private Vector2 _currentLookInput;
    private bool _isDraggingLook;
    private int _activeLookTouchId = -1;
    private Vector2 _lastTouchPosition;
    private bool _mouseLookActive;
    private Vector2 _lastMousePosition;

    private readonly HashSet<int> _uiFingers = new HashSet<int>();
    private readonly HashSet<int> _moveJoystickFingers = new HashSet<int>();
    private readonly HashSet<int> _lookJoystickFingers = new HashSet<int>();

    private int _lastMoveJoystickActivityFrame = -1;

    private UIVirtualJoystick[] _allJoysticks;
    private static FieldInfo _axisLookField;
    private float _lastRefreshTime;

    private UnityEngine.InputSystem.PlayerInput _playerInput;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        Input.simulateMouseWithTouches = false;
#if ENABLE_INPUT_SYSTEM
        try { UnityEngine.InputSystem.EnhancedTouch.TouchSimulation.Disable(); } catch { }
#endif
    }

    private void Start()
    {
        if (moveJoystickRect == null) moveJoystickRect = AutoFindJoystickRect(true);
        if (cameraJoystickRect == null) cameraJoystickRect = AutoFindJoystickRect(false);

        _allJoysticks = FindObjectsOfType<UIVirtualJoystick>(true);
    }

    private RectTransform AutoFindJoystickRect(bool isMove)
    {
        foreach (var j in FindObjectsOfType<UIVirtualJoystick>(true))
        {
            string n = j.transform.name;
            if (isMove && (n.Contains("Move") || n.Contains("Movement"))) return j.transform as RectTransform;
            if (!isMove && (n.Contains("Look") || n.Contains("Camera"))) return j.transform as RectTransform;
        }
        return null;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void OnMoveJoystickDrag(Vector2 moveInput)
    {
        if (starterAssetsInputs != null)
            starterAssetsInputs.MoveInput(moveInput);

        _lastMoveJoystickActivityFrame = Time.frameCount;
    }

    private void Update()
    {
        ResolveStarterAssetsInputs();

        if (NetworkClient.localPlayer != null)
        {
#if ENABLE_INPUT_SYSTEM
            if (_playerInput == null)
            {
                _playerInput = NetworkClient.localPlayer.GetComponent<UnityEngine.InputSystem.PlayerInput>();
                if (_playerInput != null)
                {
                    var lookAction = _playerInput.actions?.FindAction("Look");
                    if (lookAction != null) lookAction.Disable();
                }
            }
#endif
        }
    }

    private void ResolveStarterAssetsInputs()
    {
        if (starterAssetsInputs == null)
        {
            if (NetworkClient.localPlayer != null)
                starterAssetsInputs = NetworkClient.localPlayer.GetComponent<StarterAssetsInputs>();

            if (starterAssetsInputs == null)
                starterAssetsInputs = FindObjectOfType<StarterAssetsInputs>(true);
        }

        if (starterAssetsInputs != null)
            starterAssetsInputs.cursorInputForLook = false;
    }

    private void LateUpdate()
    {
        if (Time.time - _lastRefreshTime > 1f)
        {
            _lastRefreshTime = Time.time;
            if (_allJoysticks == null || _allJoysticks.Length == 0)
                _allJoysticks = FindObjectsOfType<UIVirtualJoystick>(true);
        }

        ProcessTouches();

        bool isLookJoystickActive = _lookJoystickFingers.Count > 0;
        bool hasActiveLookInput = _activeLookTouchId != -1 || _mouseLookActive;

        if (isLookJoystickActive)
        {
            // Джойстик камеры активен. Не мешаем ему.
        }
        else if (hasActiveLookInput)
        {
            // Свайп по пустому экрану или удержание мыши. Передаем обе оси (X и Y).
            ApplyCharacterLook(_currentLookInput);
        }
        else
        {
            // Сброс, если ничего не нажато.
            ApplyCharacterLook(Vector2.zero);
        }
    }

    private void ProcessTouches()
    {
        UpdateJoystickFingers();

        if (_activeLookTouchId != -1 && _uiFingers.Contains(_activeLookTouchId))
        {
            _activeLookTouchId = -1;
            _isDraggingLook = false;
        }

        _currentLookInput = Vector2.zero;

        for (int i = 0; i < Input.touchCount; i++)
        {
            Touch touch = Input.GetTouch(i);
            int fid = touch.fingerId;

            if (_uiFingers.Contains(fid)) continue;

            if (touch.phase == UnityEngine.TouchPhase.Ended || touch.phase == UnityEngine.TouchPhase.Canceled)
            {
                if (fid == _activeLookTouchId)
                {
                    _activeLookTouchId = -1;
                    _isDraggingLook = false;
                }
                continue;
            }

            if (touch.phase == UnityEngine.TouchPhase.Began && _activeLookTouchId == -1)
            {
                _activeLookTouchId = fid;
                _isDraggingLook = true;
                _lastTouchPosition = touch.position;
            }
            else if (_activeLookTouchId == fid && _isDraggingLook &&
                     (touch.phase == UnityEngine.TouchPhase.Moved || touch.phase == UnityEngine.TouchPhase.Stationary))
            {
                Vector2 delta = touch.position - _lastTouchPosition;
                _lastTouchPosition = touch.position;
                
                // Здесь мы считаем обе оси для свайпа
                _currentLookInput.x = delta.x * lookSensitivity * 0.012f;
                _currentLookInput.y = delta.y * lookSensitivity * 0.012f;
            }
        }

        if (_activeLookTouchId != -1 && !TouchExists(_activeLookTouchId))
        {
            _activeLookTouchId = -1;
            _isDraggingLook = false;
            _currentLookInput = Vector2.zero;
        }

        if (Input.touchCount == 0)
        {
            Vector2 delta = Vector2.zero;

#if ENABLE_INPUT_SYSTEM
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse != null)
                delta = mouse.delta.ReadValue();
#endif

            if (delta == Vector2.zero)
            {
                try
                {
                    delta = new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"));
                }
                catch
                {
                    delta = Vector2.zero;
                }
            }

            if (delta.sqrMagnitude > 0.01f)
            {
                _mouseLookActive = true;
                _currentLookInput.x = delta.x * lookSensitivity * 0.012f;
                _currentLookInput.y = delta.y * lookSensitivity * 0.012f;
            }
            else
            {
                _mouseLookActive = false;
                _currentLookInput = Vector2.zero;
            }
        }
    }

    private static bool TouchExists(int fingerId)
    {
        for (int i = 0; i < Input.touchCount; i++)
        {
            Touch t = Input.GetTouch(i);
            if (t.fingerId == fingerId && t.phase != UnityEngine.TouchPhase.Ended && t.phase != UnityEngine.TouchPhase.Canceled)
                return true;
        }
        return false;
    }

    private void UpdateJoystickFingers()
    {
        for (int i = 0; i < Input.touchCount; i++)
        {
            Touch t = Input.GetTouch(i);
            if (t.phase == UnityEngine.TouchPhase.Ended || t.phase == UnityEngine.TouchPhase.Canceled)
            {
                _uiFingers.Remove(t.fingerId);
                _moveJoystickFingers.Remove(t.fingerId);
                _lookJoystickFingers.Remove(t.fingerId);
            }
        }

        if (Input.touchCount == 0)
        {
            _uiFingers.Clear();
            _moveJoystickFingers.Clear();
            _lookJoystickFingers.Clear();
            return;
        }

        bool moveJoyActiveThisFrame = _lastMoveJoystickActivityFrame >= 0 && (Time.frameCount - _lastMoveJoystickActivityFrame) <= 1;

        for (int i = 0; i < Input.touchCount; i++)
        {
            Touch t = Input.GetTouch(i);
            int fid = t.fingerId;

            if (t.phase != UnityEngine.TouchPhase.Began) continue;
            if (_uiFingers.Contains(fid)) continue;

            bool overMoveJoy = IsInsideJoystickRect(t.position, moveJoystickRect, moveJoystickBufferRadius);
            bool overLookJoy = IsInsideJoystickRect(t.position, cameraJoystickRect, cameraJoystickBufferRadius);
            bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(fid);

            if (overMoveJoy || overLookJoy || overUI || (Input.touchCount == 1 && moveJoyActiveThisFrame))
            {
                _uiFingers.Add(fid);
                if (overMoveJoy || (Input.touchCount == 1 && moveJoyActiveThisFrame)) _moveJoystickFingers.Add(fid);
                if (overLookJoy) _lookJoystickFingers.Add(fid);
            }
        }
    }

    private static bool IsInsideJoystickRect(Vector2 screenPos, RectTransform rt, float buffer)
    {
        if (rt == null) return false;

        RectTransform hitRect = rt;
        var joystick = rt.GetComponent<UIVirtualJoystick>();
        if (joystick != null && joystick.containerRect != null) hitRect = joystick.containerRect;

        Canvas canvas = hitRect.GetComponentInParent<Canvas>();
        Camera cam = null;
        if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            cam = canvas.worldCamera != null ? canvas.worldCamera : Camera.main;

        if (RectTransformUtility.RectangleContainsScreenPoint(hitRect, screenPos, cam)) return true;

        Vector3[] corners = new Vector3[4];
        hitRect.GetWorldCorners(corners);
        Vector3 worldCenter = (corners[0] + corners[2]) * 0.5f;
        Vector2 screenCenter = RectTransformUtility.WorldToScreenPoint(cam, worldCenter);

        return Vector2.Distance(screenPos, screenCenter) <= buffer;
    }

    private void ApplyCharacterLook(Vector2 look)
    {
        if (starterAssetsInputs != null)
            starterAssetsInputs.LookInput(look);

        var character = NetworkClient.localPlayer != null
            ? NetworkClient.localPlayer.GetComponent<InfimaGames.LowPolyShooterPack.Character>()
            : starterAssetsInputs != null
                ? starterAssetsInputs.GetComponent<InfimaGames.LowPolyShooterPack.Character>()
                : FindObjectOfType<InfimaGames.LowPolyShooterPack.Character>(true);

        if (character != null)
        {
            if (_axisLookField == null)
                _axisLookField = typeof(InfimaGames.LowPolyShooterPack.Character).GetField("axisLook", BindingFlags.Instance | BindingFlags.NonPublic);

            _axisLookField?.SetValue(character, look);
        }
    }
}