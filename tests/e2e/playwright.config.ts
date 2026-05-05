import { defineConfig } from '@playwright/test';

export default defineConfig({
    testDir: './tests',
    fullyParallel: false,
    retries: 1,
    workers: 1,
    reporter: 'html',
    use: {
        baseURL: 'http://localhost:5000',
        trace: 'on-first-retry',
        headless: true,
        // Use a fresh browser context per test to avoid browser cache issues
        // (stale precompiled DLLs, cached ref assemblies, localStorage)
        storageState: undefined,
    },
    projects: [
        {
            name: 'chromium',
            use: { browserName: 'chromium' },
        },
    ],
    webServer: {
        command: 'dotnet run --project ../../src/EfCorePlayground --urls http://localhost:5000',
        url: 'http://localhost:5000',
        reuseExistingServer: true,
        timeout: 120000,
    },
});
