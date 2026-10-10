# X1TEAM — выделенный сервер Unity: что было сломано и как развёртывать теперь

Этот документ заменяет раздел «Настройки Unity сервера» старой инструкции и добавляет
то, чего в ней не было: самовосстановление, диагностику и разбор причин.

Файлы для развёртывания лежат рядом:

```
Deploy/
├── README.md                            <- этот документ
├── systemd/
│   ├── unity-server.service             <- игровой сервер (исправленный unit)
│   └── x1-unity-watchdog.service        <- внешний сторож (страховка)
├── scripts/
│   ├── x1-unity-watchdog.sh             <- скрипт внешнего сторожа
│   └── x1-diagnose.sh                   <- диагностика одной командой
├── nginx/
│   └── sv.x1team.ru.conf                <- WebSocket-прокси (исправленный)
└── logrotate/
    └── unity-server                     <- ротация лога (только для варианта «лог в файл»)
```

---

## 1. Главная причина падения сервера

**В копии Mirror, которая лежит в проекте, из `NetworkManager.ConfigureHeadlessFrameRate()`
была удалена одна строка.**

Так это выглядит в апстриме Mirror v96.0.1 (версия из `Assets/Mirror/Version.txt`):

```csharp
public virtual void ConfigureHeadlessFrameRate()
{
    if (Utils.IsHeadless())
    {
        Application.targetFrameRate = sendRate;      // <-- ЭТОЙ СТРОКИ НЕ БЫЛО
        // Debug.Log($"Server Tick Rate set to {Application.targetFrameRate} Hz.");
    }
}
```

Так это выглядело в вашем проекте — метод стал пустым:

```csharp
public virtual void ConfigureHeadlessFrameRate()
{
    if (Utils.IsHeadless())
    {
        // Debug.Log($"Server Tick Rate set to {Application.targetFrameRate} Hz.");
    }
}
```

Я сверил ваш Mirror с апстримом v96.0.1 по всем ключевым сетевым файлам
(`NetworkManager.cs`, `NetworkServer.cs`, `NetworkClient.cs`, `NetworkIdentity.cs`,
`NetworkConnectionToClient.cs`, `NetworkTime.cs`, весь `SimpleWebTransport`).
**Это единственное отличие.** Остальные файлы совпадают байт в байт (с точностью до
переносов строк). То есть библиотека не «устарела» — из неё потеряли ровно одну строку.

### Почему от этого сервер умирает именно на втором игроке

На headless-сервере нет вертикальной синхронизации, а `Application.targetFrameRate`
остался равным `-1` («не ограничивать»). Значит главный цикл Unity крутится **столько
раз в секунду, сколько может выжать CPU** — на практике это тысячи кадров в секунду.

Каждый такой кадр на сервере выполняются все `Update`/`LateUpdate` сцены и префаба игрока:

| Что крутится каждый кадр | Стоимость |
|---|---|
| `NetworkRoundManager.Update` → `string.Format` + запись в TMP-текст | аллокация строки **каждый кадр** |
| `LasertagScoreManager.Update` → обход игроков + 2 записи в TMP | аллокации |
| `NetworkKillScoreUI.Update` → обход игроков + 3 записи в TMP | аллокации |
| `ButtonColorSelecter.Update` ×2 | поиск локального игрока |
| Infima `WallAvoidance.Update` → `Physics.SphereCast` | физический запрос |
| Infima `Interactor.Update` → `Physics.SphereCast` | физический запрос |
| Infima `MotionApplier` ×2 + `SwayMotion`/`RecoilMotion`/`JumpMotion`/`LandMotion`/`LeaningMotion`/`OffsetMotion`/`LoweredMotion` | пружины, интерполяция |
| `ColorChanger` → обращение к `renderer.material` | native-вызов, копия материала |
| Mirror: снимки `NetworkTransform`, сериализация SyncVar, отправка | на каждого игрока |

При тысячах кадров в секунду это даёт:

