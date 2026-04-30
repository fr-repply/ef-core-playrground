// Monaco Editor JS interop
window.monacoInterop = {
    editor: null,

    initialize: function (elementId, defaultCode, dotNetRef) {
        require.config({ paths: { 'vs': 'https://cdn.jsdelivr.net/npm/monaco-editor@0.52.2/min/vs' } });

        require(['vs/editor/editor.main'], function () {
            // Register C# language configuration
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
                suggest: {
                    showKeywords: true,
                    showSnippets: true
                },
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
