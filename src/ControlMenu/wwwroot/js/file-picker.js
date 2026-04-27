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

// Check if File System Access API is available
window.hasFileSystemAccess = function () {
    return typeof window.showOpenFilePicker === 'function'
        && typeof window.showSaveFilePicker === 'function';
};