1. **100% одного ядра CPU** постоянно. На VPS, где рядом живут nginx и Gunicorn с
   тремя воркерами, свободных ядер просто не остаётся.
2. **Непрерывные сборки мусора Mono.** `string.Format` и обходы коллекций мусорят на
   каждом кадре; при 3000 кадров/с managed-куча переполняется десятки раз в секунду.
   GC в Mono останавливает все потоки. Отсюда, скорее всего, и три файла
   `mono_crash.mem.*.blob` в корне проекта.
3. **Каждый `Debug.Log` пишется синхронно.** Логируются попадания, выбор команды,
   подключения — при тысячах тиков это уже не «лог», а узкое место.

Дальше всё сходится с вашими наблюдениями:

- **Один игрок** — сервер ещё успевает, всё работает.
- **Второй игрок** — объём работы на кадр удваивается, ядро загружено на 100%, кадр не
  успевает обработать сеть. Mirror перестаёт рассылать SyncVar →
  *«сервер перестаёт засчитывать попадания»*.
- **Процесс при этом ЖИВ.** `systemctl status` показывает `active (running)`, поэтому
  `Restart=on-failure` не срабатывает → *«падает и не перезапускается заново»*.
- **Перезагрузка страницы не пускает в сцену LaserTag**, потому что рукопожатие
  WebSocket ведёт отдельный поток транспорта (`WebSocketServer.acceptLoop` /
  `HandshakeAndReceiveLoop`), и оно успешно завершается даже у полностью зависшего
  сервера. А вот `SceneMessage` отправляет уже главный поток — до него очередь не доходит.
  Клиент подключается, висит 15 секунд и отваливается. Именно так это и выглядело.
- **Через 6 часов сервер «сам ожил»** — процесс в итоге добил либо Mono (crash), либо
  OOM-killer ядра; systemd поднял его заново, и без игроков он снова работал нормально
  ровно до второго подключения.

### Что сделано

1. Строка `Application.targetFrameRate = sendRate;` **восстановлена** в
   `Assets/Mirror/Core/NetworkManager.cs`.
2. В `X1NetworkManager` добавлено **собственное переопределение**
   `ConfigureHeadlessFrameRate()`. Теперь ограничение частоты кадров не зависит от того,
   что случится с папкой Mirror при обновлении: если строку снова потеряют, сервер всё
   равно останется ограниченным. Значение берётся из поля `headlessTargetFrameRate`
   (по умолчанию 60) или из аргумента командной строки `-fps`.
3. Всё, что на сервере не нужно (TMP-тексты, обход игроков ради UI, смена цвета
   материала, панель выбора команды), **отключается в headless-режиме**.
4. Добавлен `X1ServerWatchdog` — см. раздел 3.

---

## 2. Остальные найденные и исправленные дефекты

