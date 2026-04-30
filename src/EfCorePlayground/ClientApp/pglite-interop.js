// PGlite JavaScript interop — bridges EF Core to PGlite (in-browser PostgreSQL via WASM)
let db = null;
let PGliteModule = null;

window.pgliteInterop = {
    /**
     * Initialize a fresh PGlite instance. Destroys any existing one.
     */
    init: async function () {
        if (db) {
            try { await db.close(); } catch { }
            db = null;
        }

        try {
            if (!PGliteModule) {
                PGliteModule = await import('@electric-sql/pglite');
            }

            db = new PGliteModule.PGlite();
            await db.waitReady;
        } catch (e) {
            db = null;
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
        if (!db) throw new Error('PGlite not initialized. Call init() first.');

        const result = await db.query(sql, params || []);

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
        if (!db) throw new Error('PGlite not initialized. Call init() first.');
        await db.exec(sql);
        return { affectedRows: 0 };
    },

    /**
     * Close and destroy the PGlite instance.
     */
    close: async function () {
        if (db) {
            try { await db.close(); } catch { }
            db = null;
        }
    }
};
