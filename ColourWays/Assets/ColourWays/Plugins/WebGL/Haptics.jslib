mergeInto(LibraryManager.library, {
    CW_Vibrate: function (milliseconds) {
        try {
            if (typeof navigator !== "undefined" && navigator.vibrate) navigator.vibrate(milliseconds);
        } catch (e) { }
    },

    CW_VibratePattern: function (patternPtr) {
        try {
            if (typeof navigator === "undefined" || !navigator.vibrate) return;
            var pattern = UTF8ToString(patternPtr).split(",").map(function (v) { return parseInt(v, 10) || 0; });
            navigator.vibrate(pattern);
        } catch (e) { }
    }
});
