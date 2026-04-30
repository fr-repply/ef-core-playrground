// Monaco Editor JS interop — loaded via Vite bundle
// Monaco AMD loader is served from vendor/monaco/min/vs/loader.js (copied by vite-plugin-static-copy)
window.monacoInterop = {
    editor: null,

    initialize: function (elementId, defaultCode, dotNetRef) {
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

    getValue: function () {
        if (window.monacoInterop.editor) {
            return window.monacoInterop.editor.getValue();
        }
        return '';
    },

    setValue: function (value) {
        if (window.monacoInterop.editor) {
            window.monacoInterop.editor.setValue(value);
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
