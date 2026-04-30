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

        require.config({ paths: { 'vs': base + 'vendor/monaco/min/vs' } });

        require(['vs/editor/editor.main'], function () {
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
    }
};
