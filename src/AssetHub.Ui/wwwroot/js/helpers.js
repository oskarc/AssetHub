/**
 * Triggers a file download by creating a temporary anchor element.
 * @param {string} url - The URL to download from.
 */
export function downloadViaAnchor(url) {
    const a = document.createElement('a');
    a.href = url;
    a.download = '';
    a.style.display = 'none';
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
}

/**
 * Enqueues a ZIP download, polls for completion, then triggers the download.
 * Returns a JSON string with { success, error } so Blazor can handle feedback.
 *
 * @param {string} enqueueUrl - The URL to POST/GET to enqueue the ZIP build.
 * @param {object|null} headers - Optional extra headers (e.g. { "X-Share-Token": "..." }).
 * @param {DotNetObjectReference} dotNetRef - Blazor object with UpdateZipProgress(status, message) callback.
 * @returns {Promise<string>} JSON result string.
 */
export async function enqueueAndPollZipDownload(enqueueUrl, headers, dotNetRef) {
    try {
        // Step 1: Enqueue
        const enqueueResp = await fetch(enqueueUrl, {
            method: 'POST',
            credentials: 'same-origin',
            headers: {
                'Content-Type': 'application/json',
                ...(headers || {})
            }
        });

        if (!enqueueResp.ok) {
            const errText = await enqueueResp.text();
            return JSON.stringify({ success: false, error: `Failed to start download: ${enqueueResp.status} ${errText}` });
        }

        const enqueueData = await enqueueResp.json();
        const statusUrl = enqueueData.statusUrl;

        if (!statusUrl) {
            return JSON.stringify({ success: false, error: 'No status URL returned' });
        }

        // Step 2: Poll for completion
        const pollHeaders = headers || {};
        const maxAttempts = 360; // 30 minutes max (5s intervals)
        for (let i = 0; i < maxAttempts; i++) {
            await new Promise(resolve => setTimeout(resolve, 5000));

            const pollResp = await fetch(statusUrl, {
                method: 'GET',
                credentials: 'same-origin',
                headers: pollHeaders
            });

            if (!pollResp.ok) {
                return JSON.stringify({ success: false, error: `Status check failed: ${pollResp.status}` });
            }

            const status = await pollResp.json();

            // Notify Blazor of progress
            if (dotNetRef) {
                try {
                    await dotNetRef.invokeMethodAsync('UpdateZipProgress', status.status, status.fileName || '');
                } catch { /* Blazor ref may be disposed */ }
            }

            if (status.status === 'completed') {
                if (status.downloadUrl) {
                    downloadViaAnchor(status.downloadUrl);
                    return JSON.stringify({ success: true });
                }
                return JSON.stringify({ success: false, error: 'Completed but no download URL' });
            }

            if (status.status === 'failed' || status.status === 'expired') {
                return JSON.stringify({ success: false, error: status.error || 'Download failed' });
            }
        }

        return JSON.stringify({ success: false, error: 'Download timed out' });
    } catch (ex) {
        return JSON.stringify({ success: false, error: ex.message || 'Unknown error' });
    }
}

/**
 * Sets a cookie value.
 * @param {string} name - Cookie name.
 * @param {string} value - Cookie value (already encoded).
 * @param {number} maxAge - Max age in seconds.
 */
export function setCookie(name, value, maxAge) {
    document.cookie = `${name}=${value};path=/;max-age=${maxAge};samesite=lax`;
}

/**
 * Gets a cookie value.
 * @param {string} name - Cookie name.
 * @returns {string|null} Cookie value or null if not found.
 */
export function getCookie(name) {
    const cookies = document.cookie.split(';');
    for (let cookie of cookies) {
        const [cookieName, cookieValue] = cookie.trim().split('=');
        if (cookieName === name) {
            return cookieValue;
        }
    }
    return null;
}

/**
 * Gets a value from localStorage.
 * @param {string} key
 * @returns {string|null}
 */
export function getLocalStorage(key) {
    return localStorage.getItem(key);
}

/**
 * Sets a value in localStorage.
 * @param {string} key
 * @param {string} value
 */
export function setLocalStorage(key, value) {
    localStorage.setItem(key, value);
}
