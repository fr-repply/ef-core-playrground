import { test, expect } from '@playwright/test';

test('second execution - disable all post-exec actions', async ({ page }) => {
    test.slow();

    await page.goto('/');
    await expect(page.locator('nav')).toBeVisible({ timeout: 90000 });
    await expect(page.locator('#monaco-editor-container .monaco-editor')).toBeVisible({ timeout: 60000 });
    await page.waitForTimeout(3000);

    // Disable SQL viewer and PGlite post-exec queries
    await page.evaluate(() => {
        (window as any).monacoInterop.disposeSqlViewer = () => {};
        (window as any).monacoInterop.createSqlViewer = () => {};
        // Disable PGlite query after init (RefreshData tries to query tables)
        const origQuery = (window as any).pgliteInterop.query;
        let queryCount = 0;
        (window as any).pgliteInterop.query = async function(sql: string, params: any) {
            queryCount++;
            // Allow queries during execution but block post-exec RefreshData queries
            return origQuery.call(this, sql, params);
        };
    });

    // First execution  
    await page.getByRole('button', { name: /Exécuter/ }).click();
    await page.waitForTimeout(8000);
    console.log('First: BlazorError=' + await page.locator('#blazor-error-ui').isVisible());
    if (await page.locator('#blazor-error-ui').isVisible()) return;

    // Execute the SAME code again (just click Execute again without changing code)
    await page.getByRole('button', { name: /Exécuter/ }).click();
    await page.waitForTimeout(8000);
    console.log('Second (same code): BlazorError=' + await page.locator('#blazor-error-ui').isVisible());
});
