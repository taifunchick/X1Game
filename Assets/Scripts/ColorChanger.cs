using UnityEngine;
using Mirror;

/// <summary>
/// Красит капсулу игрока в цвет команды (красный/синий).
/// Висит на дочернем объекте Capsule префаба P_LPSP_FP_CH (Renderer подхватится сам,
/// если не назначен в инспекторе). Цвет хранится на сервере и синхронизируется всем клиентам.
/// </summary>
public class ColorChanger : NetworkBehaviour
{
    [SerializeField] private Renderer _rend;

    [SyncVar(hook = nameof(OnColorChanged))]
    [SerializeField] private Color _color = Color.white;

    private void Awake()
    {
        if (_rend == null)
            _rend = GetComponent<Renderer>();
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        // Пока команда не выбрана — капсула белая у всех.
        _color = Color.white;
        ApplyColor(_color);
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        ApplyColor(_color);
    }

    /// <summary>Вызывает клиент (кнопка выбора команды). Сервер разошлёт цвет всем.</summary>
    public void SetColorByClient(Color color)
    {
        CmdSetColor(color);
    }

    /// <summary>Покрасить по имени команды: "Red" — красный, "Blue" — синий.</summary>
    public void SetTeamColorByClient(string teamName)
    {
        CmdSetColor(teamName == "Blue" ? Color.blue : Color.red);
    }

    [Command]
    private void CmdSetColor(Color color)
    {
        SetColor(color);
    }

    /// <summary>Меняет цвет на сервере (вызывать только на сервере).</summary>
    [Server]
    public void SetColor(Color color)
    {
        color.a = 1f; // всегда непрозрачный
        _color = color; // SyncVar-hook сам применит цвет на хосте и клиентах
        ApplyColor(_color);
    }

    private void OnColorChanged(Color oldValue, Color newValue)
    {
        ApplyColor(newValue);
    }

    private void ApplyColor(Color color)
    {
        if (_rend == null)
            _rend = GetComponent<Renderer>();
        if (_rend == null)
            return;

        color.a = 1f;
        _rend.material.color = color;
    }
}
