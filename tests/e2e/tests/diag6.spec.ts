import { test, expect } from '@playwright/test';

test('groupement - detailed error capture', async ({ page }) => {
    test.slow();

    // Inject error handler before anything loads
    await page.addInitScript(() => {
        window.addEventListener('error', (e) => {
            console.error('WINDOW_ERROR:', e.message, e.filename, e.lineno);
        });
        window.addEventListener('unhandledrejection', (e) => {
            console.error('UNHANDLED_REJECTION:', (e as any).reason?.message || String((e as any).reason));
        });
    });

    const allMsgs: string[] = [];
    page.on('console', msg => {
        const text = msg.text();
        if (text.includes('WINDOW_ERROR') || text.includes('UNHANDLED_REJECTION') || 
            text.includes('ExecError') || text.includes('Exception') || text.includes('error:') ||
            text.includes('[Refs]') || text.includes('[Cache]') || text.includes('FAIL') ||
            text.includes('Error') || text.includes('fail'))
            allMsgs.push(`[${msg.type()}] ${text}`);
    });
    page.on('pageerror', err => allMsgs.push('PAGEERROR: ' + err.message + '\nSTACK: ' + (err.stack || 'N/A')));

    await page.goto('/');
    await expect(page.locator('nav')).toBeVisible({ timeout: 90000 });
    await expect(page.locator('#monaco-editor-container .monaco-editor')).toBeVisible({ timeout: 60000 });
    await page.waitForTimeout(3000);

    // First run default code to verify basic execution works
    await page.getByRole('button', { name: /Exécuter/ }).click();
    const firstResult = page.locator('#results-table, .alert-danger');
    await expect(firstResult.first()).toBeVisible({ timeout: 120000 });
    console.log('First execution succeeded');
    allMsgs.push('=== First execution done ===');

    // Now try Groupement
    await page.getByRole('button', { name: 'Groupement par auteur' }).click();
    await page.waitForTimeout(1000);
    await page.getByRole('button', { name: /Exécuter/ }).click();
    
    // Wait 15 seconds for result or crash
    await page.waitForTimeout(15000);
    
    const blazorErr = await page.locator('#blazor-error-ui').isVisible();
    const table = await page.locator('#results-table').isVisible();
    const error = await page.locator('.alert-danger').isVisible();
    console.log(`After Groupement: BlazorError=${blazorErr} Table=${table} Error=${error}`);

    console.log('=== ALL MESSAGES ===');
    for (const m of allMsgs) console.log(m);
});
