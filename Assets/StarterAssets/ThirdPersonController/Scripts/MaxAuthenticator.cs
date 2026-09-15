using UnityEngine;
using System.Runtime.InteropServices;
using System;

public class MaxAuthenticator : MonoBehaviour
{
    // Подключаем наш JS-плагин
    [DllImport("__Internal")]
    private static extern string GetURLFromBrowser();

    [Header("Данные игрока")]
    public string playerName = "Guest";
    public string playerToken = "";

    void Start()
    {
        // Проверяем, запущена ли игра в браузере (WebGL)
#if UNITY_WEBGL && !UNITY_EDITOR
        string fullUrl = GetURLFromBrowser();
        ExtractDataFromUrl(fullUrl);
#else
        // Заглушка для тестов прямо в редакторе Unity
        ExtractDataFromUrl("https://твойсайт.ру/игра?token=test_token_123");
#endif
    }

    private void ExtractDataFromUrl(string url)
    {
        Debug.Log("Полный URL: " + url);

        // Ищем начало параметров (знак вопроса)
        int questionMarkIndex = url.IndexOf('?');
        if (questionMarkIndex == -1)
        {
            Debug.Log("Параметров в ссылке нет. Играем за Гостя.");
            return;
        }

        string queryString = url.Substring(questionMarkIndex + 1);
        string[] parameters = queryString.Split('&');

        foreach (string param in parameters)
        {
            string[] keyValue = param.Split('=');
            if (keyValue.Length == 2)
            {
                string key = keyValue[0];
                string value = keyValue[1];

                // Если бот передает параметр token (например: ?token=dfg87d6g)
                if (key.ToLower() == "token")
                {
                    playerToken = value;
                    DecryptTokenAndSetNickname(playerToken);
                }
            }
        }
    }

    private void DecryptTokenAndSetNickname(string token)
    {
        // ВАЖНО: Сюда нужно будет вставить код расшифровки
        
        // Пока кода нет, делаем заглушку, чтобы проверить, что передача работает:
        playerName = "MaxUser_" + token.Substring(0, Mathf.Min(4, token.Length)); 
        
        Debug.Log("Успешная авторизация! Никнейм: " + playerName);
        
        // Дальше ты можешь передать этот playerName в свой скрипт Mirror (PlayerName), 
        // чтобы он отобразился над роботом.
    }
}