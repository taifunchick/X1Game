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
        _player.GetComponent<ColorChanger>().SetColorByClient(_color);
        _colorSelecter.SetActive(false);
    }
}