| # | Файл | Проблема | Последствие | Исправление |
|---|---|---|---|---|
| 1 | `Mirror/Core/NetworkManager.cs` | удалена строка ограничения FPS | **главная причина** | восстановлена |
| 2 | `Scripts/SimpleHttpServer.cs` | `async void … while(true)` с пустым `catch` | если `HttpListener` начинал отдавать ошибку сразу, цикл становился бесконечной вертушкой **на главном потоке** без единого `await` — сервер зависал навсегда, оставаясь живым | приём вынесен на отдельный поток, добавлен счётчик подряд идущих ошибок с остановкой цикла и паузой между ошибками |
| 3 | `Scripts/SimpleHttpServer.cs` | Unity API (`FindObjectsOfType`, `GetComponent`, `StartCoroutine`) вызывались из обработчика HTTP | вызов Unity API не из главного потока аварийно завершает процесс | команды ставятся в очередь и выполняются в `Update` на главном потоке |
| 4 | `Scripts/SimpleHttpServer.cs` | слушатель на `http://+:8084/` и не закрывался при выгрузке сцены | эндпоинт торчал наружу; после смены сцены порт оставался занятым и новый экземпляр падал | по умолчанию `127.0.0.1`, корректный `OnDestroy` |
| 5 | `Scripts/NetworkCombatPlayer.cs` | цикл `Physics.Raycast` со сдвигом луча на `hit.distance + 0.05` | если луч стартовал внутри коллайдера, `hit.distance == 0` и цикл делал до **4000 физических запросов на один выстрел** на главном потоке сервера | один `Physics.RaycastNonAlloc` + разбор попаданий по возрастанию расстояния; стоимость выстрела постоянна |
| 6 | `Scripts/ColorChanger.cs` | `renderer.material` вызывался и на сервере | копии материалов в native-памяти там, где ничего не рендерится | в headless рендерер не трогается, экземпляр материала кэшируется |
| 7 | `Scripts/NetworkRoundManager.cs` | `string.Format` + запись в TMP каждый кадр, в том числе на сервере | мусор в managed-куче на каждый тик | в headless пропускается; на клиенте текст обновляется только при смене секунды |
| 8 | `Scripts/LasertagScoreManager.cs`, `NetworkKillScoreUI.cs`, `ButtonColorSelecter.cs` | UI-логика выполнялась на выделенном сервере | лишняя работа каждый кадр | отключается в headless |
| 9 | `ThirdPersonController/Scripts/BallSpawn.cs` | в сцене **Lasertag** сервер спавнил футбольный мяч | Rigidbody + `NetworkRigidbodyUnreliable` синхронизировались каждому клиенту каждый тик в сцене, где мяч не нужен | добавлен флаг `_spawnOnServer`, в сцене Lasertag он выключен |
| 10 | `ThirdPersonController/Scripts/PushBall.cs` | `lastPushTime` никогда не обновлялся | условие cooldown было истинным каждый `FixedUpdate`: мяч получал импульс 50 раз в секунду и `Debug.Log` на каждый | `lastPushTime` обновляется |
| 11 | `Football/Gates2.cs`, `Sample Game/Scripts/ScoreManager.cs` | обращение к TMP/Text без проверки | `NullReferenceException` **каждый кадр**, если ссылка не назначена | проверки добавлены |
| 12 | `RoboActivate.cs` | `FindObjectsOfType` + `Destroy` в `OnDestroy` | исключение при выгрузке сцены/выходе из приложения | защита от teardown |
| 13 | сцена `MainMenu.unity` | `maxHandshakeSize: 3000` | рукопожатие WebSocket через nginx (свои заголовки + куки домена `x1team.ru`) могло не влезть в 3000 байт → соединение отклонялось без внятной причины | 16384 |
| 14 | сцена `MainMenu.unity` | `receiveTimeout: 20000` | игрок, свернувший вкладку на 20 секунд, выбрасывался с сервера | 120000 |
| 15 | `unity-server.service` | `Restart=on-failure` + стандартный лимит стартов | зависший процесс не перезапускается; после 5 падений за 10 с юнит навсегда уходит в `failed` | `Restart=always` + `StartLimitIntervalSec=0` |
| 16 | `unity-server.service` | не было `-batchmode -nographics`, `-logfile` вместо `-logFile`, лог в файл без ротации | риск незаполненного диска и неявного поведения | исправлено, лог по умолчанию уходит в journald |
| 17 | `Football/Reset.cs` | `_gates` / `_gates1` использовались без проверки | `NullReferenceException` на каждый вход игрока в триггер, если ссылки не назначены | проверки и понятное предупреждение в лог |
| 18 | `Scripts/InputGestureBootstrap.cs` | объект создавался и на выделенном сервере, где каждый кадр опрашивал мышь/клавиатуру/тач | бессмысленная работа и лишний шум в логе headless-сервера | в headless не создаётся вовсе |
| 19 | `Editor/X1ServerBuild.cs` | сборщик сервера не проверял, что ограничение FPS на месте | можно было молча собрать сервер, который «просто виснет» на втором игроке | сборка падает с внятной ошибкой, если `Application.targetFrameRate` пропал И из Mirror, И из `X1NetworkManager` |
| 20 | репозиторий | три дампа Mono `mono_crash.mem.*.blob` по 10 МБ лежали в git | 30 МБ мусора в истории; сами дампы — следствие падения сервера | убраны из индекса, добавлены в `.gitignore` вместе с `*.log` и `*.heartbeat` |

