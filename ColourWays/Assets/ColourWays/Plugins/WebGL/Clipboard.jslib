mergeInto(LibraryManager.library, {
    CW_CopyText: function (textPtr) {
        var text = UTF8ToString(textPtr);
        function copy() {
            if (navigator.clipboard && navigator.clipboard.writeText) {
                navigator.clipboard.writeText(text).catch(fallback);
            } else {
                fallback();
            }
        }
        function fallback() {
            var area = document.createElement("textarea");
            area.value = text;
            area.style.position = "fixed";
            area.style.opacity = "0";
            document.body.appendChild(area);
            area.select();
            document.execCommand("copy");
            document.body.removeChild(area);
        }
        try {
            // Mobile browsers: native share sheet (like NYT Games); copy if it's refused.
            if (navigator.share && /Android|iPhone|iPad|iPod/i.test(navigator.userAgent)) {
                navigator.share({ text: text }).catch(function (err) {
                    if (!err || err.name !== "AbortError") copy();
                });
                return;
            }
            copy();
        } catch (e) { }
    }
});
