using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;

public class UIVirtualJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    [System.Serializable]
    public class Event : UnityEvent<Vector2> { }
    
    [Header("Rect References")]
    public RectTransform containerRect;
    public RectTransform handleRect;

    [Header("Settings")]
    public float joystickRange = 50f;
    [Tooltip("Основной коэффициент чувствительности джойстика.")]
    public float magnitudeMultiplier = 1f;
    [Tooltip("Дополнительный коэффициент чувствительности для этого конкретного джойстика.")]
    public float inputSensitivity = 1f;
    [Tooltip("Если включено — джойстик принимает только горизонтальный ввод.")]
    public bool horizontalOnly = true;
    public bool invertXOutputValue;
    public bool invertYOutputValue;

    [Header("Output")]
    public Event joystickOutputEvent;

    // ---------------------------------------------------------------------
    // Публичное состояние (для MultiTouchMobileInput и др.)
    // ---------------------------------------------------------------------

    /// <summary>Джойстик сейчас перетаскивается пальцем/мышью.</summary>
    public bool IsDragging { get; private set; }

    /// <summary>PointerId пальца, который сейчас держит джойстик (-1 если нет).</summary>
    public int ActivePointerId { get; private set; } = -1;

    void Start()
    {
        SetupHandle();
    }

    private void SetupHandle()
    {
        if (handleRect)
            UpdateHandleRectPosition(Vector2.zero);
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        IsDragging = true;
        ActivePointerId = eventData.pointerId;
        OnDrag(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        // Если пришёл чужой палец (не тот, что начал drag) — игнорируем.
        if (IsDragging && ActivePointerId != -1 && ActivePointerId != eventData.pointerId)
            return;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(containerRect, eventData.position, eventData.pressEventCamera, out Vector2 position);
        
        position = ApplySizeDelta(position);
        
        if (horizontalOnly)
            position.y = 0f;

        Vector2 clampedPosition = ClampValuesToMagnitude(position);
        Vector2 outputPosition = ApplyInversionFilter(position);

        if (horizontalOnly)
        {
            outputPosition.y = 0f;
            clampedPosition.y = 0f;
        }

        OutputPointerEventValue(outputPosition * magnitudeMultiplier * inputSensitivity);

        if (handleRect)
            UpdateHandleRectPosition(clampedPosition * joystickRange);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (ActivePointerId == eventData.pointerId || ActivePointerId == -1)
        {
            IsDragging = false;
            ActivePointerId = -1;
            OutputPointerEventValue(Vector2.zero);
            if (handleRect)
                UpdateHandleRectPosition(Vector2.zero);
        }
    }

    private void OutputPointerEventValue(Vector2 pointerPosition)
    {
        joystickOutputEvent.Invoke(pointerPosition);
    }

    private void UpdateHandleRectPosition(Vector2 newPosition)
    {
        if (handleRect) handleRect.anchoredPosition = newPosition;
    }

    Vector2 ApplySizeDelta(Vector2 position)
    {
        float x = (position.x / containerRect.sizeDelta.x) * 2.5f;
        float y = (position.y / containerRect.sizeDelta.y) * 2.5f;
        return new Vector2(x, y);
    }

    Vector2 ClampValuesToMagnitude(Vector2 position)
    {
        return Vector2.ClampMagnitude(position, 1);
    }

    Vector2 ApplyInversionFilter(Vector2 position)
    {
        if (invertXOutputValue) position.x = InvertValue(position.x);
        if (invertYOutputValue) position.y = InvertValue(position.y);
        return position;
    }

    float InvertValue(float value) => -value;
}