// EF Core Playground - Vue.js Application
// Wait for both Blazor and Vue to be ready
function waitForBlazor() {
    return new Promise((resolve) => {
        const check = () => {
            if (typeof DotNet !== 'undefined' && DotNet.invokeMethodAsync) {
                resolve();
            } else {
                setTimeout(check, 100);
            }
        };
        check();
    });
}

async function initApp() {
    // Wait for Blazor/.NET WASM to be ready
    await waitForBlazor();

    const { createApp, ref, onMounted } = Vue;

    const app = createApp({
        setup() {
            const code = ref('');
            const isExecuting = ref(false);
            const result = ref(null);
            const executionTime = ref(null);
            const schema = ref(null);
            const examples = ref([]);
            const showSchema = ref(true);

            const loadDefaults = async () => {
                try {
                    const defaultCode = await DotNet.invokeMethodAsync('EfCorePlayground', 'GetDefaultCode');
                    code.value = defaultCode;

                    const schemaJson = await DotNet.invokeMethodAsync('EfCorePlayground', 'GetSchemaInfo');
                    schema.value = JSON.parse(schemaJson);

                    const examplesJson = await DotNet.invokeMethodAsync('EfCorePlayground', 'GetExamples');
                    examples.value = JSON.parse(examplesJson);
                } catch (e) {
                    console.error('Failed to load defaults:', e);
                }
            };

            const executeCode = async () => {
                if (isExecuting.value) return;
                isExecuting.value = true;
                result.value = null;
                executionTime.value = null;

                const startTime = performance.now();
                try {
                    const resultJson = await DotNet.invokeMethodAsync('EfCorePlayground', 'ExecuteCode', code.value);
                    result.value = JSON.parse(resultJson);
                } catch (e) {
                    result.value = {
                        success: false,
                        output: 'Error communicating with .NET runtime: ' + e.message,
                        errors: [{ id: 'JS_ERROR', message: e.message, line: 0, column: 0 }]
                    };
                }
                executionTime.value = Math.round(performance.now() - startTime);
                isExecuting.value = false;
            };

            const loadExample = (example) => {
                code.value = example.code;
            };

            const resetCode = async () => {
                try {
                    const defaultCode = await DotNet.invokeMethodAsync('EfCorePlayground', 'GetDefaultCode');
                    code.value = defaultCode;
                    result.value = null;
                    executionTime.value = null;
                } catch (e) {
                    console.error('Failed to reset code:', e);
                }
            };

            const formatValue = (value) => {
                if (value === null || value === undefined) return 'null';
                if (typeof value === 'object') return JSON.stringify(value);
                return String(value);
            };

            onMounted(() => {
                loadDefaults();
            });

            return {
                code,
                isExecuting,
                result,
                executionTime,
                schema,
                examples,
                showSchema,
                executeCode,
                loadExample,
                resetCode,
                formatValue
            };
        }
    });

    app.mount('#vue-app');
}

// Start initialization when DOM is ready
if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initApp);
} else {
    initApp();
}
