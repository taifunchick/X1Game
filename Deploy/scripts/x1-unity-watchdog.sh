#!/usr/bin/env bash
# =============================================================================
#  x1-unity-watchdog.sh — внешний сторож выделенного сервера Unity.
#
#  Зачем он нужен, если сторож уже есть внутри Unity (X1ServerWatchdog).
#  Внутренний сторож лечит 99% случаев: если главный поток Unity заклинит, поток
#  сторожа завершит процесс, и systemd поднимет его заново. Но есть ситуации, когда
#  процесс не может помочь себе сам: deadlock внутри Mono, состояние D (непрерывный
#  ввод-вывод), исчерпание потоков, убийство потока сторожа. Тогда нужен кто-то СНАРУЖИ.
#
#  Скрипт различает три состояния, и это главное его отличие от простого «пинга порта»:
#    * порт открыт, но /health отвечает 503 или heartbeat устарел
#      -> процесс ЖИВ, но главный поток ЗАВИС. systemctl status при этом покажет
#         «active (running)», и Restart=on-failure никогда не сработает.
#         Именно так выглядела ваша проблема: «сервис висит и не перезапускается».
#    * порт закрыт, а сервис «active» -> Unity не поднял транспорт.
#    * сервис не active -> перезапуск.
#
#  Установка:
#     sudo cp Deploy/scripts/x1-unity-watchdog.sh /usr/local/bin/x1-unity-watchdog.sh
#     sudo chmod +x /usr/local/bin/x1-unity-watchdog.sh
#     sudo cp Deploy/systemd/x1-unity-watchdog.service /etc/systemd/system/
#     sudo systemctl daemon-reload
#     sudo systemctl enable --now x1-unity-watchdog
#     sudo journalctl -u x1-unity-watchdog -f
# =============================================================================

set -u

# --- Настройки (переопределяются переменными окружения из unit-файла) ---------
SERVICE="${X1_SERVICE:-unity-server}"
HEALTH_URL="${X1_HEALTH_URL:-http://127.0.0.1:8084/health}"
GAME_HOST="${X1_GAME_HOST:-127.0.0.1}"
GAME_PORT="${X1_GAME_PORT:-27777}"
HEARTBEAT_FILE="${X1_HEARTBEAT_FILE:-/home/bazarov/game/server.heartbeat}"

CHECK_INTERVAL="${X1_CHECK_INTERVAL:-10}"     # как часто проверять, сек
FAIL_LIMIT="${X1_FAIL_LIMIT:-3}"              # сколько провалов подряд до перезапуска
HEARTBEAT_MAX_AGE="${X1_HEARTBEAT_MAX_AGE:-60}"  # сек: старше — главный поток не жив
PROBE_TIMEOUT="${X1_PROBE_TIMEOUT:-5}"        # таймаут одного запроса, сек
RESTART_COOLDOWN="${X1_RESTART_COOLDOWN:-120}"   # сек: не чаще одного перезапуска
GRACE_AFTER_RESTART="${X1_GRACE_AFTER_RESTART:-90}" # сек: дать серверу загрузить сцену

fail_count=0
last_restart=0
last_state=""

log() {
    # timestamp + сообщение; работает под systemd, поэтому уходит в journald
    printf '%s %s\n' "$(date '+%Y-%m-%d %H:%M:%S')" "$*"
}

have() { command -v "$1" >/dev/null 2>&1; }

service_active() {
    systemctl is-active --quiet "$SERVICE" 2>/dev/null
}

# Проверка, что игровой порт принимает TCP-соединения.
# Используется bash-встроенное /dev/tcp, чтобы не зависеть от nc/telnet.
#
# ВАЖНО понимать, что этот тест доказывает и чего не доказывает: рукопожатие TCP
# завершает ЯДРО (соединение попадает в очередь accept), поэтому порт «открыт»
# даже тогда, когда главный поток Unity завис и никто accept не вызывает. Ровно
# поэтому решение о перезапуске принимается по /health и heartbeat-файлу, а порт —
# лишь дополнительный признак.
game_port_open() {
    timeout "$PROBE_TIMEOUT" bash -c "exec 3<>/dev/tcp/${GAME_HOST}/${GAME_PORT}" 2>/dev/null
}

# Запрос /health. Возвращает HTTP-код (000 — не достучались).
http_code() {
    if have curl; then
        curl -s -o /tmp/x1-health.$$.json -w '%{http_code}' \
             --max-time "$PROBE_TIMEOUT" "$HEALTH_URL" 2>/dev/null || echo "000"
    elif have wget; then
        if wget -q -T "$PROBE_TIMEOUT" -O /tmp/x1-health.$$.json "$HEALTH_URL" 2>/dev/null; then
            echo "200"
        else
            echo "000"
        fi
    else
        echo "000"
    fi
}

heartbeat_age() {
    [ -f "$HEARTBEAT_FILE" ] || { echo "-1"; return; }
    local now mtime
    now=$(date +%s)
    mtime=$(stat -c %Y "$HEARTBEAT_FILE" 2>/dev/null || echo "$now")
    echo $(( now - mtime ))
}

