using UnityEngine;
using TMPro;
using Mirror;

/// Лазертаг: счёт попаданий (убийств больше нет).
/// Каждый раз, когда игрок попадает в другого игрока, стрелку записывают +1.
///
/// Панель в углу экрана показывает те же числа, что и главный HUD (Red/Blue):
/// сумма попаданий игроков каждой команды. Источник данных — NetworkCombatPlayer.hits,
/// поэтому оба счетчика никогда не расходятся.
public class LasertagScoreManager : NetworkBehaviour
{
    [SyncVar(hook = nameof(OnScoreChanged))]
    public int redTeamScore = 0;

    [SyncVar(hook = nameof(OnScoreChanged))]
    public int blueTeamScore = 0;

    [SerializeField] private TextMeshProUGUI _redScoreText;
    [SerializeField] private TextMeshProUGUI _blueScoreText;

    public static LasertagScoreManager Instance { get; private set; }

    private int _shownRed = -1;
    private int _shownBlue = -1;

    public override void OnStartClient()
    {
        base.OnStartClient();
        Instance = this;
        CreateScoreTextIfNeeded();
        Refresh();
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        Instance = this;
    }

    private void OnScoreChanged(int oldValue, int newValue)
    {
        Refresh();
    }

    void Update()
    {
        Refresh();
    }

    /// Считает живые суммы попаданий по командам и обновляет текст, если он изменился.
    private void Refresh()
    {
        int r = 0, b = 0;
        NetworkCombatPlayer[] players = FindObjectsOfType<NetworkCombatPlayer>();
        foreach (var p in players)
        {
            if (p == null) continue;
            if (p.team == "Red") r += p.hits;
            else if (p.team == "Blue") b += p.hits;
        }

        if (r == _shownRed && b == _shownBlue) return;
        _shownRed = r;
        _shownBlue = b;

        if (_redScoreText != null)
            _redScoreText.text = $"Red: {r}";
        if (_blueScoreText != null)
            _blueScoreText.text = $"Blue: {b}";
    }

    /// Легаси-метод (использовала старая сценка Lasertag.cs). Основной счёт
    /// ведут NetworkCombatPlayer.hits, так что сюда заходить не нужно.
    [Server]
    public void ServerAddScore(string team)
    {
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
}
