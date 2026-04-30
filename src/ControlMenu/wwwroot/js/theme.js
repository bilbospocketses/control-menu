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
        // Notify Blazor (and any other) subscribers AFTER bridge.notify.
        //
        // The bridge's settingFromIframe guard is fully synchronous and
        // released before this loop runs. The order matters mainly for
        // re-entrancy: if a subscriber synchronously calls themeManager.set
        // again, doing bridge.notify first means the iframe push for the
        // outer set has already fired before the inner set re-enters, so
        // the iframe sees state changes in the order they actually happened.
        //
        // Subscriber errors are swallowed so a misbehaving listener can't
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

    // Token-keyed map of unsubscribe functions. Avoids returning a JS
    // function as IJSObjectReference (which Blazor can't marshal —
    // see fix/theme-toggle-sync-blazor-marshal).
    _blazorUnsubs: {},
    _nextBlazorToken: 0,

    // Blazor-friendly shim: wraps a DotNetObjectReference so .NET methods
    // can be called as listeners. The .NET side must expose a [JSInvokable]
    // method named 'OnThemeChanged(string theme)'. Returns a numeric token
    // that the .NET side passes back to `unsubscribeBlazor` on disposal.
    subscribeBlazor: function (dotnetRef) {
        var unsub = this.subscribe(function (theme) {
            // invokeMethodAsync returns a promise. A synchronous try/catch
            // catches setup errors (null ref, immediate disposal); .catch
            // covers async rejections from a closed Blazor Server circuit.
            try {
                var result = dotnetRef.invokeMethodAsync('OnThemeChanged', theme);
                if (result && typeof result.catch === 'function') {
                    result.catch(function () {
                        // .NET reference disposed (circuit closed); ignore.
                    });
                }
            } catch (e) {
                // ignore — synchronous failure path
            }
        });
        var token = ++this._nextBlazorToken;
        this._blazorUnsubs[token] = unsub;
        return token;
    },

    // Called by .NET on disposal of a TopBar (or any subscribeBlazor caller).
    unsubscribeBlazor: function (token) {
        var unsub = this._blazorUnsubs[token];
        if (typeof unsub === 'function') {
            unsub();
            delete this._blazorUnsubs[token];
        }
    }
};

window.themeManager.init();
