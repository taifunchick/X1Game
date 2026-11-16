mergeInto(LibraryManager.library, {

  // Текущее состояние захвата мыши (1 — захвачено, 0 — нет).
  X1WebGL_IsPointerLocked: function () {
    try {
      // Проба канала (один раз за сессию): если этой строки НЕТ в консоли
      // браузера, X1WebGL.jslib не в сборке.
      // ВАЖНО: лог обязан быть ВНУТРИ функции. console.log на верхнем уровне
      // .jslib выполняется при генерации JS-символов Emscripten, засоряет
      // stdout, который emcc парсит как JSON, и роняет билд с
      // JSONDecodeError: Expecting value: line 1 column 2 (char 1).
      if (!window.__x1WebGLProbeLogged) {
        window.__x1WebGLProbeLogged = true;
        console.log('[X1WebGL] X1WebGL.jslib включён в сборку');
      }
      return document.pointerLockElement ? 1 : 0;
    } catch (e) {
      return 0;
    }
  },

  // Запрос захвата мыши с ПОГЛОЩЕНИЕМ отказа.
  //
  // Зачем: Unity внутри делает Module.requestPointerLock() и не подписывается
  // на отказ промиса. Когда захват запрашивают без жеста пользователя или сразу
  // после выхода (ESC), браузер отклоняет промис, rejection остаётся необработанным,
  // Emscripten вызывает обработчик ошибки Unity — и это рвёт главный цикл игры
  // вместе с WebSocket. Поэтому запрос идёт отсюда, а catch() съедает отказ.
  X1WebGL_TryPointerLock: function () {
    try {
      var canvas = document.getElementById('unity-canvas');
      if (!canvas) {
        canvas = document.querySelector('canvas');
      }
      if (!canvas) {
        return 0;
      }

      if (document.pointerLockElement === canvas) {
        return 1;
      }

      var request = canvas.requestPointerLock();
      if (request && typeof request.catch === 'function') {
        request.catch(function () {
          // Браузер отказал (нет жеста / недавно вышли из захвата) — это штатно.
        });
      }
      return 1;
    } catch (e) {
      return 0;
    }
  },

  X1WebGL_ExitPointerLock: function () {
    try {
      if (document.pointerLockElement) {
        document.exitPointerLock();
      }
    } catch (e) {
      // Игнорируем: выход из захвата может отсутствовать в некоторых браузерах.
    }
  }

});

// Проба канала: если этой строки НЕТ в консоли браузера, X1WebGL.jslib не в сборке.
