// Project files and downloads, entirely in the browser. Chromium browsers get real open/save through the File System
// Access API (so Save writes back to the same .pbicomp); other browsers fall back to an upload and a download.

let projectHandle = null;
let dirty = false;

window.addEventListener('beforeunload', e => {
    if (dirty) {
        e.preventDefault();
        e.returnValue = '';
    }
});

const pickerTypes = [{ description: 'DAXDiff project', accept: { 'application/json': ['.pbicomp'] } }];

export function setDirty(value) {
    dirty = value;
}

export function forgetProjectHandle() {
    projectHandle = null;
}

/** Lets the user pick a .pbicomp; returns { name, text } or null when cancelled. */
export async function openProject() {
    if (window.showOpenFilePicker) {
        try {
            const [handle] = await window.showOpenFilePicker({ types: pickerTypes, multiple: false });
            const file = await handle.getFile();
            projectHandle = handle;
            return { name: file.name, text: await file.text() };
        } catch (e) {
            if (e.name === 'AbortError')
                return null;
            throw e;
        }
    }

    const file = await new Promise(resolve => {
        const input = document.createElement('input');
        input.type = 'file';
        input.accept = '.pbicomp,application/json';
        input.addEventListener('change', () => resolve(input.files[0] ?? null));
        input.addEventListener('cancel', () => resolve(null));
        input.click();
    });
    projectHandle = null;
    return file ? { name: file.name, text: await file.text() } : null;
}

/**
 * Saves the project text. Writes back to the opened file when there is one (unless saveAs), else asks where to save,
 * else downloads it. Returns the file name, or null when cancelled.
 */
export async function saveProject(text, suggestedName, saveAs) {
    if (window.showSaveFilePicker) {
        try {
            if (!projectHandle || saveAs)
                projectHandle = await window.showSaveFilePicker({ suggestedName, types: pickerTypes });
            const writable = await projectHandle.createWritable();
            await writable.write(text);
            await writable.close();
            return projectHandle.name;
        } catch (e) {
            if (e.name === 'AbortError')
                return null;
            throw e;
        }
    }

    download(new Blob([text], { type: 'application/json' }), suggestedName);
    return suggestedName;
}

/** Downloads bytes streamed from .NET (e.g. the Excel workbook). */
export async function downloadStream(fileName, contentType, streamRef) {
    const buffer = await streamRef.arrayBuffer();
    download(new Blob([buffer], { type: contentType }), fileName);
}

function download(blob, fileName) {
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = fileName;
    document.body.appendChild(a);
    a.click();
    a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 10000);
}

export function getSetting(key) {
    try {
        return localStorage.getItem(key);
    } catch {
        return null;
    }
}

export function setSetting(key, value) {
    try {
        if (value === null || value === '')
            localStorage.removeItem(key);
        else
            localStorage.setItem(key, value);
    } catch {
        // Storage can be blocked (private mode, policies); settings then last for this visit only.
    }
}
