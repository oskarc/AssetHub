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
 * Sets a cookie value.
 * @param {string} name - Cookie name.
 * @param {string} value - Cookie value (already encoded).
 * @param {number} maxAge - Max age in seconds.
 */
export function setCookie(name, value, maxAge) {
    document.cookie = `${name}=${value};path=/;max-age=${maxAge};samesite=lax`;
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
