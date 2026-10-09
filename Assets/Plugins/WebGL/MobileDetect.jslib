mergeInto(LibraryManager.library, {
  // Возвращает 1, если браузер мобильный, иначе 0.
  IsMobileBrowser: function () {
    try {
      var ua = navigator.userAgent || navigator.vendor || window.opera || '';

      // Классические мобильные/планшетные UA
      var isMobileUA = /Android|iPhone|iPad|iPod|IEMobile|Opera Mini|Mobile|Tablet|PlayBook|Silk|Kindle/i.test(ua);

      // iPadOS 13+ прикидывается Macintosh, но имеет touch-точки
      var isIPadOS = /Macintosh/i.test(ua) && (navigator.maxTouchPoints || 0) > 1;

      // Дополнительная проверка: touch-события и ориентация
      var hasTouch = ('ontouchstart' in window) || (navigator.maxTouchPoints > 0);
      var isCoarse = window.matchMedia && window.matchMedia('(pointer: coarse)').matches;

      if (isMobileUA || isIPadOS) return 1;

      // Фолбэк: тач без мыши — почти наверняка мобильный
      if (hasTouch && isCoarse) return 1;

      return 0;
    } catch (e) {
      return 0;
    }
  }
});