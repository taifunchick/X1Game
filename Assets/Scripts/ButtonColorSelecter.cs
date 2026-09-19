using UnityEngine;
using Mirror;

public class ButtonColorSelecter : NetworkBehaviour
{
    [SerializeField] private Color _color;
    [SerializeField] private GameObject _player;
    [SerializeField] private GameObject _colorSelecter;

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

        var colorChanger = _player.GetComponent<ColorChanger>();
        if (colorChanger == null)
        {
            Debug.LogWarning("ButtonColorSelecter: ColorChanger component missing on local player (цвет персонажа не сменится, команда назначится).");
        }
        else
        {
            colorChanger.SetColorByClient(_color);
        }

        // Красный/синий цвет кнопки = команда. В новом счёте (попадания, без убийств)
        // команда нужна только чтобы считать общую статистику Red/Blue.
        string teamName = _color.b > _color.r ? "Blue" : "Red";
        var combat = _player.GetComponent<NetworkCombatPlayer>();
        if (combat == null)
        {
            Debug.LogWarning("ButtonColorSelecter: NetworkCombatPlayer component missing on local player — команда не назначена.");
        }
        else
        {
            combat.CmdSetTeam(teamName);
            Debug.Log($"ButtonColorSelecter: local player выбрал команду {teamName}.");
        }

        if (_colorSelecter != null)
            _colorSelecter.SetActive(false);
    }
}
