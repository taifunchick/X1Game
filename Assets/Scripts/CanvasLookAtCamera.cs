using UnityEngine;

/// <summary>
/// Заставляет объект (World Space Canvas) всегда смотреть в сторону камеры.
/// Используется для никнейма игрока — текст всегда читается.
/// </summary>
public class CanvasLookAtCamera : MonoBehaviour
{
    [Header("Settings")]
    [Tooltip("Если true — смотрит точно на камеру (точка). Если false — просто поворот камеры (шире угол обзора).")]
    [SerializeField] private bool lookAtCameraPosition = false;

    [Header("Timing")]
    [Tooltip("0 = каждый кадр (плавнее, дороже). >0 = интервал в секундах (дешевле).")]
    [SerializeField] private float updateInterval = 0f;

    private float _timer;
    private Transform _cameraTransform;

    private void Awake()
    {
        _cameraTransform = Camera.main?.transform;
    }

    private void Update()
    {
        if (_cameraTransform == null)
            return;

        if (updateInterval > 0f)
        {
            _timer += Time.deltaTime;
            if (_timer < updateInterval)
                return;
            _timer = 0f;
        }

        if (lookAtCameraPosition)
        {
            // Точный поворот к камере — текст всегда по центру
            transform.LookAt(_cameraTransform);
        }
        else
        {
            // Копируем поворот камеры — текст лежит «перед» камерой,
            // что даёт чуть более широкий угол обзора для разных FOV
            transform.rotation = _cameraTransform.rotation;
        }
    }
}
