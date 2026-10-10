#!/usr/bin/env bash
# =============================================================================
#  x1-diagnose.sh — быстрая диагностика выделенного сервера Unity.
#
#  Запуск:   sudo bash Deploy/scripts/x1-diagnose.sh
#  Или:      sudo /usr/local/bin/x1-diagnose.sh
#
#  Скрипт отвечает на один вопрос: «сервер упал, завис или работает?» — и сразу
#  показывает улики. Ничего не перезапускает и ничего не меняет.
# =============================================================================

set -u

SERVICE="${X1_SERVICE:-unity-server}"
WATCHDOG_SERVICE="${X1_WATCHDOG_SERVICE:-x1-unity-watchdog}"
HEALTH_URL="${X1_HEALTH_URL:-http://127.0.0.1:8084/health}"
GAME_PORT="${X1_GAME_PORT:-27777}"
HEARTBEAT_FILE="${X1_HEARTBEAT_FILE:-/home/bazarov/game/server.heartbeat}"

hr()  { printf '\n\033[1m=== %s ===\033[0m\n' "$1"; }
ok()  { printf '  [\033[32m OK \033[0m] %s\n' "$1"; }
bad() { printf '  [\033[31m ПЛОХО \033[0m] %s\n' "$1"; }
warn(){ printf '  [\033[33m ВНИМАНИЕ \033[0m] %s\n' "$1"; }
run() { printf '\n\033[2m$ %s\033[0m\n' "$1"; eval "$1" 2>&1 | sed 's/^/  /'; }

hr "1. Сервисы systemd"
for s in "$SERVICE" "$WATCHDOG_SERVICE"; do
    if systemctl list-unit-files --no-legend 2>/dev/null | grep -q "^${s}\.service"; then
        state=$(systemctl is-active "$s" 2>/dev/null)
        enabled=$(systemctl is-enabled "$s" 2>/dev/null)
        if [ "$state" = "active" ]; then ok "$s: active (enabled=$enabled)"; else bad "$s: $state (enabled=$enabled)"; fi
    else
        warn "$s: unit-файл не найден"
    fi
done

hr "2. Статус игрового сервиса"
run "systemctl status $SERVICE --no-pager -l | head -25"

hr "3. Процесс и его ресурсы"
run "ps -eo pid,ppid,user,pcpu,pmem,rss,etime,stat,cmd | grep -i '[L]inuxBuild' || echo 'процесс LinuxBuild НЕ найден'"
run "cat /proc/loadavg"
run "free -m"
run "df -h /home /var/log / 2>/dev/null | head -10"

# Интерпретация: RSS растёт без предела — утечка; %CPU ~100 на одном ядре долгое
# время — главный поток не успевает (см. ограничение FPS: -fps в unity-server.service).

hr "4. Порты"
run "ss -ltnp 2>/dev/null | grep -E ':($GAME_PORT|8084|443)\b' || echo 'ни один из ожидаемых портов не слушается'"
if timeout 3 bash -c "exec 3<>/dev/tcp/127.0.0.1/$GAME_PORT" 2>/dev/null; then
    ok "игровой порт $GAME_PORT принимает TCP-соединения"
    exec 3<&- 2>/dev/null || true
else
    bad "игровой порт $GAME_PORT НЕ принимает соединения"
fi

hr "5. Жив ли ГЛАВНЫЙ ПОТОК Unity (это главное)"
code="000"
body=""
if command -v curl >/dev/null 2>&1; then
    code=$(curl -s -o /tmp/x1diag-health.json -w '%{http_code}' --max-time 5 "$HEALTH_URL" 2>/dev/null || echo 000)
    body=$(cat /tmp/x1diag-health.json 2>/dev/null)
fi
echo "  GET $HEALTH_URL -> HTTP $code"
[ -n "$body" ] && echo "  $body"

if [ "$code" = "200" ]; then
    ok "Unity отвечает: главный поток жив"
elif [ "$code" = "503" ]; then
    bad "Unity отвечает 503: процесс жив, но ГЛАВНЫЙ ПОТОК ЗАВИС. Перезапуск: sudo systemctl restart $SERVICE"
elif [ "$code" = "000" ]; then
    warn "$HEALTH_URL недоступен. Проверьте heartbeat-файл ниже: если он устарел — главный поток завис."
fi

