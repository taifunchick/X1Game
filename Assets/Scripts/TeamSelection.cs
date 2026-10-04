using UnityEngine;

/// <summary>
/// Выбор команды, сделанный в MainMenu. Переживает смену сцены (статик),
/// поэтому Lasertag-сцена может сразу применить команду к заспавненному игроку.
/// У каждого клиента свой процесс — свой выбор.
/// </summary>
public static class TeamSelection
{
    public const string Red = "Red";
    public const string Blue = "Blue";

    /// <summary>Пусто — команда не выбрана (прямой запуск сцены из редактора).</summary>
    public static string SelectedTeam { get; private set; } = "";

    public static bool HasSelection => !string.IsNullOrEmpty(SelectedTeam);

    public static void Select(string teamName)
    {
        SelectedTeam = teamName == Blue ? Blue : Red;
        Debug.Log($"TeamSelection: выбрана команда {SelectedTeam}.");
    }

    public static void Clear()
    {
        SelectedTeam = "";
    }
}
