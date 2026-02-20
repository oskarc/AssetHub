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
    const files = inputElement.files;
    const result = [];
    for (let i = 0; i < files.length; i++) {
        result.push({
            name: files[i].name,
            size: files[i].size,
            type: files[i].type || 'application/octet-stream'
        });
    }
    return result;
}
