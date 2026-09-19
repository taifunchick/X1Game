using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;
using System.Collections;

/// Server-authoritative match clock. Put on a spawned scene object with a NetworkIdentity.
public class NetworkRoundManager : NetworkBehaviour
{
    public static NetworkRoundManager Instance { get; private set; }
    [SerializeField] float roundLength = 600f;
    [SerializeField] TMP_Text timerText;
    [SerializeField] GameObject matchFinishedPanel;
    [SerializeField] string mainMenuScene = "MainMenu";
    [SyncVar] public double endTime;
    [SyncVar] public bool finished;
    void Awake() => Instance = this;
    public override void OnStartServer() { endTime = NetworkTime.time + roundLength; finished = false; }
    void Update()
    {
        if (finished) { if (matchFinishedPanel) matchFinishedPanel.SetActive(true); return; }
        int seconds = Mathf.Max(0, Mathf.CeilToInt((float)(endTime - NetworkTime.time)));
        if (timerText) timerText.text = string.Format("{0}:{1:00}", seconds / 60, seconds % 60);
        if (isServer && seconds <= 0) { finished = true; StartCoroutine(EndMatch()); }
    }
    [Server] IEnumerator EndMatch()
    {
        yield return new WaitForSeconds(5f);

        if (NetworkClient.active)
        {
            // Хост: останавливаем игру — Mirror сам вернёт всех в offlineScene (меню).
            // ServerChangeScene(меню) при живом сервере приводил к тому, что игроки респавнились прямо в сцене меню.
            NetworkManager.singleton.StopHost();
            yield break;
        }

        // Выделенный сервер: в меню возвращаться некуда — перезапускаем игровую сцену (новый раунд).
        // Иначе сервер навсегда оставался в сцене меню и новые игроки не могли заспавниться.
        string currentScene = SceneManager.GetActiveScene().path;
        if (!string.IsNullOrEmpty(currentScene))
            NetworkManager.singleton.ServerChangeScene(currentScene);
        else if (!string.IsNullOrEmpty(mainMenuScene))
            NetworkManager.singleton.ServerChangeScene(mainMenuScene);
    }
}
