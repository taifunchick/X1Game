const assert = require('node:assert/strict');
const fs = require('node:fs');
const test = require('node:test');
const vm = require('node:vm');
const path = require('node:path');

function loadPlugin(name, globals) {
  const library = {};
  const source = path.join(__dirname, '../Assets/Plugins/WebGL', `${name}.jslib`);
  vm.runInNewContext(fs.readFileSync(source, 'utf8'), {
    LibraryManager: { library }, mergeInto: Object.assign,
    console: { log() {}, warn() {} }, ...globals,
  });
  return library;
}

test('mobile detection covers phones, desktop mode and desktop PCs with touchscreens', () => {
  function detect(userAgent, maxTouchPoints, coarse) {
    return loadPlugin('MobileDetect', {
      navigator: { userAgent, maxTouchPoints },
      window: { matchMedia: () => ({ matches: coarse }) },
    }).IsMobileBrowser();
  }
  assert.equal(detect('Android Mobile', 5, true), 1);
  assert.equal(detect('iPhone', 5, true), 1);
  assert.equal(detect('Macintosh', 5, false), 1);
  assert.equal(detect('X11; Linux x86_64', 5, true), 1);
  assert.equal(detect('Windows NT', 10, false), 0);
  assert.equal(detect('Macintosh', 0, false), 0);
});

test('pointer lock uses mouse gestures on the Unity canvas and can be released and reacquired', async () => {
  let handler;
  let attempts = 0;
  const document = { pointerLockElement: null, exitPointerLock() { this.pointerLockElement = null; } };
  const canvas = {
    addEventListener(type, callback, capture) {
      assert.equal(type, 'pointerdown');
      assert.equal(capture, true);
      assert.equal(handler, undefined);
      handler = callback;
    },
    removeEventListener(type, callback, capture) {
      assert.equal(type, 'pointerdown');
      assert.equal(capture, true);
      assert.equal(handler, callback);
      handler = undefined;
    },
    requestPointerLock() {
      if (++attempts === 1) return Promise.reject(new Error('browser refused'));
      document.pointerLockElement = this;
    },
  };
  const plugin = loadPlugin('X1WebGL', { Module: { canvas }, document });
  plugin.X1WebGL_SetPointerLockEnabled(1);
  plugin.X1WebGL_SetPointerLockEnabled(1);
  handler({ pointerType: 'touch', button: 0 });
  assert.equal(attempts, 0);
  const click = { pointerType: 'mouse', button: 0 };
  handler(click);
  await Promise.resolve();
  assert.equal(plugin.X1WebGL_IsPointerLocked(), 0);
  handler(click);
  assert.equal(plugin.X1WebGL_IsPointerLocked(), 1);
  document.exitPointerLock(); // Browser Escape does not disarm the click handler.
  handler(click);
  assert.equal(plugin.X1WebGL_IsPointerLocked(), 1);
  plugin.X1WebGL_SetPointerLockEnabled(0);
  assert.equal(handler, undefined);
  assert.equal(plugin.X1WebGL_IsPointerLocked(), 0);
});
