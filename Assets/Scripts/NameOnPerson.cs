using UnityEngine;
using Mirror;
using TMPro;

public class NameOnPerson : NetworkBehaviour
{
    [SyncVar(hook = nameof(OnNameChanged))] private string _name = "Player";

    [SerializeField] private TextMeshProUGUI _nameText;

    public override void OnStartClient()
    {
        base.OnStartClient();
        UpdateNameText(_name);
    }

    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();
        if (_nameText != null && _nameText.transform.parent != null)
            _nameText.transform.parent.gameObject.SetActive(false);
    }

    public void SetName(string newName)
    {
        if (isLocalPlayer && isClient)
        {
            CmdSetName(newName);
            return;
        }

        _name = NormalizeName(newName);
        UpdateNameText(_name);
    }

    [Command]
    public void CmdSetName(string newName)
    {
        _name = NormalizeName(newName);
    }

    private void OnNameChanged(string oldName, string newName)
    {
        UpdateNameText(newName);
    }

    private void Start()
    {
        if (!isLocalPlayer)
            return;

        string playerName = PlayerPrefs.GetString("name", "Player");
        if (MaxAuthenticators.Instance != null && !string.IsNullOrWhiteSpace(MaxAuthenticators.Instance.playerName))
            playerName = MaxAuthenticators.Instance.playerName;

        CmdSetName(playerName);
    }

    private static string NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Player";

        return name.Trim();
    }

    private void UpdateNameText(string name)
    {
        if (_nameText == null)
            return;

        _nameText.text = NormalizeName(name);
    }
}


