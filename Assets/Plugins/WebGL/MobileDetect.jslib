mergeInto(LibraryManager.library, {

  // Возвращает 1, если браузер запущен на мобильном устройстве, иначе 0.
  //
  // Определение БЕЗ проверок размера экрана в пикселях — они неточны:
  // планшеты, свёрнутые окна браузера на десктопе и экраны с Retina/
  // высоким devicePixelRatio ломают такие эвристики. Используем только
  // userAgent браузера и его возможности ввода (тач-события, тип указателя).
  //
  // ВНИМАНИЕ: если этот файл не попал в сборку (сломанный .meta у .jslib),
  // C#-сторона падает в фолбэки (Input.touchSupported / InputSystem.Touchscreen),
  // поэтому панель всё равно покажется на телефоне. См. Assets/Scripts/X1DeviceDetect.cs.
  IsMobileBrowser: function () {
    try {
      // Проба канала: если этой строки (и лога ниже с входными данными) НЕТ
      // в консоли браузера при запуске, значит MobileDetect.jslib НЕ
      // слинкован в сборку (чаще всего сломан .meta — нет секции
      // PluginImporter с WebGL: enabled:1). Проверь .meta и пересобери.
      // ВАЖНО: лог обязан быть ВНУТРИ функции. console.log на верхнем уровне
      // .jslib выполняется при генерации JS-символов Emscripten, засоряет
      // stdout, который emcc парсит как JSON, и роняет билд с
      // JSONDecodeError: Expecting value: line 1 column 2 (char 1).
      console.log('[MobileDetect] MobileDetect.jslib включён в сборку');
      var ua = (navigator.userAgent || navigator.vendor || window.opera || '').toString();

      // Классические мобильные/планшетные токены в userAgent
      var isMobileUA = /Android|iPhone|iPad|iPod|IEMobile|Opera Mini|Mobile|Tablet|PlayBook|Silk|Kindle|BlackBerry|Windows Phone/i.test(ua);

      // iPadOS 13+ может отдавать десктопный userAgent «Macintosh», но при этом
      // это планшет с тач-экраном (несколько точек касания).
      var isIPadOS = /Macintosh/i.test(ua) && (navigator.maxTouchPoints || 0) > 1;

      // Наличие тач-ввода в браузере
      var hasTouch = ('ontouchstart' in window) || !!window.TouchEvent || (navigator.maxTouchPoints || 0) > 0;
      // Основной указатель — неточный (палец), а не мышь/стилус
      var isCoarse = !!(window.matchMedia && window.matchMedia('(pointer: coarse)').matches);

      var result = (isMobileUA || isIPadOS || (hasTouch && isCoarse)) ? 1 : 0;

      // Всегда логируем входные данные — по этому выводу видно, ПОЧЕМУ
      // принято решение, без повторной отладки.
      console.log('[MobileDetect] ua="' + ua + '" isMobileUA=' + isMobileUA +
        ' isIPadOS=' + isIPadOS + ' hasTouch=' + hasTouch + ' coarse=' + isCoarse +
        ' maxTouchPoints=' + (navigator.maxTouchPoints || 0) + ' => ' + result);

      return result;
    } catch (e) {
      console.warn('[MobileDetect] ошибка определения устройства:', e);
      return 0;
    }
  }

});
