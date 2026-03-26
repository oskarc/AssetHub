/**
 * Direct-to-MinIO file upload via presigned PUT URLs.
 * Bypasses the Blazor Server SignalR circuit entirely,
 * enabling large file uploads (up to 700MB+) with progress tracking.
 */

/**
 * Upload a file directly to MinIO using a presigned PUT URL.
 * @param {string} presignedUrl - The presigned PUT URL from the init-upload API
 * @param {HTMLInputElement} inputElement - The file input element
 * @param {number} fileIndex - Index of the file in the input's FileList
 * @param {DotNetObjectReference} dotNetHelper - Blazor interop callback object
 * @returns {Promise<boolean>} - Success status
 */
export async function uploadFile(presignedUrl, inputElement, fileIndex, dotNetHelper) {
    const file = inputElement.files[fileIndex];
    if (!file) {
        await dotNetHelper.invokeMethodAsync('OnUploadError', 'File not found');
        return false;
    }

    return new Promise((resolve) => {
        const xhr = new XMLHttpRequest();

        xhr.upload.addEventListener('progress', async (e) => {
            if (e.lengthComputable) {
                const percent = Math.round((e.loaded / e.total) * 100);
                await dotNetHelper.invokeMethodAsync('OnUploadProgress', percent, e.loaded, e.total);
            }
        });

        xhr.addEventListener('load', async () => {
            if (xhr.status >= 200 && xhr.status < 300) {
                await dotNetHelper.invokeMethodAsync('OnUploadComplete');
                resolve(true);
            } else {
                await dotNetHelper.invokeMethodAsync('OnUploadError',
                    `Upload failed with status ${xhr.status}: ${xhr.statusText}`);
                resolve(false);
            }
        });

        xhr.addEventListener('error', async () => {
            await dotNetHelper.invokeMethodAsync('OnUploadError', 'Network error during upload');
            resolve(false);
        });

        xhr.addEventListener('abort', async () => {
            await dotNetHelper.invokeMethodAsync('OnUploadError', 'Upload was cancelled');
            resolve(false);
        });

        xhr.open('PUT', presignedUrl, true);
        xhr.setRequestHeader('Content-Type', file.type || 'application/octet-stream');
        xhr.send(file);
    });
}

/**
 * Get file metadata from a file input without reading the full content.
 * Used to get file info for the init-upload API call before actually uploading.
 * @param {HTMLInputElement} inputElement - The file input element
 * @returns {Array<{name: string, size: number, type: string}>} File metadata array
 */
export function getFileMetadata(inputElement) {
    const result = [];
    for (const file of inputElement.files) {
        result.push({
            name: file.name,
            size: file.size,
            type: file.type || 'application/octet-stream'
        });
    }
    return result;
}

/**
 * Preload thumbnail images so the browser caches them before the grid renders.
 * Resolves when all images have loaded (or failed), with a safety timeout.
 * @param {string[]} urls - Array of thumbnail URLs to preload
 * @param {number} timeoutMs - Maximum wait time in milliseconds (default: 10000)
 * @returns {Promise<void>}
 */
export function preloadImages(urls, timeoutMs = 10000) {
    if (!urls || urls.length === 0) return Promise.resolve();

    return new Promise((resolve) => {
        const timeout = setTimeout(resolve, timeoutMs);
        let remaining = urls.length;

        const onDone = () => {
            remaining--;
            if (remaining <= 0) {
                clearTimeout(timeout);
                resolve();
            }
        };

        for (const url of urls) {
            const img = new Image();
            img.onload = onDone;
            img.onerror = onDone;
            img.src = url;
        }
    });
}

/** @type {((e: BeforeUnloadEvent) => void) | null} */
let _beforeUnloadHandler = null;

/**
 * Enable the browser's native "Leave site?" confirmation dialog.
 * Called when uploads start.
 */
export function enableBeforeUnload() {
    if (_beforeUnloadHandler) return;
    _beforeUnloadHandler = (e) => {
        e.preventDefault();
    };
    window.addEventListener('beforeunload', _beforeUnloadHandler);
}

/**
 * Disable the browser's native "Leave site?" dialog.
 * Called when all uploads finish or the component is disposed.
 */
export function disableBeforeUnload() {
    if (_beforeUnloadHandler) {
        window.removeEventListener('beforeunload', _beforeUnloadHandler);
        _beforeUnloadHandler = null;
    }
}