---

## 3. Что добавлено нового

### `X1ServerWatchdog` (`Assets/Scripts/Server/X1ServerWatchdog.cs`)

Ставится автоматически в headless-сборке, в сцене настраивать ничего не нужно.

- **Детектор зависания.** Главный поток отмечает heartbeat каждый кадр; отдельный поток
  сверяет его раз в 0.5 с. Если главный поток молчит дольше `-watchdog` секунд (по
  умолчанию 45) — процесс завершает сам себя с кодом 2, и systemd (`Restart=always`)
  поднимает его заново. Это прямое лечение симптома «сервис висит и не перезапускается»,
  причём независимо от того, чем именно вызвано зависание.
- **Диагностика в логе.** Раз в `-stats` секунд (по умолчанию 30) пишется одна строка:

  ```
  [X1ServerWatchdog] scene=Lasertag players=2 connections=2 fps=59.8 worstFrameMs=41.3
                     uptime=00:12:33 managedMemMB=512 systemMemMB=3840 gc0=1234 gc1=12 gc2=0
                     logMsgs=88 stale=0.01s | logMsgs/window=31 logErrors/window=0 worstFrameMs/window=18.4
  ```

  (`systemMemMB` — вся ОЗУ машины: с ней сравнивают `managedMemMB`.)

  По ней видно, что именно происходит: `fps` сильно ниже заданного — не хватает CPU;
  `managedMemMB` растёт без остановки — утечка; `worstFrameMs` в тысячи — главный поток
  чем-то блокируется; `logMsgs/window` огромный — сервер тонет в собственном логе.
- **Детектор «потопа» сообщений в лог** — отдельно предупреждает, если сообщений слишком
  много, и показывает последнее из них.
- **Файл heartbeat** (`-heartbeat`) — для внешнего мониторинга.
- **Глушение Info-логов** на сервере (см. `X1Log`): предупреждения, ошибки и исключения
  пишутся всегда, а `Debug.Log` — нет. Вернуть полное логирование: аргумент `-verbose`
  или галка `verboseServerLogging` в `X1NetworkManager`.

### `X1Log` (`Assets/Scripts/X1Log.cs`)

Отключает `Debug.Log` на сервере через собственную обёртку над `ILogHandler` —
предсказуемо, в отличие от `Debug.unityLogger.filterLogType`, чья семантика неочевидна.
Плюс флаг `X1Log.InfoEnabled`, которым горячие места проверяют «а стоит ли вообще
формировать строку»: интерполяция `$"..."` выполняется ДО вызова `Debug.Log`, поэтому
фильтр внутри логгера память всё равно тратит.

### `/health` в `SimpleHttpServer`

`GET http://127.0.0.1:8084/health` отдаёт JSON:

```json
{"ok":true,"mainThreadStaleSeconds":0.012,"uptimeSeconds":753.4,"fps":59.8,
 "worstFrameMs":41.3,"frames":45120,"serverActive":true,"connections":2,
 "spawnedObjects":11,"scene":"Lasertag","managedMemoryMB":512.3}
```

Отвечает **отдельный поток**, поэтому эндпоинт работает даже у зависшего сервера:
код `503` и большое `mainThreadStaleSeconds` означают «процесс жив, главный поток стоит».
Именно это позволяет отличить зависание от падения — то, чего не может сделать
`systemctl status`.

### Внешний сторож (`x1-unity-watchdog`)

Страховка на случай, когда процесс не может помочь себе сам (deadlock в Mono, состояние
D, исчерпание потоков). Раз в 10 с проверяет `/health`, возраст heartbeat-файла и игровой
порт; после 3 провалов подряд перезапускает сервис и **перед перезапуском складывает в
журнал улики**: статус, последние 60 строк лога, память, диск, load, записи OOM-killer
и маркер зависания от внутреннего сторожа.

