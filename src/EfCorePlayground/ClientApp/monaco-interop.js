// Monaco Editor JS interop — loaded via Vite bundle
// Monaco AMD loader is served from vendor/monaco/min/vs/loader.js (copied by vite-plugin-static-copy)
window.monacoInterop = {
    editor: null,
    prefixLines: 0,
    suffixLines: 0,
    _decorationCollection: null,

    initialize: function (elementId, defaultCode, dotNetRef, prefixLines, suffixLines) {
        window.monacoInterop.prefixLines = prefixLines || 0;
        window.monacoInterop.suffixLines = suffixLines || 0;

        // Inject CSS for boilerplate line styling
        var style = document.createElement('style');
        style.textContent =
            '.boilerplate-text { opacity: 0.45 !important; }';
        document.head.appendChild(style);

        // Determine the base path for Monaco files (works with <base href="/...">)
        var base = document.querySelector('base')?.getAttribute('href') || '/';
        if (!base.endsWith('/')) base += '/';

        // Use window.require (from Monaco AMD loader) since this runs as an ES module
        window.require.config({ paths: { 'vs': base + 'vendor/monaco/min/vs' } });

        window.require(['vs/editor/editor.main'], function () {
            monaco.languages.register({ id: 'csharp' });

            window.monacoInterop.editor = monaco.editor.create(document.getElementById(elementId), {
                value: defaultCode,
                language: 'csharp',
                theme: 'vs-dark',
                fontSize: 14,
                fontFamily: "'Cascadia Code', 'Fira Code', Consolas, 'Courier New', monospace",
                minimap: { enabled: false },
                automaticLayout: true,
                scrollBeyondLastLine: false,
                lineNumbers: 'on',
                roundedSelection: true,
                padding: { top: 10 },
                suggest: { showKeywords: true, showSnippets: true },
                tabSize: 4,
                wordWrap: 'on'
            });

            // Apply boilerplate decorations
            window.monacoInterop._applyBoilerplateDecorations();

            // Ctrl+Enter to execute
            window.monacoInterop.editor.addAction({
                id: 'execute-code',
                label: 'Execute Code',
                keybindings: [monaco.KeyMod.CtrlCmd | monaco.KeyCode.Enter],
                run: function () {
                    dotNetRef.invokeMethodAsync('OnExecuteFromEditor');
                }
            });
        });
    },

    _applyBoilerplateDecorations: function () {
        if (!window.monacoInterop.editor) return;
        var model = window.monacoInterop.editor.getModel();
        var totalLines = model.getLineCount();
        var decorations = [];
        var minExpectedLines = window.monacoInterop.prefixLines + window.monacoInterop.suffixLines + 1;

        // Only apply decorations if the code has enough lines (i.e. looks like a full template)
        if (totalLines >= minExpectedLines) {
            // Dim prefix lines (usings, namespace, class, method signature)
            if (window.monacoInterop.prefixLines > 0) {
                decorations.push({
                    range: new monaco.Range(1, 1, window.monacoInterop.prefixLines, model.getLineMaxColumn(window.monacoInterop.prefixLines)),
                    options: {
                        isWholeLine: true,
                        inlineClassName: 'boilerplate-text'
                    }
                });
            }

            // Dim suffix lines (closing braces)
            if (window.monacoInterop.suffixLines > 0) {
                var suffixStart = totalLines - window.monacoInterop.suffixLines + 1;
                decorations.push({
                    range: new monaco.Range(suffixStart, 1, totalLines, model.getLineMaxColumn(totalLines)),
                    options: {
                        isWholeLine: true,
                        inlineClassName: 'boilerplate-text'
                    }
                });
            }
        }

        // Replace existing decorations
        if (window.monacoInterop._decorationCollection) {
            window.monacoInterop._decorationCollection.clear();
        }
        window.monacoInterop._decorationCollection =
            window.monacoInterop.editor.createDecorationsCollection(decorations);
    },

    getValue: function () {
        if (window.monacoInterop.editor) {
            return window.monacoInterop.editor.getValue();
        }
        return '';
    },

    setValue: function (value) {
        if (window.monacoInterop.editor) {
            window.monacoInterop.editor.setValue(value);
            window.monacoInterop._applyBoilerplateDecorations();
        }
    },

    setMarkers: function (markers) {
        if (window.monacoInterop.editor) {
            if (!Array.isArray(markers)) markers = [markers];
            var model = window.monacoInterop.editor.getModel();
            monaco.editor.setModelMarkers(model, 'compilation', markers.map(function (m) {
                return {
                    severity: monaco.MarkerSeverity.Error,
                    startLineNumber: m.line,
                    startColumn: m.column,
                    endLineNumber: m.line,
                    endColumn: m.column + 1,
                    message: m.message
                };
            }));
        }
    },

    clearMarkers: function () {
        if (window.monacoInterop.editor) {
            var model = window.monacoInterop.editor.getModel();
            monaco.editor.setModelMarkers(model, 'compilation', []);
        }
    },

    /**
     * Creates (or updates) a read-only Monaco editor for SQL display.
     * Reuses the same instance if the container already has an editor.
     */
    createSqlViewer: function (elementId, sql) {
        var container = document.getElementById(elementId);
        if (!container) return;

        // Register SQL language if not already done
        if (!window.monacoInterop._sqlLangRegistered) {
            window.require(['vs/editor/editor.main'], function () {
                window.monacoInterop._initSqlViewer(container, sql);
            });
        } else {
            window.monacoInterop._initSqlViewer(container, sql);
        }
    },

    _sqlEditor: null,
    _sqlLangRegistered: false,
    _sqlHighlightDecoration: null,

    /**
     * Highlights lines [startLine, endLine] (1-based) in the SQL viewer.
     * Called on mouseenter of a query tag badge.
     */
    highlightSqlRange: function (startLine, endLine) {
        var editor = window.monacoInterop._sqlEditor;
        if (!editor) return;
        var model = editor.getModel();
        if (!model) return;
        var lineCount = model.getLineCount();
        var safeEnd = Math.min(endLine, lineCount);
        var endCol = model.getLineMaxColumn(safeEnd);

        if (window.monacoInterop._sqlHighlightDecoration) {
            window.monacoInterop._sqlHighlightDecoration.clear();
        }
        window.monacoInterop._sqlHighlightDecoration = editor.createDecorationsCollection([{
            range: new monaco.Range(startLine, 1, safeEnd, endCol),
            options: {
                isWholeLine: true,
                className: 'sql-highlight-range',
                overviewRuler: { color: '#569cd6', position: monaco.editor.OverviewRulerLane.Full }
            }
        }]);
        editor.revealLinesInCenterIfOutsideViewport(startLine, safeEnd);
    },

    /**
     * Clears the SQL highlight decoration. Called on mouseleave of a query tag badge.
     */
    clearSqlHighlight: function () {
        if (window.monacoInterop._sqlHighlightDecoration) {
            window.monacoInterop._sqlHighlightDecoration.clear();
            window.monacoInterop._sqlHighlightDecoration = null;
        }
    },

    _initSqlViewer: function (container, sql) {
        if (!window.monacoInterop._sqlLangRegistered) {
            // Register a basic SQL language with syntax highlighting
            monaco.languages.register({ id: 'sql' });
            monaco.languages.setMonarchTokensProvider('sql', {
                ignoreCase: true,
                keywords: [
                    'SELECT', 'FROM', 'WHERE', 'AND', 'OR', 'NOT', 'IN', 'EXISTS',
                    'INSERT', 'INTO', 'VALUES', 'UPDATE', 'SET', 'DELETE',
                    'CREATE', 'TABLE', 'ALTER', 'DROP', 'INDEX', 'VIEW',
                    'JOIN', 'INNER', 'LEFT', 'RIGHT', 'OUTER', 'CROSS', 'LATERAL',
                    'ON', 'AS', 'IS', 'NULL', 'TRUE', 'FALSE',
                    'ORDER', 'BY', 'ASC', 'DESC', 'GROUP', 'HAVING',
                    'LIMIT', 'OFFSET', 'DISTINCT', 'ALL', 'UNION', 'EXCEPT', 'INTERSECT',
                    'CASE', 'WHEN', 'THEN', 'ELSE', 'END',
                    'COUNT', 'SUM', 'AVG', 'MIN', 'MAX',
                    'LIKE', 'BETWEEN', 'CAST', 'COALESCE',
                    'PRIMARY', 'KEY', 'FOREIGN', 'REFERENCES', 'CONSTRAINT',
                    'IF', 'RETURNING', 'WITH', 'RECURSIVE', 'OVER', 'PARTITION',
                    'ROW_NUMBER', 'RANK', 'DENSE_RANK', 'LAG', 'LEAD',
                    'SUBQUERY', 'ANY', 'SOME', 'APPLY', 'FETCH', 'NEXT', 'ROWS', 'ONLY',
                    'TAKE', 'SKIP', 'TOP', 'PERCENT', 'TIES'
                ],
                typeKeywords: [
                    'integer', 'int', 'bigint', 'smallint', 'text', 'varchar',
                    'boolean', 'bool', 'timestamp', 'date', 'time', 'numeric',
                    'decimal', 'real', 'double', 'precision', 'serial', 'uuid',
                    'character', 'varying', 'bytea', 'json', 'jsonb'
                ],
                operators: ['=', '>', '<', '>=', '<=', '<>', '!=', '||', '::', '+', '-', '*', '/'],
                tokenizer: {
                    root: [
                        [/--.*$/, 'comment'],
                        [/\/\*/, 'comment', '@comment'],
                        [/'[^']*'/, 'string'],
                        [/\$\d+/, 'variable'],
                        [/"[^"]*"/, 'identifier'],
                        [/\d+(\.\d+)?/, 'number'],
                        [/[a-zA-Z_]\w*/, {
                            cases: {
                                '@keywords': 'keyword',
                                '@typeKeywords': 'type',
                                '@default': 'identifier'
                            }
                        }],
                        [/[=><:!|+\-*/]/, 'operator'],
                        [/[,;.()\[\]]/, 'delimiter']
                    ],
                    comment: [
                        [/\*\//, 'comment', '@pop'],
                        [/./, 'comment']
                    ]
                }
            });
            window.monacoInterop._sqlLangRegistered = true;
        }

        if (window.monacoInterop._sqlEditor) {
            // Reuse existing editor — just update value
            window.monacoInterop._sqlEditor.setValue(sql);
            return;
        }

        window.monacoInterop._sqlEditor = monaco.editor.create(container, {
            value: sql,
            language: 'sql',
            theme: 'vs-dark',
            fontSize: 12,
            fontFamily: "'Cascadia Code', 'Fira Code', Consolas, 'Courier New', monospace",
            minimap: { enabled: false },
            automaticLayout: true,
            scrollBeyondLastLine: false,
            readOnly: true,
            lineNumbers: 'off',
            renderLineHighlight: 'none',
            padding: { top: 6, bottom: 6 },
            wordWrap: 'on',
            domReadOnly: true,
            contextmenu: false,
            scrollbar: { vertical: 'auto', horizontal: 'auto' },
            overviewRulerLanes: 0,
            hideCursorInOverviewRuler: true,
            overviewRulerBorder: false,
            folding: false,
            glyphMargin: false
        });
    },

    /**
     * Disposes the SQL viewer editor instance.
     */
    disposeSqlViewer: function () {
        if (window.monacoInterop._sqlEditor) {
            window.monacoInterop._sqlEditor.dispose();
            window.monacoInterop._sqlEditor = null;
        }
    }
};
