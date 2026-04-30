import { defineConfig } from '@playwright/test';

export default defineConfig({
    testDir: './tests',
    timeout: 120000,
    expect: {
        timeout: 30000,
    },
    fullyParallel: false,
    retries: 1,
    workers: 1,
    reporter: 'html',
    use: {
        baseURL: 'http://localhost:5000',
        trace: 'on-first-retry',
        headless: true,
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
        reuseExistingServer: !process.env.CI,
        timeout: 120000,
    },
});