---

## 4. ОБЯЗАТЕЛЬНО: пересобрать оба билда

Изменены C#-код **и сцены** (`MainMenu.unity`, `Lasertag.unity`). Значит пересобирать
нужно и клиент, и сервер, и выкладывать их **одновременно**:

1. **Сервер**: Unity → `Build/X1/Собрать выделенный сервер (Linux x86_64)`
   → загрузить `LinuxBuild.x86_64`, `LinuxBuild_Data/`, `UnityPlayer.so` в
   `/home/bazarov/game/`.
2. **Клиент WebGL**: обычная сборка → загрузить папку `Build` на веб-сервер.

Если оставить старый клиент и новый сервер (или наоборот), настройки транспорта
разъедутся, и вы получите новые симптомы поверх старых.

Проверить, что Build Settings не сломаны: в нём должны быть ровно две включённые сцены —
`MainMenu` (первой) и `Lasertag`. Сборщик сервера это проверяет сам и не даст собрать
билд без них.

---

## 5. Установка на сервер

```bash
# 0. Зависимости
sudo apt-get update
sudo apt-get install -y curl

# 1. Загрузить новый билд сервера по SFTP в /home/bazarov/game/
chmod +x /home/bazarov/game/LinuxBuild.x86_64

# 2. Unit-файл игрового сервера
sudo cp Deploy/systemd/unity-server.service /etc/systemd/system/unity-server.service

# 3. Скрипты
sudo cp Deploy/scripts/x1-unity-watchdog.sh /usr/local/bin/x1-unity-watchdog.sh
sudo cp Deploy/scripts/x1-diagnose.sh       /usr/local/bin/x1-diagnose.sh
sudo chmod +x /usr/local/bin/x1-unity-watchdog.sh /usr/local/bin/x1-diagnose.sh

# 4. Внешний сторож
sudo cp Deploy/systemd/x1-unity-watchdog.service /etc/systemd/system/

# 5. Nginx. ОСТОРОЖНО: сначала найдите, где сейчас описан server_name sv.x1team.ru
sudo grep -rn "sv.x1team.ru" /etc/nginx/sites-available/ /etc/nginx/conf.d/ /etc/nginx/nginx.conf

#    5a. Если отдельного файла для sv.x1team.ru нет — создайте его:
sudo cp Deploy/nginx/sv.x1team.ru.conf /etc/nginx/sites-available/sv.x1team.ru
sudo ln -sf /etc/nginx/sites-available/sv.x1team.ru /etc/nginx/sites-enabled/sv.x1team.ru

#    5b. Если блок `server { server_name sv.x1team.ru; ... }` уже живёт внутри
#        /etc/nginx/sites-available/x1team (рядом с Django) — НЕ копируйте файл
#        целиком: получатся два server-блока с одним именем, и nginx возьмёт
#        первый попавшийся. Вместо этого перенесите из нашего конфига в существующий
#        блок только настройки проксирования (proxy_pass, Upgrade/Connection,
#        proxy_read_timeout, proxy_buffering off, X-Forwarded-For) и убедитесь,
#        что в `listen` НЕТ слова http2.

sudo nginx -t && sudo systemctl reload nginx

# 6. Запустить
sudo systemctl daemon-reload
sudo systemctl enable --now unity-server
sudo systemctl enable --now x1-unity-watchdog

# 7. Убедиться
sudo journalctl -u unity-server -f
sudo journalctl -u x1-unity-watchdog -f
curl -s http://127.0.0.1:8084/health
```

Старый `server.log` больше не нужен (лог идёт в journald). Если хотите оставить вариант
«лог в файл» — замените в unit-файле `-logFile -` на
`-logFile /home/bazarov/game/server.log` и поставьте ротацию:

```bash
sudo cp Deploy/logrotate/unity-server /etc/logrotate.d/unity-server
```

---

## 6. Аргументы командной строки сервера

Все настройки читаются без пересборки — достаточно поправить `ExecStart` и
`sudo systemctl restart unity-server`.