do_restart() {
    local reason="$1"
    local now
    now=$(date +%s)

    if [ $(( now - last_restart )) -lt "$RESTART_COOLDOWN" ]; then
        log "ПЕРЕЗАПУСК ПРОПУЩЕН (cooldown ${RESTART_COOLDOWN}с): $reason"
        return 1
    fi

    log "!!! ПЕРЕЗАПУСК $SERVICE. Причина: $reason"

    # Собрать улики ДО перезапуска: именно по ним видно, что случилось.
    {
        echo "----- systemctl status -----"
        systemctl status "$SERVICE" --no-pager -l 2>&1 | head -40
        echo "----- последние 60 строк лога -----"
        journalctl -u "$SERVICE" -n 60 --no-pager 2>&1
        echo "----- память -----"
        free -m 2>&1
        echo "----- диск -----"
        df -h /home 2>&1
        echo "----- load -----"
        cat /proc/loadavg 2>&1
        echo "----- убитые OOM (последние) -----"
        journalctl -k --since "-30 min" --no-pager 2>&1 | grep -iE "killed process|out of memory" | tail -10
        echo "----- маркер зависания от X1ServerWatchdog -----"
        cat "${HEARTBEAT_FILE}.stalled" 2>/dev/null || echo "(нет)"
    } | while IFS= read -r line; do log "DIAG| $line"; done

    systemctl restart "$SERVICE" 2>&1 | while IFS= read -r line; do log "RESTART| $line"; done

    last_restart=$(date +%s)
    fail_count=0

    # Серверу нужно время на загрузку сцены: не проверяем его, пока он стартует.
    log "Жду ${GRACE_AFTER_RESTART}с, пока $SERVICE поднимется и загрузит сцену..."
    sleep "$GRACE_AFTER_RESTART"
    return 0
}

# --- Предварительные проверки -------------------------------------------------
if ! have systemctl; then
    log "FATAL: systemctl не найден. Скрипт рассчитан на systemd."
    exit 1
fi

if ! have curl && ! have wget; then
    log "ВНИМАНИЕ: нет ни curl, ни wget — проверка /health работать не будет, "
    log "останутся только heartbeat-файл и игровой порт. Установите: sudo apt-get install -y curl"
fi

log "=== x1-unity-watchdog запущен ==="
log "service=$SERVICE health=$HEALTH_URL game=${GAME_HOST}:${GAME_PORT}"
log "heartbeat=$HEARTBEAT_FILE interval=${CHECK_INTERVAL}s failLimit=$FAIL_LIMIT heartbeatMaxAge=${HEARTBEAT_MAX_AGE}s"

# --- Основной цикл ------------------------------------------------------------
while true; do
    state="ok"
    detail=""

    if ! service_active; then
        state="dead"
        detail="systemctl is-active: сервис не запущен"
    else
        code=$(http_code)
        age=$(heartbeat_age)

        if [ "$code" = "200" ] && { [ "$age" = "-1" ] || [ "$age" -le "$HEARTBEAT_MAX_AGE" ]; }; then
            state="ok"
            detail="health=200 heartbeat_age=${age}s"
        elif [ "$code" = "503" ]; then
            # Unity сам сообщил: главный поток не обновлялся слишком долго.
            state="stalled"
            detail="health=503 (главный поток Unity завис) heartbeat_age=${age}s"
        elif [ "$age" != "-1" ] && [ "$age" -gt "$HEARTBEAT_MAX_AGE" ]; then
            # Сервис «active», порт, возможно, даже открыт (рукопожатие WebSocket ведёт
            # отдельный поток транспорта), но главный поток не подаёт признаков жизни.
            state="stalled"
            detail="heartbeat устарел на ${age}s (>${HEARTBEAT_MAX_AGE}s), health=$code"
        elif [ "$code" = "000" ]; then
            # /health недоступен. Сам по себе это не приговор: HTTP-эндпоинт мог не подняться
            # (например, порт 8084 занят прошлым процессом) — игра при этом работает.
            if game_port_open; then
                state="degraded"
                detail="/health не отвечает, но игровой порт ${GAME_PORT} открыт"
            else
                state="stalled"
                detail="/health не отвечает И игровой порт ${GAME_PORT} закрыт"
            fi
        else
            state="degraded"
            detail="health=$code heartbeat_age=${age}s"
        fi
    fi

    # Печатаем только изменения состояния + периодический «я жив», чтобы журнал не пух.
    if [ "$state" != "$last_state" ]; then
        log "состояние: ${last_state:-<start>} -> $state ($detail)"
        last_state="$state"
    fi

    case "$state" in
        ok)
            fail_count=0
            ;;
        stalled|dead)
            fail_count=$(( fail_count + 1 ))
            log "ПРОВАЛ ${fail_count}/${FAIL_LIMIT}: $state ($detail)"
            if [ "$fail_count" -ge "$FAIL_LIMIT" ]; then
                do_restart "$state: $detail"
                last_state=""
            fi
            ;;
        degraded)
            # Намеренно НЕ перезапускаем. «Игровой порт открыт, а /health молчит» означает
            # лишь то, что не поднялся вспомогательный HTTP-эндпоинт (занят порт 8084,
            # выключен SimpleHttpServer и т.п.). Перезапускать из-за этого рабочий сервер —
            # значит устраивать игрокам обрывы на пустом месте. Зависание главного потока
            # в этой ветке всё равно ловится: heartbeat-файл проверяется отдельно,
            # и его устаревание переводит состояние в stalled.
            fail_count=0
            ;;
    esac

    sleep "$CHECK_INTERVAL"
done