if [ -f "$HEARTBEAT_FILE" ]; then
    age=$(( $(date +%s) - $(stat -c %Y "$HEARTBEAT_FILE" 2>/dev/null || date +%s) ))
    echo "  heartbeat-файл: $HEARTBEAT_FILE, возраст ${age} с, содержимое: $(cat "$HEARTBEAT_FILE" 2>/dev/null | tr -d '\n')"
    if [ "$age" -le 15 ]; then ok "главный поток обновлялся ${age} с назад"; 
    elif [ "$age" -le 60 ]; then warn "главный поток не обновлялся ${age} с";
    else bad "главный поток НЕ обновлялся ${age} с — сервер завис. Перезапуск: sudo systemctl restart $SERVICE"; fi
else
    warn "heartbeat-файл не найден ($HEARTBEAT_FILE). Проверьте аргумент -heartbeat в unity-server.service."
fi

if [ -f "${HEARTBEAT_FILE}.stalled" ]; then
    bad "Есть маркер зависания от внутреннего сторожа:"
    sed 's/^/    /' "${HEARTBEAT_FILE}.stalled"
fi

hr "6. Что сервер писал в лог (последние 60 строк)"
run "journalctl -u $SERVICE -n 60 --no-pager"

hr "7. Строки диагностики X1ServerWatchdog (FPS, память, игроки)"
run "journalctl -u $SERVICE --since '-2 hours' --no-pager | grep -E 'X1ServerWatchdog|X1NetworkManager' | tail -30"
echo "  Как читать: fps= должен держаться около значения -fps из unit-файла."
echo "  Если fps << 30 — серверу не хватает CPU (или снова снято ограничение кадров)."
echo "  Если managedMemMB непрерывно растёт — утечка памяти."
echo "  Если worstFrameMs в тысячи — главный поток чем-то блокируется."

hr "8. Ошибки, исключения и OOM"
run "journalctl -u $SERVICE --since '-2 hours' --no-pager | grep -iE 'error|exception|fatal|abort|segfault' | tail -30"
run "journalctl -k --since '-6 hours' --no-pager | grep -iE 'killed process|out of memory|oom' | tail -10"
run "dmesg 2>/dev/null | grep -iE 'killed process|out of memory|oom' | tail -10"

hr "9. Перезапуски сервиса (сколько раз и когда)"
run "journalctl -u $SERVICE --since '-24 hours' --no-pager | grep -E 'Started|Stopped|Main process exited|Scheduled restart|Failed' | tail -30"

hr "10. Кто подключён к игровому порту"
run "ss -tnp state established 2>/dev/null | grep ':${GAME_PORT}' | head -20"

hr "11. Nginx"
run "nginx -t 2>&1"
run "tail -n 20 /var/log/nginx/x1game.error.log 2>/dev/null || tail -n 20 /var/log/nginx/error.log 2>/dev/null"

hr "12. Проверка снаружи (WebSocket-рукопожатие через nginx)"
if command -v curl >/dev/null 2>&1; then
    run "curl -sS -o /dev/null -D - --max-time 8 -H 'Connection: Upgrade' -H 'Upgrade: websocket' -H 'Sec-WebSocket-Version: 13' -H 'Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==' https://sv.x1team.ru/ | head -12"
    echo "  Ожидается: HTTP/1.1 101 Switching Protocols."
    echo "  502 Bad Gateway   -> Unity не слушает порт 27777."
    echo "  504 Gateway Timeout -> Unity не принял соединение (завис или порт закрыт файрволом)."
    echo "  400/404           -> nginx не проксировал Upgrade (проверьте proxy_set_header)."
else
    warn "curl не установлен: sudo apt-get install -y curl"
fi

hr "Что делать по итогу"
cat <<'EOF'
  Сервис не active, в журнале «Start request repeated too quickly»
      -> sudo systemctl reset-failed unity-server && sudo systemctl start unity-server
         (в новом unit-файле стоит StartLimitIntervalSec=0, и так больше не повторится)

  Сервис active, но /health отдаёт 503 или heartbeat устарел
      -> главный поток Unity завис: sudo systemctl restart unity-server
         Затем посмотрите раздел 7: что было с fps и worstFrameMs перед зависанием.

  В журнале «Killed process … out of memory»
      -> не хватает RAM. Включите MemoryMax в unity-server.service и поднимите swap,
         либо уменьшите число одновременных игроков (maxConnections в NetworkManager).

  Всё зелёное, но игроки не заходят в сцену LaserTag
      -> проблема не в сервере: сравнивайте версии клиентского WebGL-билда и серверного
         (сцены в Build Settings, onlineScene), смотрите раздел 12.
EOF
