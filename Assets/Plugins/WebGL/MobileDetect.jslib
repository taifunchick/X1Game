mergeInto(LibraryManager.library, {

  // Возвращает 1, если браузер запущен на мобильном устройстве, иначе 0.
  //
  // Определение БЕЗО ВСЯКИХ проверок размера экрана в пикселях — они неточны:
  // планшеты, свёрнутые окна браузера на десктопе и экраны с Retina/
  // высоким devicePixelRatio ломают такие эвристики. Используем только
  // userAgent браузера и его возможности ввода (тач-у события, тип указателя).
  IsMobileBrowser: function () {
    try {
      var ua = (navigator.userAgent || navigator.vendor || window.opera || '').toString();

      // Классические мобильные/планшетные токены в userAgent
      var isMobileUA = /Android|iPhone|iPad|iPod|IEMobile|Opera Mini|Mobile|Tablet|PlayBook|Silk|Kindle|BlackBerry|Windows Phone/i.test(ua);

      // iPadOS 13+ может отдавать десктопный userAgent «Macintosh», но при этом
      // это планшет с тач-экраном (несколько точек касания).
      var isIPadOS = /Macintosh/i.test(ua) && (navigator.maxTouchPoints || 0) > 1;

      // Наличие тач-ввода в браузере
      var hasTouch = ('ontouchstart' in window) || !!window.TouchEvent || (navigator.maxTouchPoints || 0) > 0;
      // Основной указатель — неточный (палец), а не мышь/стилус
      var isCoarse = window.matchMedia && window.matchMedia('(pointer: coarse)').matches;

      if (isMobileUA || isIPadOS) {
        console.log('[MobileDetect] мобильное устройство по userAgent:', ua);
        return 1;
      }

      // Фолбэк без пикселей: тач-экран + неточный указатель — почти наверняка мобильное устройство.
      // Срабатывает даже для браузеров с нестандартным/сокращённым userAgent.
      if (hasTouch && isCoarse) {
        console.log('[MobileDetect] мобильное устройство по тачу+указателю');
        return 1;
      }

      return 0;
    } catch (e) {
      console.warn('[MobileDetect] ошибка определения устройства:', e);
      return 0;
    }
  }

});
