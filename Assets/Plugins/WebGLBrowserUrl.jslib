mergeInto(LibraryManager.library, {
    GetURLFromBrowser: function () {
        // Проба канала: если этой строки НЕТ в консоли браузера,
        // WebGLBrowserUrl.jslib не в сборке.
        // ВАЖНО: лог обязан быть ВНУТРИ функции. console.log на верхнем
        // уровне .jslib выполняется при генерации JS-символов Emscripten,
        // засоряет stdout, который emcc парсит как JSON, и роняет билд с
        // JSONDecodeError: Expecting value: line 1 column 2 (char 1).
        console.log('[WebGLBrowserUrl] WebGLBrowserUrl.jslib включён в сборку');
        var url = window.location.href;
        var bufferSize = lengthBytesUTF8(url) + 1;
        var buffer = _malloc(bufferSize);
        stringToUTF8(url, buffer, bufferSize);
        return buffer;
    }
});

// Проба канала: если этой строки НЕТ в консоли браузера, WebGLBrowserUrl.jslib не в сборке.
