using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;
using System.Collections;

/// <summary>
/// Синхронизированный таймер и менеджер раунда лазертага.
///
/// Основные задачи:
/// 1. На выделенном сервере (VPS) таймер не тикает впустую, пока на сервере нет игроков.
/// 2. По окончании раунда НЕ вызывается деструктивный ServerChangeScene (который в Mirror
///    ломает соединения WebGL, оставляет сервер в сцене меню MainMenu или портит conn.identity).
/// 3. Раунд сбрасывается плавно: блокируется стрельба, показывается панель окончания матча на 5 сек.,
///    обнуляются попадания (hits), игроки телепортируются на спавны, таймер запускается заново.
/// </summary>
public class NetworkRoundManager : NetworkBehaviour
{
    public static NetworkRoundManager Instance { get; private set; }

    [SerializeField] private float roundLength = 600f;
    [SerializeField] private TMP_Text timerText;
    [SerializeField] private GameObject matchFinishedPanel;
    [SerializeField] private string mainMenuScene = "MainMenu";

    [SyncVar] public double endTime;
    [SyncVar(hook = nameof(OnFinishedChanged))] public bool finished;

    /// Последняя секунда, записанная в текст таймера. -1 — текст ещё не заполняли.
    private int _shownSeconds = -1;

    private void Awake()
    {
        Instance = this;
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        finished = false;
        endTime = NetworkTime.time + roundLength;
    }

    private void OnFinishedChanged(bool oldValue, bool newValue)
    {
        if (matchFinishedPanel != null)
            matchFinishedPanel.SetActive(newValue);
    }

    private void Update()
    {
        // На выделенном сервере нет ни текста таймера, ни панели итогов: headless-сборка ничего
        // не рисует. Раньше этот Update выполнял string.Format и дёргал TMP каждый кадр —
        // на сервере это чистый мусор в managed-куче (и лишняя работа GC) без всякого смысла.
        bool headless = Mirror.Utils.IsHeadless();

        if (!headless && matchFinishedPanel != null && matchFinishedPanel.activeSelf != finished)
            matchFinishedPanel.SetActive(finished);

        if (finished)
            return;

        // На выделенном сервере, пока нет ни одного активного игрока — держим таймер полным
        if (isServer && NetworkServer.connections.Count == 0)
        {
            endTime = NetworkTime.time + roundLength;
        }

        int seconds = Mathf.Max(0, Mathf.CeilToInt((float)(endTime - NetworkTime.time)));

        // Текст обновляем только когда изменилась отображаемая секунда, а не каждый кадр:
        // string.Format + перестроение меша TMP на 60 FPS — заметная нагрузка и на клиенте WebGL.
        if (!headless && timerText != null && seconds != _shownSeconds)
        {
            _shownSeconds = seconds;
            timerText.text = string.Format("{0}:{1:00}", seconds / 60, seconds % 60);
        }

        if (isServer && seconds <= 0)
        {
            finished = true;
            StartCoroutine(RestartRound());
        }
    }

    [Server]
    private IEnumerator RestartRound()
    {
        Debug.Log("[NetworkRoundManager] Раунд окончен! Подведение итогов (5 сек.)...");
        yield return new WaitForSeconds(5f);

        // Если это локальный хост в редакторе (хост захотел выйти в меню)
        if (NetworkClient.active && !Mirror.Utils.IsHeadless())
        {
            // Для хоста можно остановить игру и вернуться в меню
            NetworkManager.singleton.StopHost();
            yield break;
        }

        // Выделенный сервер: перезапускаем новый раунд на месте без перезагрузки сцены
        Debug.Log("[NetworkRoundManager] Старт нового раунда: сброс очков и телепортация игроков.");

        var players = NetworkCombatPlayer.Instances;
        for (int i = 0; i < players.Count; i++)
        {
            var p = players[i];
            if (p == null) continue;

            p.hits = 0;

            // Возвращаем игрока на точку спавна
            Transform spawnPoint = NetworkManager.singleton != null ? NetworkManager.singleton.GetStartPosition() : null;
            if (spawnPoint != null)
            {
                CharacterController cc = p.GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false;
                p.transform.position = spawnPoint.position;
                p.transform.rotation = spawnPoint.rotation;
                if (cc != null) cc.enabled = true;
            }
        }

        endTime = NetworkTime.time + roundLength;
        finished = false;
        _shownSeconds = -1;
        Debug.Log("[NetworkRoundManager] Новый раунд запущен.");
    }
}

