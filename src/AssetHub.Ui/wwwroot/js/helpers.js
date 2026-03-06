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
    a.remove();
}

/**
 * Safely downloads a file by first checking the URL for errors.
 * Returns null on success, or an error message string if download failed.
 * @param {string} url - The URL to download from.
 * @param {object|null} headers - Optional headers to include.
 * @returns {Promise<string|null>} null on success, error message on failure.
 */
export async function downloadWithErrorCheck(url, headers) {
    try {
        // Use fetch with redirect: 'manual' to intercept redirects
        const response = await fetch(url, {
            method: 'GET',
            credentials: 'same-origin',
            redirect: 'manual',
            headers: headers || {}
        });

        // 3xx redirect means success - the server returned a presigned URL
        if (response.type === 'opaqueredirect' || (response.status >= 300 && response.status < 400)) {
            // Let the browser handle the redirect normally
            downloadViaAnchor(url);
            return null;
        }

        // 2xx with redirect in body (some APIs return redirect URL in response)
        if (response.ok) {
            const contentType = response.headers.get('content-type') || '';
            if (contentType.includes('application/json')) {
                const data = await response.json();
                if (data.url || data.redirectUrl || data.downloadUrl) {
                    downloadViaAnchor(data.url || data.redirectUrl || data.downloadUrl);
                    return null;
                }
            }
            // Direct 200 response - download directly
            downloadViaAnchor(url);
            return null;
        }

        // Error response - extract message from JSON
        const contentType = response.headers.get('content-type') || '';
        if (contentType.includes('application/json')) {
            const errorData = await response.json();
            return errorData.message || errorData.error || `Download failed (${response.status})`;
        }

        return `Download failed (${response.status})`;
    } catch (ex) {
        return ex.message || 'Download failed';
    }
}

function zipResult(success, error) {
    return JSON.stringify(error ? { success: false, error } : { success: true });
}

async function enqueueZipBuild(enqueueUrl, headers) {
    const resp = await fetch(enqueueUrl, {
        method: 'POST',
        credentials: 'same-origin',
        headers: { 'Content-Type': 'application/json', ...(headers || {}) }
    });

    if (!resp.ok) {
        const errText = await resp.text();
        return { error: `Failed to start download: ${resp.status} ${errText}` };
    }

    const data = await resp.json();
    return data.statusUrl ? { statusUrl: data.statusUrl } : { error: 'No status URL returned' };
}

async function handlePollStatus(status, dotNetRef) {
    if (dotNetRef) {
        try {
            await dotNetRef.invokeMethodAsync('UpdateZipProgress', status.status, status.fileName || '');
        } catch { /* Blazor ref may be disposed */ }
    }

    if (status.status === 'completed') {
        if (status.downloadUrl) {
            downloadViaAnchor(status.downloadUrl);
            return zipResult(true);
        }
        return zipResult(false, 'Completed but no download URL');
    }

    if (status.status === 'failed' || status.status === 'expired') {
        return zipResult(false, status.error || 'Download failed');
    }

    return null; // still in progress
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
        const enqueue = await enqueueZipBuild(enqueueUrl, headers);
        if (enqueue.error) return zipResult(false, enqueue.error);

        const pollHeaders = headers || {};
        const maxAttempts = 360; // 30 minutes max (5s intervals)
        for (let i = 0; i < maxAttempts; i++) {
            await new Promise(resolve => setTimeout(resolve, 5000));

            const pollResp = await fetch(enqueue.statusUrl, {
                method: 'GET',
                credentials: 'same-origin',
                headers: pollHeaders
            });

            if (!pollResp.ok) return zipResult(false, `Status check failed: ${pollResp.status}`);

            const result = await handlePollStatus(await pollResp.json(), dotNetRef);
            if (result) return result;
        }

        return zipResult(false, 'Download timed out');
    } catch (ex) {
        return zipResult(false, ex.message || 'Unknown error');
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
