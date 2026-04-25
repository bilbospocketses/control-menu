window.themeManager = {
    _storageKey: 'controlmenu-theme',

    get: function () {
        return localStorage.getItem(this._storageKey) || 'dark';
    },

    set: function (theme) {
        localStorage.setItem(this._storageKey, theme);
        document.documentElement.setAttribute('data-theme', theme);
    },

    toggle: function () {
        var current = this.get();
        var next = current === 'dark' ? 'light' : 'dark';
        this.set(next);
        return next;
    },

    init: function () {
        document.documentElement.setAttribute('data-theme', this.get());
    }
};

window.themeManager.init();
