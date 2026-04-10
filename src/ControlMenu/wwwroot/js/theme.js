window.themeManager = {
    _storageKey: 'controlmenu-theme',

    get: function () {
        return localStorage.getItem(this._storageKey) || 'system';
    },

    set: function (theme) {
        localStorage.setItem(this._storageKey, theme);
        this._apply(theme);
    },

    _apply: function (theme) {
        if (theme === 'system') {
            var prefersDark = window.matchMedia('(prefers-color-scheme: dark)').matches;
            document.documentElement.setAttribute('data-theme', prefersDark ? 'dark' : 'light');
        } else {
            document.documentElement.setAttribute('data-theme', theme);
        }
    },

    init: function () {
        var theme = this.get();
        this._apply(theme);

        window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', function () {
            if (localStorage.getItem('controlmenu-theme') === 'system') {
                var prefersDark = window.matchMedia('(prefers-color-scheme: dark)').matches;
                document.documentElement.setAttribute('data-theme', prefersDark ? 'dark' : 'light');
            }
        });
    }
};

window.themeManager.init();
