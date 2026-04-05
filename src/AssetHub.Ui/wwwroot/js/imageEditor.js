/**
 * Filerobot Image Editor JS interop module.
 * Wraps the Filerobot lifecycle and provides save/upload callbacks to .NET.
 * Loaded locally as window.FilerobotImageEditor (vendored UMD bundle).
 */

let _editorInstance = null;
let _dotNetHelper = null;
let _hasUnsavedChanges = false;

/**
 * Initializes the Filerobot Image Editor in the specified container.
 * @param {string} containerId - DOM element ID to mount the editor in.
 * @param {string} imageUrl - Presigned URL of the original image.
 * @param {object} themeColors - { primary, surface, textPrimary } from MudBlazor theme.
 * @param {object} dotNetHelper - DotNetObjectReference for callbacks.
 */
export function initEditor(containerId, imageUrl, themeColors, dotNetHelper) {
    _dotNetHelper = dotNetHelper;
    _hasUnsavedChanges = false;

    const container = document.getElementById(containerId);
    if (!container) {
        console.error('Image editor container not found:', containerId);
        dotNetHelper.invokeMethodAsync('OnEditorError', 'Editor container element not found.');
        return;
    }

    if (typeof window.FilerobotImageEditor === 'undefined') {
        console.error('FilerobotImageEditor library not loaded');
        dotNetHelper.invokeMethodAsync('OnEditorError', 'Image editor library failed to load.');
        return;
    }

    const { TABS } = window.FilerobotImageEditor;

    const config = {
        source: imageUrl,
        useBackendTranslations: false,
        avoidChangesNotSavedAlertOnLeave: true,
        defaultSavedImageType: 'jpeg',
        defaultSavedImageQuality: 0.92,
        savingPixelRatio: 2,
        previewPixelRatio: window.devicePixelRatio || 1,
        disableSaveIfNoChanges: true,
        tabsIds: [TABS.ADJUST, TABS.FINETUNE, TABS.FILTERS, TABS.RESIZE, TABS.ANNOTATE, TABS.WATERMARK],
        defaultTabId: TABS.ADJUST,
        Adjust: {
            brightness: { min: -100, max: 100 },
            contrast: { min: -100, max: 100 },
            hue: { min: -100, max: 100 },
            saturation: { min: -100, max: 100 },
            warmth: { min: -100, max: 100 },
            exposure: { min: -100, max: 100 },
        },
        Finetune: {
            brightness: { min: -100, max: 100 },
            contrast: { min: -100, max: 100 },
            hue: { min: -100, max: 100 },
            saturation: { min: -100, max: 100 },
            warmth: { min: -100, max: 100 },
            exposure: { min: -100, max: 100 },
            blurriness: { min: 0, max: 100 },
        },
        morph: {
            closingReasons: {
                SAVE: false,
                CLOSE_BUTTON: false,
                BACK_BUTTON: false,
            },
        },
        theme: {
            palette: {
                'bg-secondary': themeColors?.surface || '#1e1e2d',
                'bg-primary': themeColors?.surface || '#1e1e2d',
                'bg-primary-active': themeColors?.primaryLight || '#e8eef5',
                'accent-primary': themeColors?.primary || '#776be7',
                'accent-primary-active': themeColors?.primary || '#776be7',
                'icons-primary': themeColors?.textPrimary || '#ffffff',
                'icons-secondary': themeColors?.textPrimary || '#ffffff',
                'btn-primary-text': themeColors?.textPrimary || '#ffffff',
                'btn-secondary-text': themeColors?.textPrimary || '#ffffff',
                'borders-secondary': themeColors?.primary || '#776be7',
            },
            typography: {
                fontFamily: 'Inter, Roboto, Arial',
            },
        },
        onModify: () => {
            if (!_hasUnsavedChanges) {
                _hasUnsavedChanges = true;
                _dotNetHelper?.invokeMethodAsync('OnEditorModified');
            }
        },
        onSave: (editedImageObject, designState) => {
            // Intercept Filerobot's save — delegate to .NET for Replace Original flow
            _dotNetHelper?.invokeMethodAsync('OnEditorSaveRequested');
        },
    };

    _editorInstance = new window.FilerobotImageEditor(container, config);
    _editorInstance.render({
        onClose: (closingReason) => {
            _dotNetHelper?.invokeMethodAsync('OnEditorClose');
        },
    });

    dotNetHelper.invokeMethodAsync('OnEditorReady');
}

/**
 * Gets info about the current edited image (content type and size) without uploading.
 * @param {string} format - Image format: 'jpeg', 'png', or 'webp'.
 * @param {number} quality - Image quality 0.1-1.0 (for jpeg/webp).
 * @returns {object} Image info { success, contentType, size, error }.
 */
