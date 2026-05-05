import { test, expect } from '@playwright/test';

test('second execution with different code', async ({ page }) => {
    test.slow();

    await page.goto('/');
    await expect(page.locator('nav')).toBeVisible({ timeout: 90000 });
    await expect(page.locator('#monaco-editor-container .monaco-editor')).toBeVisible({ timeout: 60000 });
    await page.waitForTimeout(3000);

    // Override to disable SQL viewer
    await page.evaluate(() => {
        (window as any).monacoInterop.disposeSqlViewer = () => {};
        (window as any).monacoInterop.createSqlViewer = () => {};
    });

    // First execution (default code)
    await page.getByRole('button', { name: /Exécuter/ }).click();
    await page.waitForTimeout(8000);
    console.log('First: BlazorError=' + await page.locator('#blazor-error-ui').isVisible());
    if (await page.locator('#blazor-error-ui').isVisible()) return;

    // Load different example and execute
    await page.getByRole('button', { name: 'Lister tous les blogs' }).click();
    await page.waitForTimeout(500);
    await page.getByRole('button', { name: /Exécuter/ }).click();
    await page.waitForTimeout(8000);
    console.log('Different code: BlazorError=' + await page.locator('#blazor-error-ui').isVisible());
});