| Аргумент | По умолчанию | Что делает |
|---|---|---|
| `-batchmode -nographics` | — | headless-режим. Mirror без него может не определить `IsHeadless()` и не стартовать автоматически |
| `-logFile -` | — | лог в stdout, то есть в journald |
| `-scene Lasertag` | `defaultServerScene` | игровая сцена |
| `-port 27777` | порт транспорта | игровой порт (WebSocket) |
| `-fps 60` | 60 | **ограничение частоты кадров сервера.** Не убирайте: без него сервер «захлёбывается» на 2+ игроках |
| `-watchdog 45` | 45 | через сколько секунд молчания главного потока процесс убивает сам себя. `0` — выключить |
| `-stats 30` | 30 | как часто писать строку диагностики. `0` — не писать |
| `-heartbeat <путь>` | `./server.heartbeat` | файл heartbeat для внешнего мониторинга |
| `-verbose` | выкл | не глушить `Debug.Log` (нужен для отладки, в бою не оставлять) |

Подбор `-fps`: начните с 60. Если в строке диагностики `fps` заметно ниже заданного —
серверу не хватает CPU; попробуйте 30 (совпадает с `sendRate = 30` в `NetworkManager`)
и проверьте, что попадания считаются так же уверенно.

---

## 7. Диагностика

Одной командой:

```bash
sudo x1-diagnose.sh
```

Скрипт проходит 12 проверок и в конце подсказывает, что делать. Ничего не перезапускает.

Вручную — три вопроса по порядку:

```bash
# 1. Сервис вообще запущен?
sudo systemctl status unity-server --no-pager

# 2. ГЛАВНЫЙ ПОТОК жив? (это главный вопрос — systemctl его не задаёт)
curl -s http://127.0.0.1:8084/health
stat -c '%Y' /home/bazarov/game/server.heartbeat; date +%s   # разница должна быть < 15

# 3. Что сервер писал перед смертью?
sudo journalctl -u unity-server -n 200 --no-pager
sudo journalctl -u unity-server --since "-2 hours" | grep X1ServerWatchdog
```

Как читать строку диагностики:

| Показатель | Норма | Что значит отклонение |
|---|---|---|
| `fps` | около значения `-fps` | сильно ниже — не хватает CPU, или снова снято ограничение кадров |
| `worstFrameMs` | десятки мс | тысячи мс — главный поток чем-то заблокирован (тяжёлая загрузка сцены, GC, блокирующий вызов) |
| `managedMemMB` | стабилен | непрерывно растёт — утечка |
| `gc0` | растёт медленно | растёт тысячами в минуту — слишком много аллокаций на кадр |
| `logMsgs/window` | десятки | тысячи — сервер тонет в логе; включите `-verbose` только для отладки |
| `players` / `connections` | совпадают | `connections > 0`, а `players = 0` — клиенты подключились, но не заспавнились (проблема со сценой) |

Полезные команды:

```bash
sudo journalctl -u unity-server -f                      # лог в реальном времени
sudo journalctl -u unity-server --since "-1 hour"       # за час
sudo journalctl -u x1-unity-watchdog -f                 # что видит внешний сторож
sudo systemctl reset-failed unity-server                # снять «сдавшийся» юнит (больше не понадобится)
ss -ltnp | grep -E '27777|8084'                         # кто слушает порты
free -m; df -h; cat /proc/loadavg                       # ресурсы машины
sudo journalctl -k | grep -i "killed process"           # убивал ли OOM-killer
```

---

## 8. Как понять, что починилось

Запустите сервер, зайдите с двух устройств и посмотрите в журнал:

```bash
sudo journalctl -u unity-server -f | grep -E 'X1NetworkManager|X1ServerWatchdog'
```

Должно быть:

