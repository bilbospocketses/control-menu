window.themeManager = {
    _storageKey: 'controlmenu-theme',
    _listeners: [],

    get: function () {
        return localStorage.getItem(this._storageKey) || 'dark';
    },

    set: function (theme) {
        localStorage.setItem(this._storageKey, theme);
        document.documentElement.setAttribute('data-theme', theme);
        if (window.scrcpyThemeBridge) {
            window.scrcpyThemeBridge.notify(theme);
        }
        // Notify Blazor (and any other) subscribers AFTER bridge.notify so
        // the bridge's settingFromIframe guard wraps cleanly. Fire-and-forget
        // — subscriber errors are swallowed so a misbehaving listener can't
        // break the theme toggle.
        for (var i = 0; i < this._listeners.length; i++) {
            try {
                this._listeners[i](theme);
            } catch (e) {
                // ignore
            }
        }
    },

    toggle: function () {
        var current = this.get();
        var next = current === 'dark' ? 'light' : 'dark';
        this.set(next);
        return next;
    },

    init: function () {
        document.documentElement.setAttribute('data-theme', this.get());
    },

    // Subscribe a callback to fire on every set/toggle. Returns an unsubscribe
    // function. Used by Blazor TopBar to keep its rendered icon in sync when
    // the theme changes via the iframe bridge or any other path.
    subscribe: function (callback) {
        this._listeners.push(callback);
        var self = this;
        return function () {
            var idx = self._listeners.indexOf(callback);
            if (idx >= 0) self._listeners.splice(idx, 1);
        };
    },

    // Blazor-friendly shim: wraps a DotNetObjectReference so .NET methods
    // can be called as listeners. The .NET side must expose an [JSInvokable]
    // method named 'OnThemeChanged(string theme)'.
    subscribeBlazor: function (dotnetRef) {
        return this.subscribe(function (theme) {
            try {
                dotnetRef.invokeMethodAsync('OnThemeChanged', theme);
            } catch (e) {
                // ignore — the .NET reference may have been disposed
            }
        });
    }
};

window.themeManager.init();
