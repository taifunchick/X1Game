#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace X1.EditorTools
{
    /// <summary>
    /// Сборка выделенного (dedicated) сервера X1Game под Linux x86_64.
    ///
    /// ВАЖНО: клиент WebGL и выделенный сервер — это ДВА РАЗНЫХ билда одного проекта.
    /// Пересборка клиента (Builds_!gitignore/Web/webGLFootball) никак не влияет на сервер,
    /// потому что на сервере крутится отдельный исполняемый файл Football_sv.x86_64,
    /// и только в нём зашиты:
    ///   - набор сцен из Build Settings (если сцены там нет — сервер её не найдёт);
    ///   - сериализованные поля X1NetworkManager из сцены меню
    ///     (headlessStartMode, offlineScene, defaultServerScene);
    ///   - серверная логика X1NetworkManager.ConfigureDedicatedServer(),
    ///     которая подставляет onlineScene и заставляет Mirror вызвать ServerChangeScene.
    ///
    /// Если сервер собран из старой версии проекта, он остаётся в сцене меню:
    /// networkSceneName == offlineScene, и Mirror (NetworkManager.OnServerAuthenticated)
    /// НЕ отправляет клиенту SceneMessage вообще. Клиент подключается по WebSocket,
    /// но висит до таймаута и пишет «Сервер не прислал игровую сцену».
    /// Лечится только пересборкой и перезапуском сервера.
    /// </summary>
    public static class X1ServerBuild
    {
        /// <summary>Имя исполняемого файла сервера (без расширения .x86_64).</summary>
        private const string ExecutableName = "LinuxBuild";

        /// <summary>Сцена, которая должна быть стартовой: в ней живёт NetworkManager.</summary>
        private const string StartupSceneName = "MainMenu";

        /// <summary>Игровая сцена, которую сервер обязан загрузить и сообщить клиентам.</summary>
        private const string GameSceneName = "Lasertag";

        private static string OutputRoot =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds_!gitignore", "Server"));

        private static string ExecutablePath =>
            Path.Combine(OutputRoot, ExecutableName + ".x86_64");

        [MenuItem("Build/X1/Собрать выделенный сервер (Linux x86_64)", false, 10)]
        private static void BuildServerMenu() => BuildServer(false);

        [MenuItem("Build/X1/Собрать выделенный сервер + архив", false, 11)]
        private static void BuildServerAndZipMenu() => BuildServer(true);

        /// <summary>
        /// Точка входа для сборки из командной строки:
        /// Unity.exe -batchmode -quit -projectPath &lt;проект&gt;
        ///          -executeMethod X1.EditorTools.X1ServerBuild.BuildFromCommandLine
        /// </summary>
        public static void BuildFromCommandLine()
        {
            bool ok = BuildServer(false);

            if (Application.isBatchMode)
                EditorApplication.Exit(ok ? 0 : 1);
        }

        private static bool BuildServer(bool makeZip)
        {
            string[] scenes = GetEnabledScenes();

            if (!ValidateScenes(scenes))
                return false;

            Log("=== Сборка выделенного сервера ===");
            Log($"Сцены ({scenes.Length}): {string.Join(", ", scenes)}");
            Log($"Целевая платформа: {BuildTarget.StandaloneLinux64} (Server subtarget)");
            Log($"Выход: {ExecutablePath}");

            StandaloneBuildSubtarget previousSubtarget = EditorUserBuildSettings.standaloneBuildSubtarget;
            bool previousStripEngineCode = PlayerSettings.stripEngineCode;

            BuildReport report = null;

            try
            {
                // Dedicated Server subtarget — это и есть «headless»: сборка без графики
                // и без аудиоустройств, которые на серверной машине не нужны.
                EditorUserBuildSettings.standaloneBuildSubtarget = StandaloneBuildSubtarget.Server;

                // Серверный билд работает на Mono (как в предыдущем Football_sv:
                // в Football_sv_Data лежит MonoBleedingEdge). IL2CPP на сервере не нужен
                // и только замедляет сборку.
                PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Server, ScriptingImplementation.Mono2x);

                // Минимальный стриппинг: Mirror использует рефлексию и post-processor,
                // агрессивный стриппинг может вырезать нужные обработчики сообщений.
                PlayerSettings.SetManagedStrippingLevel(UnityEditor.Build.NamedBuildTarget.Server, ManagedStrippingLevel.Low);
                PlayerSettings.stripEngineCode = false;

                Directory.CreateDirectory(OutputRoot);

                var options = new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = ExecutablePath,
                    target = BuildTarget.StandaloneLinux64,
                    targetGroup = BuildTargetGroup.Standalone,
                    options = BuildOptions.None
                };

                report = BuildPipeline.BuildPlayer(options);
            }
            catch (Exception e)
            {
                Debug.LogError($"[X1ServerBuild] Сборка упала с исключением: {e}");
                return false;
            }
            finally
            {
                // Возвращаем настройки, иначе следующая сборка WebGL/Windows
                // унаследует серверный subtarget.
                EditorUserBuildSettings.standaloneBuildSubtarget = previousSubtarget;
                PlayerSettings.stripEngineCode = previousStripEngineCode;
            }

            if (report == null)
                return false;

            BuildSummary summary = report.summary;

            if (summary.result != BuildResult.Succeeded)
            {
                Debug.LogError($"[X1ServerBuild] Сборка завершилась с результатом {summary.result}, " +
                               $"ошибок: {summary.totalErrors}, предупреждений: {summary.totalWarnings}");
                return false;
            }

            Log($"Сборка успешна. Размер: {summary.totalSize / (1024 * 1024)} МБ, " +
                $"время: {summary.totalTime.TotalSeconds:F0} сек.");

            if (makeZip)
                CreateZip();

            PrintDeployInstructions();
            return true;
        }

        private static string[] GetEnabledScenes()
        {
            var result = new List<string>();

            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
                if (scene.enabled && !string.IsNullOrEmpty(scene.path))
                    result.Add(scene.path);

            return result.ToArray();
        }

        /// <summary>
        /// Проверяем ровно то, из-за чего клиент не мог зайти раньше:
        /// сцены обязаны быть включены в Build Settings, иначе сервер их не найдёт
        /// ни через -scene, ни через defaultServerScene.
        /// </summary>
        private static bool ValidateScenes(string[] scenes)
        {
            bool ok = true;

            ok &= RequireScene(scenes, StartupSceneName,
                $"Стартовая сцена '{StartupSceneName}' не включена в Build Settings. " +
                "Без неё на сервере не будет NetworkManager (он лежит именно в сцене меню).");

            ok &= RequireScene(scenes, GameSceneName,
                $"Игровая сцена '{GameSceneName}' не включена в Build Settings. " +
                "Сервер запустится, но Mirror не сможет загрузить сцену и не пришлёт её клиентам " +
                "(Ready/AddPlayer будут отклоняться). Либо добавь сцену в Build Settings, " +
                "либо укажи другую в X1NetworkManager.defaultServerScene.");

            if (scenes.Length > 0 && Path.GetFileNameWithoutExtension(scenes[0]) != StartupSceneName)
                Debug.LogWarning($"[X1ServerBuild] Стартовая сцена — '{scenes[0]}', " +
                                 $"а не '{StartupSceneName}'. Сервер стартует с неё; " +
                                 "проверьте, что offlineScene в NetworkManager совпадает.");

            return ok;
        }

        private static bool RequireScene(string[] scenes, string sceneName, string error)
        {
            foreach (string scene in scenes)
            {
                if (Path.GetFileNameWithoutExtension(scene) == sceneName)
                    return true;
            }

            Debug.LogError($"[X1ServerBuild] {error}");
            return false;
        }

        private static void CreateZip()
        {
            try
            {
                string zipPath = Path.Combine(OutputRoot, "sv.zip");

                if (File.Exists(zipPath))
                    File.Delete(zipPath);

                ZipFile.CreateFromDirectory(OutputRoot, zipPath);

                Log($"Архив готов: {zipPath} ({new FileInfo(zipPath).Length / (1024 * 1024)} МБ)");
            }
            catch (Exception e)
            {
                // Архив — удобство, а не обязательный шаг. Сборка уже удалась.
                Debug.LogWarning($"[X1ServerBuild] Не удалось создать архив: {e.Message}. " +
                                 "Соберите sv.zip вручную из папки Server.");
            }
        }

        private static void PrintDeployInstructions()
        {
            string data = Path.Combine(OutputRoot, ExecutableName + "_Data");

            Log("");
            Log("=== Что делать с этим сервером ===");
            Log("1. Выложить на серверный хост содержимое папки:");
            Log($"   {ExecutablePath}");
            Log($"   {data}");
            Log($"   {Path.Combine(OutputRoot, "UnityPlayer.so")}");
            Log($"   {Path.Combine(OutputRoot, "libdecor-0.so.0")} (если есть в сборке)");
            Log("");
            Log("2. Остановить старый процесс сервера и запустить новый:");
            Log($"   ./{ExecutableName}.x86_64 -batchmode -nographics -scene {GameSceneName} -logFile /var/log/x1server.log");
            Log("");
            Log("   -batchmode обязателен: без него Mirror.Utils.IsHeadless() вернёт false,");
            Log("   сервер не стартует автоматически и не пришлёт клиенту сцену.");
            Log($"   -scene {GameSceneName} необязателен (подставится defaultServerScene),");
            Log("   но полезен как явное указание.");
            Log("");
            Log("3. Проверить в логе сервера, что он загрузил игровую сцену:");
            Log($"   [X1NetworkManager] Выделенный сервер: сцена '.../{GameSceneName}.unity', порт ...");
            Log($"   [X1NetworkManager] Сервер загрузил сцену '.../{GameSceneName}.unity'. Ожидаем игроков.");
            Log("");
            Log("4. Если wss:// не работает — проблема в реверс-прокси (nginx) перед портом Unity,");
            Log("   а не в клиенте: nginx должен проксировать WebSocket на порт из настроек.");
        }

        private static void Log(string message) => Debug.Log($"[X1ServerBuild] {message}");
    }
}
#endif