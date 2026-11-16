# Мобильная панель в WebGL: почему не работала и как выложить так, чтобы точно заработало

Дата: 2026-11-16. Контекст: WebGL-клиент на dev.x1team.ru (NewBuild2.*), выделенный сервер на sv.x1team.ru.

## Диагноз по логу с телефона

В консоли **отсутствовали** все строки от мобильной логики:

- `[MobileMultiTouchSetup] Configuration complete …` — нет;
- `[MobileDetect] …` / `MobileDetect.jslib включён в сборку` — нет;
- видимость мобильного UI решают **два** места, и оба ходили в jslib с одинаковой ошибкой.

Корневая причина: у трёх `.jslib` файлы были «самописные» `.meta` **без секции `PluginImporter`** и без `WebGL: enabled: 1`
(`Assets/Plugins/WebGL/MobileDetect.jslib.meta`, `X1WebGL.jslib.meta`, `Assets/Plugins/WebGLBrowserUrl.jslib.meta`).
Для `.cs`/`.prefab` минимальный `.meta` — норма, а вот плагины (`.jslib`) без `PluginImporter` в WebGL-сборку **не линкуются**.
В результате:

- `IsMobileBrowser()` не существует → `DllImport` падает → старый код молча возвращал `false`;
- `UICanvasControllerInput.Update()` **каждый кадр** выключал `mobileRoot` (панель джойстиков) по этому же `false`;
- заодно мёртвыми были `X1WebGL_*` (захват мыши в SafeCursor) и `GetURLFromBrowser()` (MaxAuthenticator).

Дополнительно: `Application.isMobilePlatform` в WebGL всегда `false`, а в режиме «Версия для ПК» Android Chrome
userAgent тоже десктопный — поэтому полагаться только на userAgent нельзя.

## Что исправлено в репозитории

1. `.jslib.meta` для `MobileDetect`, `X1WebGL`, `WebGLBrowserUrl` переписаны в полный формат (`PluginImporter`, `WebGL: enabled: 1`) — GUID'ы сохранены.
2. Новый единый детектор `Assets/Scripts/X1DeviceDetect.cs`:
   `jslib IsMobileBrowser()` → `Input.touchSupported` → `InputSystem.Touchscreen.current`, результат кэшируется,
   любой сработавший слой включает мобильный UI. Работает даже если jslib снова не слинкуется.
3. Оба места, решающие видимость панели (`MobileMultiTouchSetup`, `UICanvasControllerInput`), переведены на `X1DeviceDetect.IsMobileDevice()`.
4. В `.jslib` добавлены пробы «включён в сборку» в консоль + подробный лог решения `[MobileDetect] ua=… => 0|1`.
5. Регрессионные тесты: `python3 -m unittest discover -s tests -v` — каждый `.jslib` обязан иметь WebGL-`PluginImporter` в `.meta`.

## Что нужно сделать при выкладке (обязательный чеклист)

1. **Пересобрать WebGL** в Unity (Build Settings → WebGL → Build), выложить **все** файлы `Build/` (`loader.js`, `framework.js`, `.wasm`, `.data`) и `index.html`. Старые файлы + новые `.data` = гарантированный «как раньше».
2. **Имена файлов.** Если останутся `NewBuild2.*`, браузер и UnityCache вправе отдать кэш старого `framework.js` — и фикса не будет видно. Выберите одно:
   - переименовать сборку (`NewBuild3.*`) и обновить `<script>`/пути в странице; **или**
   - добавить `?v=20261116` ко всем URL сборки на странице; **или**
   - на телефоне: Chrome → настройки сайта dev.x1team.ru → «Удалить данные» (UnityCache живёт в Cache Storage и не чистится обычным F5).
   - Проверка: открыть игру в **приватной вкладке** — там кэш пуст, это честный тест.
3. **Выделенный сервер (sv.x1team.ru) пересобирать НЕ нужно** — для этой правки. Сцену и весь UI клиент грузит из **своей** WebGL-сборки; сервер лишь сообщает имя сцены (`Lasertag`). Сервер трогать только если менялась сетевая логика/карта/Build Settings; при желании пересобрать можно без вреда.
4. Проверка в консоли телефона после выкладки (по этим строкам видно всё):
   - `[MobileDetect] MobileDetect.jslib включён в сборку` — jslib слинкован (если строки нет — сборка не та или .meta снова сломались);
   - `[MobileDetect] ua="…" … => 1` — что именно решил jslib;
   - `[X1DeviceDetect] Итог: мобильное=True (jslib=True, jsДоступен=True, touchSupported=True, touchscreen=True)`;
   - `[MobileMultiTouchSetup] Configuration complete. mobileUI=True …`;
   - панель джойстиков видна, левый/правый джойстик водят, свайп по экрану крутит камеру.
5. Проверить и **«Версию для ПК»** в Chrome на телефоне — теперь панель появится и там (фолбэки по тачу, не по userAgent).

## Осознанный сайд-эффект

На ПК с тач-экраном мобильная панель тоже может показаться (`touchSupported=True`). Это сознательная цена за «точно на телефоне».
Если на десктопе панель не нужна — в `X1DeviceDetect.IsWebGLMobileBrowser()` убрать фолбэки и доверять только jslib.

## Частые грабли при выкладке

- Залили только `.data`/`.wasm`, забыв `framework.js` → игра старая, фикса нет.
- Проверка «в том же телефоне без очистки данных» → кэш, а не реальность.
- Unity при импорте **не должен** переписывать `.jslib.meta` — если после открытия проекта diff по `*.jslib.meta` появился, проверьте, что WebGL по-прежнему `enabled: 1` (лог тестов `test_every_jslib_meta_enables_webgl` это ловит).
