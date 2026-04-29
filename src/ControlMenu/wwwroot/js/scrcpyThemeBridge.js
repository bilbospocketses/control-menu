(function () {
    'use strict';

    var IFRAME_SELECTOR = '#ws-scrcpy-iframe';
    var READY_TYPE = 'ws-scrcpy-web:theme-ready';
    var CHANGED_TYPE = 'ws-scrcpy-web:theme-changed';
    var PUSH_TYPE = 'ws-scrcpy-web:theme';
    var REQUEST_TYPE = 'ws-scrcpy-web:theme-request';

    var settingFromIframe = false;

    function currentTheme() {
        return (window.themeManager && window.themeManager.get()) || 'dark';
    }

    function iframeOrigin(iframe) {
        try {
            return new URL(iframe.src, window.location.href).origin;
        } catch (e) {
            return '*';
        }
    }

    function postToIframe(iframe, type, theme, targetOrigin) {
        if (!iframe || !iframe.contentWindow) return;
        try {
            var msg = type === REQUEST_TYPE
                ? { type: type }
                : { type: type, theme: theme };
            iframe.contentWindow.postMessage(msg, targetOrigin);
        } catch (e) {
            // Iframe not yet ready or cross-origin error; ignore.
        }
    }

    // Listen for iframe handshakes and changed-events from ws-scrcpy-web.
    window.addEventListener('message', function (event) {
        var data = event.data;
        if (!data || typeof data !== 'object') return;

        if (data.type === READY_TYPE) {
            // Iframe just loaded — reply with our current theme,
            // using event.origin as targetOrigin (locks the reply to the
            // ws-scrcpy-web origin without hardcoding it in CM).
            if (event.source && typeof event.source.postMessage === 'function') {
                event.source.postMessage(
                    { type: PUSH_TYPE, theme: currentTheme() },
                    event.origin
                );
            }
            return;
        }

        if (data.type === CHANGED_TYPE) {
            // ws-scrcpy-web's own UI changed the theme — sync CM to match.
            if (data.theme !== 'dark' && data.theme !== 'light') return;
            if (data.theme === currentTheme()) return; // already in sync
            if (window.themeManager && typeof window.themeManager.set === 'function') {
                // Set guard so notify() doesn't echo back to the iframe.
                settingFromIframe = true;
                try {
                    window.themeManager.set(data.theme);
                } finally {
                    settingFromIframe = false;
                }
            }
            return;
        }
    });

    // Public API exposed to CM's themeManager and to defensive callers.
    window.scrcpyThemeBridge = {
        // Called by themeManager.set/toggle to push a new theme to all
        // currently-mounted ws-scrcpy iframes. No-op when the change
        // originated from the iframe (avoids echo).
        notify: function (theme) {
            if (settingFromIframe) return;
            var iframes = document.querySelectorAll(IFRAME_SELECTOR);
            for (var i = 0; i < iframes.length; i++) {
                var iframe = iframes[i];
                postToIframe(iframe, PUSH_TYPE, theme, iframeOrigin(iframe));
            }
        },
        // Optionally request a re-announce from the iframe (covers the case
        // where this script attached its message listener after the iframe
        // already posted theme-ready). Currently unused — the bridge script
        // tag is registered above the iframe element in the host page, so
        // the listener is always attached first. Available for defensive
        // callers (e.g., a future page that mounts the iframe dynamically).
        requestReady: function () {
            var iframes = document.querySelectorAll(IFRAME_SELECTOR);
            for (var i = 0; i < iframes.length; i++) {
                var iframe = iframes[i];
                postToIframe(iframe, REQUEST_TYPE, null, iframeOrigin(iframe));
            }
        }
    };
})();