export async function getEditedImageInfo(format, quality) {
    if (!_editorInstance) {
        return { success: false, contentType: '', size: 0, error: 'Editor not initialized' };
    }

    try {
        const blob = await getEditedBlob(format, quality);
        if (!blob) {
            return { success: false, contentType: '', size: 0, error: 'Failed to get edited image data' };
        }

        return { success: true, contentType: blob.type, size: blob.size, error: '' };
    } catch (err) {
        console.error('getEditedImageInfo failed:', err);
        return { success: false, contentType: '', size: 0, error: err.message || 'Unknown error' };
    }
}

/**
 * Gets the current edited image data as a Blob and uploads it to a presigned URL.
 * @param {string} presignedUrl - MinIO presigned PUT URL.
 * @param {string} format - Image format: 'jpeg', 'png', or 'webp'.
 * @param {number} quality - Image quality 0.1-1.0 (for jpeg/webp).
 * @returns {Promise<object>} Upload result { success, contentType, size, error }.
 */
export async function saveAndUpload(presignedUrl, format, quality) {
    if (!_editorInstance) {
        return { success: false, contentType: '', size: 0, error: 'Editor not initialized' };
    }

    try {
        const blob = await getEditedBlob(format, quality);
        if (!blob) {
            return { success: false, contentType: '', size: 0, error: 'Failed to get edited image data' };
        }

        const result = await uploadBlob(presignedUrl, blob, blob.type);

        _hasUnsavedChanges = false;

        return {
            success: result,
            contentType: blob.type,
            size: blob.size,
            error: result ? '' : 'Upload failed'
        };
    } catch (err) {
        console.error('Save and upload failed:', err);
        return { success: false, contentType: '', size: 0, error: err.message || 'Unknown error' };
    }
}

/**
 * Extracts the edited image from the editor as a Blob.
 * Uses fetch() for async, memory-efficient base64-to-blob conversion.
 * @param {string} format - Image format: 'jpeg', 'png', or 'webp'.
 * @param {number} quality - Image quality 0.1-1.0 (for jpeg/webp).
 * @returns {Promise<Blob|null>} The image Blob, or null on failure.
 */
async function getEditedBlob(format, quality) {
    const imgData = _editorInstance.getCurrentImgData(
        { extension: format, quality: quality },
        2, // pixelRatio
        true // keepLoadingSpinnerShown
    );

    if (!imgData?.imageData?.imageBase64) {
        return null;
    }

    const base64 = imgData.imageData.imageBase64;
    const response = await fetch(base64);
    return await response.blob();
}

/**
 * Uploads a Blob to a presigned PUT URL via XMLHttpRequest (for progress support).
 * @param {string} presignedUrl - The presigned PUT URL.
 * @param {Blob} blob - The image Blob.
 * @param {string} contentType - MIME type for the Content-Type header.
 * @returns {Promise<boolean>} True if upload succeeded.
 */
function uploadBlob(presignedUrl, blob, contentType) {
    return new Promise((resolve) => {
        const xhr = new XMLHttpRequest();

        xhr.addEventListener('load', () => {
            resolve(xhr.status >= 200 && xhr.status < 300);
        });

        xhr.addEventListener('error', () => resolve(false));
        xhr.addEventListener('abort', () => resolve(false));

        xhr.open('PUT', presignedUrl, true);
        xhr.setRequestHeader('Content-Type', contentType);
        xhr.send(blob);
    });
}

/**
 * Returns whether the editor has unsaved changes.
 * @returns {boolean}
 */
export function hasUnsavedChanges() {
    return _hasUnsavedChanges;
}

/**
 * Resets the unsaved changes flag after a successful save.
 */
export function clearUnsavedChanges() {
    _hasUnsavedChanges = false;
}

/**
 * Destroys the editor instance and cleans up.
 */
export function destroyEditor() {
    if (_editorInstance) {
        try {
            _editorInstance.terminate();
        } catch { /* ignore cleanup errors */ }
        _editorInstance = null;
    }
    _dotNetHelper = null;
    _hasUnsavedChanges = false;
}

let _beforeUnloadHandler = null;

export function enableBeforeUnload() {
    if (_beforeUnloadHandler) return;
    _beforeUnloadHandler = (e) => {
        e.preventDefault();
    };
    window.addEventListener('beforeunload', _beforeUnloadHandler);
}

export function disableBeforeUnload() {
    if (_beforeUnloadHandler) {
        window.removeEventListener('beforeunload', _beforeUnloadHandler);
        _beforeUnloadHandler = null;
    }
}
