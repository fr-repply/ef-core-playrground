// LocalStorage-based compilation cache interop
// All entries are stored under the 'efpg:' key prefix to avoid collisions.

window.efCacheInterop = {
    /**
     * Persist a compiled assembly (base64-encoded bytes) for a given code hash.
     * Silently ignores quota / unavailable storage errors.
     */
    save: function (hash, base64) {
        try {
            localStorage.setItem('efpg:' + hash, base64);
        } catch (e) {
            console.warn('[efCacheInterop] save failed:', e.message);
        }
    },

    /**
     * Retrieve a previously saved assembly for a given hash, or null if absent.
     */
    load: function (hash) {
        return localStorage.getItem('efpg:' + hash);
    },

    /**
     * Return all cached entries as a plain object { hash: base64 }.
     * Used at startup to bulk-restore the in-memory cache.
     */
    loadAll: function () {
        const result = {};
        for (let i = 0; i < localStorage.length; i++) {
            const k = localStorage.key(i);
            if (k && k.startsWith('efpg:')) {
                result[k.substring(5)] = localStorage.getItem(k);
            }
        }
        return result;
    },

    /** Remove a single entry from the persistent cache. */
    remove: function (hash) {
        localStorage.removeItem('efpg:' + hash);
    },

    /** Wipe all entries belonging to this app from localStorage. */
    clear: function () {
        const keys = [];
        for (let i = 0; i < localStorage.length; i++) {
            const k = localStorage.key(i);
            if (k && k.startsWith('efpg:')) keys.push(k);
        }
        keys.forEach(k => localStorage.removeItem(k));
    }
};

