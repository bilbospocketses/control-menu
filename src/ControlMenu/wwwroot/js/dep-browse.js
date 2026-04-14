// Folder path picker for dependency install paths.
// Uses showDirectoryPicker where available (shows native OS dialog),
// falls back to a prompt for the path since the browser API can't
// return full filesystem paths for security reasons.
window.depPathBrowse = async function (depName) {
    const input = document.getElementById('dep-path-' + depName);
    const currentPath = input ? input.value : '';

    // Try native folder picker (Chrome/Edge) — gives user a visual cue
    // even though we still need them to confirm/paste the path
    if (typeof window.showDirectoryPicker === 'function') {
        try {
            const handle = await window.showDirectoryPicker({
                id: 'dep-' + depName,
                mode: 'read',
                startIn: 'desktop'
            });
            // Ask user to confirm the full path (API doesn't expose it)
            const path = prompt(
                'Selected: "' + handle.name + '"\n\n' +
                'Paste or confirm the full path to this folder:',
                currentPath
            );
            return path && path.trim() ? path.trim() : null;
        } catch (e) {
            if (e.name === 'AbortError') return null;
        }
    }

    // Fallback: prompt with current value
    const path = prompt('Enter install path for ' + depName + ':', currentPath);
    return path && path.trim() ? path.trim() : null;
};
