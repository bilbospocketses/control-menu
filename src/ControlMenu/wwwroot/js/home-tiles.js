window.controlMenu = window.controlMenu || {};

/**
 * Measure-and-snap pass for the home page modular tile grid. For each
 * .module-card, measure its natural content height and snap its grid-row span
 * to the smallest whole number of tile-units that fits, by writing the
 * --cm-tile-span custom property the CSS reads. Authoritative over the
 * server-seeded estimate.
 */
window.controlMenu.layoutHomeTiles = function () {
    var grid = document.querySelector('.module-grid');
    if (!grid) {
        return;
    }
    var gridStyle = getComputedStyle(grid);
    var unit = parseFloat(gridStyle.gridAutoRows); // px height of one tile-unit
    var gap = parseFloat(gridStyle.rowGap) || 0;   // px gap between rows
    if (!unit || isNaN(unit)) {
        return;
    }
    var cards = grid.querySelectorAll('.module-card');
    for (var i = 0; i < cards.length; i++) {
        var card = cards[i];
        // scrollHeight is the full content height even when overflow clips the
        // card to a shorter cell, so it is independent of the current span.
        var content = card.scrollHeight;
        var span = Math.max(1, Math.ceil((content + gap) / (unit + gap)));
        card.style.setProperty('--cm-tile-span', String(span));
    }
};

/**
 * Numeric-token resize subscription. Re-runs layoutHomeTiles on a debounced
 * window resize and returns a token the caller passes to
 * unsubscribeHomeTilesResize on disposal. Mirrors
 * controlMenu.subscribeFlyoutResize in sidebar-flyout.js (the numeric-token
 * pattern avoids the IJSObjectReference marshaling issue).
 */
window.controlMenu._homeTilesResizeHandlers = window.controlMenu._homeTilesResizeHandlers || {};
window.controlMenu._homeTilesResizeNextToken = window.controlMenu._homeTilesResizeNextToken || 1;

window.controlMenu.subscribeHomeTilesResize = function () {
    var token = window.controlMenu._homeTilesResizeNextToken++;
    var timer = null;
    var handler = function () {
        if (timer) {
            clearTimeout(timer);
        }
        timer = setTimeout(window.controlMenu.layoutHomeTiles, 100);
    };
    window.addEventListener('resize', handler);
    window.controlMenu._homeTilesResizeHandlers[token] = handler;
    return token;
};

window.controlMenu.unsubscribeHomeTilesResize = function (token) {
    var handler = window.controlMenu._homeTilesResizeHandlers[token];
    if (handler) {
        window.removeEventListener('resize', handler);
        delete window.controlMenu._homeTilesResizeHandlers[token];
    }
};
