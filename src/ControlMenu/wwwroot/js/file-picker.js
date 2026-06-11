// File System Access API helpers for Icon Converter

// Opens native file picker, reads the file, returns { name, bytes } or null
window.filePickerOpen = async function (acceptTypes) {
    if (typeof window.showOpenFilePicker !== 'function') return null;
    try {
        const [handle] = await window.showOpenFilePicker({
            types: [{
                description: 'Images',
                accept: { 'image/*': acceptTypes.split(',') }
            }],
            multiple: false
        });
        const file = await handle.getFile();
        const buffer = await file.arrayBuffer();
        // Encode as base64 — returning Uint8Array nested in an object does not
        // round-trip cleanly through Blazor JS interop into a record's byte[] field.
        let binary = '';
        const bytes = new Uint8Array(buffer);
        const chunkSize = 0x8000;
        for (let i = 0; i < bytes.length; i += chunkSize) {
            binary += String.fromCharCode.apply(null, bytes.subarray(i, i + chunkSize));
        }
        return {
            name: file.name,
            bytesBase64: btoa(binary)
        };
    } catch (e) {
        if (e.name === 'AbortError') return null;
        throw e;
    }
};

// Opens native save picker with suggested name, writes bytes to the chosen location.
// Returns the saved filename or null if cancelled.
window.filePickerSave = async function (suggestedName, base64Data) {
    if (typeof window.showSaveFilePicker !== 'function') return null;
    try {
        const handle = await window.showSaveFilePicker({
            suggestedName: suggestedName,
            types: [{
                description: 'Icon files',
                accept: { 'image/x-icon': ['.ico'] }
            }]
        });
        const writable = await handle.createWritable();
        const bytes = Uint8Array.from(atob(base64Data), c => c.charCodeAt(0));
        await writable.write(bytes);
        await writable.close();
        return handle.name;
    } catch (e) {
        if (e.name === 'AbortError') return null;
        throw e;
    }
};

// Opens native save picker with suggested name, writes bytes to the chosen location.
// Generic variant: derives the accept entry from the suggested name's extension so the
// picker accepts any image format (PNG/JPG/WEBP/AVIF/TIFF/BMP/GIF/...), unlike
// filePickerSave which is hard-coded to .ico for the Icon Converter.
// Returns the saved filename or null if cancelled.
window.filePickerSaveAs = async function (suggestedName, base64Data) {
    if (typeof window.showSaveFilePicker !== 'function') return null;
    try {
        const dot = suggestedName.lastIndexOf('.');
        const ext = dot >= 0 ? suggestedName.substring(dot).toLowerCase() : '.png';
        const mimeByExt = {
            '.png': 'image/png',
            '.jpg': 'image/jpeg',
            '.jpeg': 'image/jpeg',
            '.webp': 'image/webp',
            '.avif': 'image/avif',
            '.tif': 'image/tiff',
            '.tiff': 'image/tiff',
            '.bmp': 'image/bmp',
            '.gif': 'image/gif'
        };
        const mime = mimeByExt[ext] || 'application/octet-stream';
        const handle = await window.showSaveFilePicker({
            suggestedName: suggestedName,
            types: [{
                description: 'Image files',
                accept: { [mime]: [ext] }
            }]
        });
        const writable = await handle.createWritable();
        const bytes = Uint8Array.from(atob(base64Data), c => c.charCodeAt(0));
        await writable.write(bytes);
        await writable.close();
        return handle.name;
    } catch (e) {
        if (e.name === 'AbortError') return null;
        throw e;
    }
};

// Check if File System Access API is available
window.hasFileSystemAccess = function () {
    return typeof window.showOpenFilePicker === 'function'
        && typeof window.showSaveFilePicker === 'function';
};

// Returns an image element's natural (intrinsic) and rendered (client) dimensions, so callers
// can map a click's OffsetX/OffsetY (rendered coords) back to source-pixel coords:
//   sourceX = offsetX * naturalWidth / renderedWidth.
// Used by the Magic Wand page for click-to-seed. Returns null if the element is missing.
window.getElementRect = function (id) {
    const el = document.getElementById(id);
    if (!el) return null;
    return {
        naturalWidth: el.naturalWidth || 0,
        naturalHeight: el.naturalHeight || 0,
        renderedWidth: el.clientWidth || 0,
        renderedHeight: el.clientHeight || 0
    };
};