```
[X1NetworkManager] Headless-сервер: Application.targetFrameRate = 60 Гц (sendRate = 30). …
[X1ServerWatchdog] запущен. stallTimeout=45 c, статистика каждые 30 c.
[X1NetworkManager] Сервер загрузил сцену '…/Lasertag.unity'. Ожидаем игроков.
[X1NetworkManager] Клиент подключился: connId=1, address=<реальный IP игрока>
[X1NetworkManager] Игрок для connId=1 создан в сцене 'Lasertag' …
[X1ServerWatchdog] scene=Lasertag players=2 connections=2 fps=59.8 … managedMemMB=…
```

Критерии успеха:

- `fps` держится около 60 и **не падает** при подключении второго и третьего игрока;
- `address=` показывает реальные IP игроков, а не `127.0.0.1` (значит nginx передаёт
  `X-Forwarded-For` — без этого в логе невозможно разобрать, кто есть кто);
- попадания считаются у обоих игроков больше 10 минут подряд;
- `managedMemMB` не растёт монотонно;
- `curl http://127.0.0.1:8084/health` отдаёт `"ok":true`;
- за сутки в `journalctl -u unity-server` нет строк `FATAL: главный поток Unity не обновлялся`.

Проверка намеренного зависания (разово, на тестовой машине): уберите из `ExecStart`
`-fps 60`, перезапустите, зайдите вдвоём и убедитесь, что через ~45 с в журнале появится
строка `[X1ServerWatchdog] FATAL: главный поток Unity не обновлялся …`, а сервис
перезапустится сам. После этого верните `-fps 60`.

---

## 9. Если сервер всё-таки падает

Порядок действий:

1. `sudo x1-diagnose.sh` — и смотреть раздел 8 («Ошибки, исключения и OOM»).
2. Если в `journalctl -k` есть `Killed process … out of memory` — не хватает RAM.
   Раскомментируйте `MemoryHigh`/`MemoryMax` в `unity-server.service` (не больше
   70–80% от RAM машины: там же живут Gunicorn и nginx), добавьте swap, уменьшите
   `maxConnections` в `X1NetworkManager` (сейчас 100).
3. Если есть `FATAL: главный поток Unity не обновлялся` — смотрите последнюю строку
   диагностики перед ней: `worstFrameMs`, `managedMemMB`, `logMsgs`. Это и есть причина.
4. Если `/health` отвечает, `fps` в норме, а игроки всё равно не попадают в сцену —
   проблема не в сервере, а в несоответствии клиентского и серверного билдов:
   проверьте раздел 12 диагностики (`101 Switching Protocols`) и что WebGL-билд
   пересобран из того же коммита, что и серверный.
5. Для глубокой отладки запустите сервер вручную с полным логированием и посмотрите
   его вживую:

   ```bash
   sudo systemctl stop unity-server
   cd /home/bazarov/game
   ./LinuxBuild.x86_64 -batchmode -nographics -logFile - -scene Lasertag -port 27777 \
                       -fps 60 -watchdog 0 -stats 5 -verbose
   ```

   `-watchdog 0` отключает самоубийство процесса, чтобы зависание можно было
   исследовать, а не лечить. Не забудьте потом вернуть сервис:
   `sudo systemctl start unity-server`.

---

## 10. Краткая памятка по сервисам

```bash
# Игровой сервер
sudo systemctl start|stop|restart|status unity-server
sudo systemctl enable|disable unity-server
sudo journalctl -u unity-server -f

# Внешний сторож
sudo systemctl start|stop|restart|status x1-unity-watchdog
sudo journalctl -u x1-unity-watchdog -f

# Сайт (Django/Gunicorn) — без изменений
sudo systemctl restart x1team
sudo journalctl -u x1team -f

# Nginx
sudo nginx -t && sudo systemctl reload nginx
sudo tail -f /var/log/nginx/x1game.error.log
```

Обновление билда сервера:

```bash
# 1. Загрузить новый билд по SFTP в /home/bazarov/game/
chmod +x /home/bazarov/game/LinuxBuild.x86_64
sudo systemctl restart unity-server
sudo systemctl status unity-server --no-pager
curl -s http://127.0.0.1:8084/health
sudo journalctl -u unity-server -n 50 --no-pager
```
