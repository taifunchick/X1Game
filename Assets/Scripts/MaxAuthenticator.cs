using UnityEngine;

public class MaxAuthenticators : MonoBehaviour
{
    // Делаем скрипт синглтоном, чтобы к нему было легко обращаться из других скриптов
    public static MaxAuthenticators Instance;

    [Header("Данные игрока")]
    public string playerName = "Guest";

    private void Awake()
    {
        // Проверяем, нет ли уже такого объекта на сцене (чтобы они не плодились)
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject); // Запрещаем удалять объект при загрузке новых сцен
        }
        else
        {
            Destroy(gameObject);
        }
    }

    // Этот метод вызовется АВТОМАТИЧЕСКИ из JavaScript (со страницы Django)
    public void SetPlayerName(string nameFromWeb)
    {
        playerName = nameFromWeb;
        Debug.Log("Получено имя из браузера: " + playerName);
    }
}