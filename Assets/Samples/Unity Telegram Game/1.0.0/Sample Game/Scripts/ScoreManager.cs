//using Asynkrone.UnityTelegramGame.Networking;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class ScoreManager : MonoBehaviour
{
    [SerializeField] private Text scoreText;
   // [SerializeField] private ConnexionManager telegramConnexionManager;

    [SerializeField] private int score;

    /// Последнее значение, записанное в текст.
    private int _shownScore = int.MinValue;

    private void Update()
    {
        // Без проверки здесь получается NullReferenceException КАЖДЫЙ КАДР, если ссылка
        // не назначена. На выделенном сервере поток исключений пишется в лог синхронно
        // и сам по себе способен остановить главный поток.
        if (scoreText == null)
            return;

        // Текст обновляем только когда число действительно изменилось.
        if (score == _shownScore)
            return;

        _shownScore = score;
        scoreText.text = score.ToString();
    }

    public void OnClickAddToScore()
    {
        score++;
    }
    public void OnClickShareScore()
    {
     //   telegramConnexionManager.SendScore(score);
    }
}
