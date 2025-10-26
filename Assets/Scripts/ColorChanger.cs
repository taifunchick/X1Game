
using UnityEngine;
using Mirror;

public class ColorChanger : NetworkBehaviour   // colour sync
{
    [SerializeField] private Renderer _rend;
    [SyncVar] [SerializeField] private Color _color;

    private void Start()
    {
        if (isServer) _color = Color.white;
    }
    public override void OnStartClient()                   // _rend.material.color
    {
        base.OnStartClient();
        SetRender();
    }

    public void SetColor(Color color)
    {
        _color = color;
        RpcSetRender(color);
    }

    [ClientRpc]
    private void RpcSetRender(Color color)
    {
        _rend.material.color = color;
    }

    private void SetRender()
    {
        _rend.material.color = _color;
    }

    [Command]
    private void CmdSetColor(Color color)
    {
        SetColor(color);
    }

    public void SetColorByClient(Color color)
    {
        CmdSetColor(color);
    }
}
