using UnityEngine;
using TMPro;
using Mirror;

public class LasertagScoreManager : NetworkBehaviour
{
    [SyncVar(hook = nameof(OnScoreChanged))]
    public int redTeamScore = 0;

    [SyncVar(hook = nameof(OnScoreChanged))]
    public int blueTeamScore = 0;

    [SerializeField] private TextMeshProUGUI _redScoreText;
    [SerializeField] private TextMeshProUGUI _blueScoreText;

    public static LasertagScoreManager Instance { get; private set; }

    public override void OnStartClient()
    {
        base.OnStartClient();
        Instance = this;
        Debug.Log("LasertagScoreManager: OnStartClient called");
        CreateScoreTextIfNeeded();
        UpdateScoreUI();
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        Instance = this;
        Debug.Log("LasertagScoreManager: OnStartServer called");
    }

    private void OnScoreChanged(int oldValue, int newValue)
    {
        Debug.Log($"LasertagScoreManager: OnScoreChanged {oldValue} -> {newValue}");
        UpdateScoreUI();
    }

    [ClientRpc]
    public void RpcUpdateScore(int red, int blue)
    {
        Debug.Log($"LasertagScoreManager: RpcUpdateScore called red={red}, blue={blue}");
        redTeamScore = red;
        blueTeamScore = blue;
        UpdateScoreUI();
    }

    [Server]
    public void ServerAddScore(string team)
    {
        Debug.Log($"LasertagScoreManager: ServerAddScore called for team={team}");
        if (team == "Red")
        {
            redTeamScore++;
        }
        else if (team == "Blue")
        {
            blueTeamScore++;
        }
        else
        {
            Debug.LogWarning($"LasertagScoreManager: invalid team '{team}' passed to ServerAddScore.");
        }

        UpdateScoreUI();
        RpcUpdateScore(redTeamScore, blueTeamScore);
    }

    private void CreateScoreTextIfNeeded()
    {
        if (_redScoreText != null && _blueScoreText != null) return;

        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null) return;

        if (_redScoreText == null)
        {
            GameObject go = new GameObject("RedScoreText");
            go.transform.SetParent(canvas.transform, false);
            _redScoreText = go.AddComponent<TextMeshProUGUI>();
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(10, -10);
            rt.sizeDelta = new Vector2(100, 50);
        }

        if (_blueScoreText == null)
        {
            GameObject go = new GameObject("BlueScoreText");
            go.transform.SetParent(canvas.transform, false);
            _blueScoreText = go.AddComponent<TextMeshProUGUI>();
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(10, -60);
            rt.sizeDelta = new Vector2(100, 50);
        }
    }

    private void UpdateScoreUI()
    {
        if (_redScoreText != null)
        {
            _redScoreText.text = $"Red: {redTeamScore}";
        }

        if (_blueScoreText != null)
        {
            _blueScoreText.text = $"Blue: {blueTeamScore}";
        }
    }
}
