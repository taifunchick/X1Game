using UnityEngine;
using Mirror;
using TMPro;

public class NameOnPerson : NetworkBehaviour
{
    [SyncVar(hook = nameof(OnNameChanged))] private string _name = "Player";

    [SerializeField] private TextMeshProUGUI _nameText;
    [SerializeField] private bool billboardToCamera = true;

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

    private void LateUpdate()
    {
        if (!billboardToCamera || _nameText == null)
            return;

        Camera cam = GetActiveCamera();
        if (cam == null)
            return;

        Transform billboardRoot = _nameText.transform.parent != null ? _nameText.transform.parent : transform;
        Vector3 directionToCamera = cam.transform.position - billboardRoot.position;
        if (directionToCamera.sqrMagnitude < 0.001f)
            return;

        billboardRoot.rotation = Quaternion.LookRotation(directionToCamera, Vector3.up);
    }

    private static Camera GetActiveCamera()
    {
        if (Camera.main != null && Camera.main.gameObject.activeInHierarchy)
            return Camera.main;

        foreach (Camera cam in Camera.allCameras)
        {
            if (cam != null && cam.enabled && cam.gameObject.activeInHierarchy)
                return cam;
        }

        return null;
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


