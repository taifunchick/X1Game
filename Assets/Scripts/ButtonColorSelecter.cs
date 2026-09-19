using UnityEngine;
using Mirror;

/// <summary>
/// Кнопка выбора команды (красная/синяя). Висит на кнопках Color1/Color2 в сценах.
/// Красит Capsule локального игрока через ColorChanger, назначает команду
/// (NetworkCombatPlayer в лазертаге, Lasertag в футболе) и прячет панель выбора.
/// Пока команда не выбрана — курсор виден и управление взглядом заморожено,
/// после выбора курсор лочится обратно для игры.
/// </summary>
public class ButtonColorSelecter : NetworkBehaviour
{
    [SerializeField] private Color _color;
    [SerializeField] private GameObject _player;
    [SerializeField] private GameObject _colorSelecter;

    private bool _teamChosen;

    private void Start()
    {
        // Сцена только загрузилась, команда ещё не выбрана — показываем курсор.
        ShowCursor();
    }

    private void Update()
    {
        // Пока панель выбора открыта — курсор всегда виден.
        // Это перекрывает Character.Awake, который лочит курсор при спавне игрока.
        if (!_teamChosen && _colorSelecter != null && _colorSelecter.activeInHierarchy)
            ShowCursor();
    }

    private GameObject GetLocalPlayer()
    {
        if (NetworkClient.localPlayer != null)
        {
            return NetworkClient.localPlayer.gameObject;
        }
        return null;
    }

    public void SetColor()
    {
        _player = GetLocalPlayer();
        if (_player == null)
        {
            Debug.LogWarning("ButtonColorSelecter: local player not found when setting team.");
            return;
        }

        // Красный/синий цвет кнопки = команда. Красим чистым красным/синим.
        string teamName = _color.b > _color.r ? "Blue" : "Red";
        Color teamColor = teamName == "Blue" ? Color.blue : Color.red;

        // ColorChanger висит на дочерней Capsule (P_LPSP_FP_CH) либо на корне (PlayerArmature).
        var colorChanger = _player.GetComponentInChildren<ColorChanger>();
        if (colorChanger == null)
        {
            Debug.LogWarning("ButtonColorSelecter: ColorChanger component missing on local player (цвет персонажа не сменится, команда назначится).");
        }
        else
        {
            colorChanger.SetColorByClient(teamColor);
        }

        // Команда нужна счёту, чтобы считать общую статистику Red/Blue.
        var combat = _player.GetComponent<NetworkCombatPlayer>();
        if (combat != null)
            combat.CmdSetTeam(teamName);

        // Футбол (PlayerArmature) хранит команду в старом компоненте Lasertag.
        var lasertag = _player.GetComponent<Lasertag>();
        if (lasertag != null)
            lasertag.CmdSetTeam(teamName);

        if (combat == null && lasertag == null)
        {
            Debug.LogWarning("ButtonColorSelecter: ни NetworkCombatPlayer, ни Lasertag не найдены на локальном игроке — команда не назначена.");
        }
        else
        {
            Debug.Log($"ButtonColorSelecter: local player выбрал команду {teamName}.");
        }

        _teamChosen = true;
        if (_colorSelecter != null)
            _colorSelecter.SetActive(false);

        HideCursor();
    }

    private void ShowCursor()
    {
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
        SetCharacterCursorLocked(false);
        SetStarterLookEnabled(false);
    }

    private void HideCursor()
    {
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;
        SetCharacterCursorLocked(true);
        SetStarterLookEnabled(true);
    }

    // Лазертаг (P_LPSP_FP_CH): у Character свой флаг cursorLocked, который гейтит
    // ввод движения/взгляда — просто показать курсор Unity недостаточно.
    private void SetCharacterCursorLocked(bool locked)
    {
        GameObject player = _player != null ? _player : GetLocalPlayer();
        if (player == null)
            return;

        var character = player.GetComponent<InfimaGames.LowPolyShooterPack.Character>();
        if (character == null)
            return;

        if (locked)
            character.LockCursor();
        else
            character.UnlockCursor();
    }

    // Футбол (PlayerArmature): замораживаем взгляд, чтобы камера не крутилась,
    // пока игрок ведёт мышь к кнопкам выбора команды.
    private void SetStarterLookEnabled(bool enabled)
    {
        GameObject player = _player != null ? _player : GetLocalPlayer();
        if (player == null)
            return;

        var inputs = player.GetComponent<StarterAssets.StarterAssetsInputs>();
        if (inputs != null)
            inputs.cursorInputForLook = enabled;
    }
}
