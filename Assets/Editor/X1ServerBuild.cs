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

            if (!ValidateMirrorHeadlessFrameRate())
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

        /// <summary>
        /// Проверка ограничения частоты кадров headless-сервера.
        ///
        /// Это не паранойя, а конкретная история: из копии Mirror этого проекта была удалена
        /// строка <c>Application.targetFrameRate = sendRate;</c> в
        /// <c>NetworkManager.ConfigureHeadlessFrameRate()</c> — единственное отличие от
        /// апстрима v96.0.1 во всех сетевых файлах. Без неё выделенный сервер крутит главный
        /// цикл со скоростью тысячи кадров в секунду: съедает ядро CPU, тонет в сборках мусора
        /// Mono и на втором игроке перестаёт обрабатывать сеть. Процесс при этом остаётся живым,
        /// поэтому systemd его не перезапускает.
        ///
        /// Сейчас ограничение продублировано в <c>X1NetworkManager.ConfigureHeadlessFrameRate()</c>,
        /// поэтому потеря строки в Mirror не смертельна. Но если исчезнут ОБА места — сборку
        /// лучше не выпускать вовсе: на стенде это выглядит как «сервер просто виснет».
        /// </summary>
        private static bool ValidateMirrorHeadlessFrameRate()
        {
            bool inMirror = SourceContains(
                Path.Combine(Application.dataPath, "Mirror/Core/NetworkManager.cs"),
                "Application.targetFrameRate");

            bool inX1Manager = SourceContains(
                Path.Combine(Application.dataPath, "Scripts/X1NetworkManager.cs"),
                "Application.targetFrameRate")
                && SourceContains(
                Path.Combine(Application.dataPath, "Scripts/X1NetworkManager.cs"),
                "ConfigureHeadlessFrameRate");

            if (inMirror && inX1Manager)
                return true;

            if (!inMirror && !inX1Manager)
            {
                Debug.LogError("[X1ServerBuild] НИГДЕ не задаётся Application.targetFrameRate для headless-сервера. " +
                               "Такой сервер крутит главный цикл без ограничения FPS, съедает всё CPU ядро и " +
                               "зависает на втором игроке (процесс остаётся живым, systemd его не перезапускает). " +
                               "Верните строку 'Application.targetFrameRate = sendRate;' в " +
                               "Mirror/Core/NetworkManager.cs:ConfigureHeadlessFrameRate() или в " +
                               "X1NetworkManager.ConfigureHeadlessFrameRate().");
                return false;
            }

            if (!inMirror)
                Debug.LogWarning("[X1ServerBuild] В Mirror/Core/NetworkManager.cs нет Application.targetFrameRate — " +
                                 "похоже, локальная копия Mirror снова разошлась с апстримом. Ограничение частоты кадров " +
                                 "держится только на X1NetworkManager.ConfigureHeadlessFrameRate(). Работать будет, " +
                                 "но строку в Mirror лучше восстановить.");

            if (!inX1Manager)
                Debug.LogWarning("[X1ServerBuild] В X1NetworkManager нет переопределения ConfigureHeadlessFrameRate(). " +
                                 "Ограничение частоты кадров держится только на Mirror — при обновлении библиотеки оно " +
                                 "может снова потеряться.");

            return true;
        }

        private static bool SourceContains(string path, string needle)
        {
            try
            {
                if (!File.Exists(path))
                {
                    Debug.LogWarning($"[X1ServerBuild] Файл для проверки не найден: {path}");
                    return false;
                }

                return File.ReadAllText(path).IndexOf(needle, StringComparison.Ordinal) >= 0;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[X1ServerBuild] Не удалось проверить {path}: {e.Message}");
                return false;
            }
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
            Log("   sudo systemctl stop unity-server");
            Log($"   ./{ExecutableName}.x86_64 -batchmode -nographics -logFile - \\");
            Log($"       -scene {GameSceneName} -port 27777 -fps 60 -watchdog 45 -stats 30");
            Log("");
            Log("   -batchmode -nographics обязательны: без них Mirror.Utils.IsHeadless() может");
            Log("   вернуть false, и сервер не стартует автоматически и не пришлёт клиенту сцену.");
            Log("   -logFile - направляет лог в stdout, то есть в journald. Правильное имя");
            Log("   аргумента — именно -logFile: с -logfile часть сборок Unity лог не пишет.");
            Log($"   -scene {GameSceneName} необязателен (подставится defaultServerScene),");
            Log("   но полезен как явное указание.");
            Log("");
            Log("   -fps 60 — КРИТИЧНО. Ограничивает частоту кадров headless-сервера. Без него");
            Log("             главный цикл крутит тысячи кадров в секунду: сервер съедает ядро CPU,");
            Log("             тонет в сборках мусора Mono и на ВТОРОМ игроке перестаёт считать");
            Log("             попадания и пускать в сцену, оставаясь «живым» для systemd.");
            Log("   -watchdog 45 — если главный поток замолчит на 45 с, процесс завершит сам себя,");
            Log("                  и systemd (Restart=always) поднимет его заново.");
            Log("   -stats 30    — раз в 30 с писать в лог строку диагностики (fps, память, игроки).");
            Log("   -verbose     — не глушить Debug.Log (только для отладки).");
            Log("");
            Log("3. В бою сервер должен работать через systemd, а не вручную:");
            Log("   Deploy/systemd/unity-server.service      — исправленный unit игрового сервера");
            Log("   Deploy/systemd/x1-unity-watchdog.service — внешний сторож (страховка)");
            Log("   Deploy/scripts/x1-diagnose.sh            — диагностика одной командой");
            Log("   Deploy/nginx/sv.x1team.ru.conf           — WebSocket-прокси");
            Log("   Deploy/README.md                         — что было сломано и как развёртывать");
            Log("");
            Log("4. Проверить в логе сервера, что он загрузил игровую сцену:");
            Log("   [X1NetworkManager] Headless-сервер: Application.targetFrameRate = 60 Гц (sendRate = ...)");
            Log($"   [X1NetworkManager] Выделенный сервер: сцена '.../{GameSceneName}.unity', порт ...");
            Log($"   [X1NetworkManager] Сервер загрузил сцену '.../{GameSceneName}.unity'. Ожидаем игроков.");
            Log("   [X1ServerWatchdog] запущен. stallTimeout=45 c ...");
            Log("");
            Log("5. Проверить, что живой не только процесс, но и главный поток:");
            Log("   curl -s http://127.0.0.1:8084/health");
            Log("");
            Log("6. Если wss:// не работает — проблема в реверс-прокси (nginx) перед портом Unity,");
            Log("   а не в клиенте: nginx должен проксировать WebSocket на порт из настроек.");
            Log("");
            Log("7. ВАЖНО: клиентский WebGL-билд нужно пересобирать и выкладывать ОДНОВРЕМЕННО");
            Log("   с серверным — настройки транспорта живут в сцене MainMenu и должны совпадать.");
        }

        private static void Log(string message) => Debug.Log($"[X1ServerBuild] {message}");
    }
}
#endif