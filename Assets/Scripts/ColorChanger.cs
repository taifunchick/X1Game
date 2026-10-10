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

    /// Экземпляр материала этого рендерера. Кэшируется, потому что каждое обращение к
    /// renderer.material — это native-вызов (и создание копии материала при первом обращении).
    private Material _materialInstance;

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
        color.a = 1f;

        // На выделенном сервере ничего не рендерится, поэтому экземпляры материалов там не нужны
        // вовсе. Обращение к renderer.material создаёт копию материала в native-памяти, а игроков
        // за время жизни серверного процесса проходят сотни — на headless просто не трогаем рендерер.
        if (Mirror.Utils.IsHeadless())
            return;

        if (_rend == null)
            _rend = GetComponent<Renderer>();
        if (_rend == null)
            return;

        // Кэшируем экземпляр материала: повторные обращения к .material — это лишний native-вызов
        // на каждую смену цвета.
        if (_materialInstance == null)
            _materialInstance = _rend.material;

        _materialInstance.color = color;
    }
}
