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
            Debug.LogWarning("ButtonColorSelecter: local player not found when setting color.");
            return;
        }

        var colorChanger = _player.GetComponent<ColorChanger>();
        if (colorChanger == null)
        {
            Debug.LogWarning("ButtonColorSelecter: ColorChanger component missing on local player.");
        }
        else
        {
            colorChanger.SetColorByClient(_color);
        }

        string teamName = _color.b > _color.r ? "Blue" : "Red";
        var lasertag = _player.GetComponent<Lasertag>();
        if (lasertag == null)
        {
            Debug.LogWarning("ButtonColorSelecter: Lasertag component missing on local player.");
        }
        else
        {
            lasertag.CmdSetTeam(teamName);
        }

        if (_colorSelecter != null)
            _colorSelecter.SetActive(false);
    }
}
