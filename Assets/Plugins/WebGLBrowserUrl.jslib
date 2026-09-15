mergeInto(LibraryManager.library, {
    GetURLFromBrowser: function () {
        var url = window.location.href;
        var bufferSize = lengthBytesUTF8(url) + 1;
        var buffer = _malloc(bufferSize);
        stringToUTF8(url, buffer, bufferSize);
        return buffer;
    }
});