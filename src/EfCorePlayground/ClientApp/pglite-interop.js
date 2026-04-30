// PGlite JavaScript interop — bridges EF Core to PGlite (in-browser PostgreSQL via WASM)
// Uses static import to avoid Vite code-splitting issues with module-scoped variables.
import { PGlite } from '@electric-sql/pglite';

window.pgliteInterop = {
    /**
     * Initialize a fresh PGlite instance. Destroys any existing one.
     */
    init: async function () {
        if (window._pgliteDb) {
            try { await window._pgliteDb.close(); } catch { }
            window._pgliteDb = null;
        }

        try {
            window._pgliteDb = new PGlite();
            await window._pgliteDb.waitReady;
        } catch (e) {
            window._pgliteDb = null;
            throw new Error('PGlite initialization failed: ' + e.message);
        }
    },

    /**
     * Execute a SQL query and return results as {columns, rows, affectedRows}.
     * @param {string} sql - SQL query text
     * @param {any[]} params - Query parameters (positional: $1, $2, ...)
     * @returns {{ columns: string[], rows: any[][], affectedRows: number }}
     */
    query: async function (sql, params) {
        if (!window._pgliteDb) throw new Error('PGlite not initialized. Call init() first.');

        const result = await window._pgliteDb.query(sql, params || []);

        const columns = (result.fields || []).map(f => f.name);
        const rows = (result.rows || []).map(row =>
            columns.map(col => {
                const val = row[col];
                // Convert Date objects to ISO strings for JSON serialization
                if (val instanceof Date) return val.toISOString();
                return val;
            })
        );

        return {
            columns: columns,
            rows: rows,
            affectedRows: result.affectedRows || 0
        };
    },

    /**
     * Execute SQL statement(s) without returning result sets (DDL, INSERT, etc.).
     * Uses PGlite's exec() for multi-statement support.
     * @param {string} sql - SQL statement(s) to execute
     * @returns {{ affectedRows: number }}
     */
    exec: async function (sql) {
        if (!window._pgliteDb) throw new Error('PGlite not initialized. Call init() first.');
        await window._pgliteDb.exec(sql);
        return { affectedRows: 0 };
    },

    /**
     * Close and destroy the PGlite instance.
     */
    close: async function () {
        if (window._pgliteDb) {
            try { await window._pgliteDb.close(); } catch { }
            window._pgliteDb = null;
        }
    }
};
