mergeInto(LibraryManager.library, {
  X1WebGL_IsPointerLocked: function () {
    return Module['canvas'] && document.pointerLockElement === Module['canvas'] ? 1 : 0;
  },

  X1WebGL_SetPointerLockEnabled: function (enabled) {
    var canvas = Module['canvas'];
    if (!canvas) return;

    if (!enabled) {
      if (canvas.x1PointerLockHandler) {
        canvas.removeEventListener('pointerdown', canvas.x1PointerLockHandler, true);
        delete canvas.x1PointerLockHandler;
      }
      if (document.pointerLockElement === canvas) document.exitPointerLock();
      return;
    }

    if (canvas.x1PointerLockHandler || !canvas.requestPointerLock) return;
    canvas.x1PointerLockHandler = function (event) {
      // A Unity Update callback can run after the browser's user activation expires.
      if (event.pointerType !== 'mouse' || (event.button !== 0 && event.button !== 2) ||
          document.pointerLockElement === canvas) return;
      try {
        var request = canvas.requestPointerLock();
        if (request && typeof request.catch === 'function') request.catch(function () {});
      } catch (error) {
        // No pointer lock permission, or the player just pressed Escape. Retry on the next click.
      }
    };
    canvas.addEventListener('pointerdown', canvas.x1PointerLockHandler, true);
  }
});
