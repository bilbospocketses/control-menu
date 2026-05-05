window.controlMenu = window.controlMenu || {};

/**
 * Returns the top offset (in CSS px) of the sidebar group-header element with
 * the given module-id, or 0 if not found.
 */
window.controlMenu.getModuleIconRectTop = (moduleId) => {
    const el = document.querySelector(`[data-flyout-anchor="${moduleId}"]`);
    return el ? el.getBoundingClientRect().top : 0;
};

/**
 * Numeric-token resize subscription. Returns a token that the caller passes to
 * unsubscribeFlyoutResize. Pattern mirrors themeManager.subscribeBlazor in
 * theme.js — avoids the IJSObjectReference marshaling issue documented in
 * feedback_blazor_jsinterop_marshaling.md.
 */
window.controlMenu._flyoutResizeHandlers = window.controlMenu._flyoutResizeHandlers || {};
window.controlMenu._flyoutResizeNextToken = window.controlMenu._flyoutResizeNextToken || 1;

window.controlMenu.subscribeFlyoutResize = (dotnetRef) => {
    const token = window.controlMenu._flyoutResizeNextToken++;
    const handler = () => {
        try {
            dotnetRef.invokeMethodAsync('OnWindowResize');
        } catch (e) {
            // dotnetRef may have been disposed (circuit ended). Swallow.
        }
    };
    window.addEventListener('resize', handler);
    window.controlMenu._flyoutResizeHandlers[token] = handler;
    return token;
};

window.controlMenu.unsubscribeFlyoutResize = (token) => {
    const handler = window.controlMenu._flyoutResizeHandlers[token];
    if (handler) {
        window.removeEventListener('resize', handler);
        delete window.controlMenu._flyoutResizeHandlers[token];
    }
};
