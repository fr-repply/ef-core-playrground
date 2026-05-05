import { test, expect } from '@playwright/test';

test('groupement - capture pageerror', async ({ page }) => {
    test.slow();
    const pageErrors: string[] = [];
    const consoleErrors: string[] = [];
    page.on('pageerror', err => pageErrors.push(err.message + '\n' + (err.stack || '')));
    page.on('console', msg => {
        if (msg.type() === 'error' && !msg.text().includes('RAW SQL') && !msg.text().includes('Named param')
            && !msg.text().includes('VALUES') && !msg.text().includes('CREATE TABLE')
            && !msg.text().includes('CONSTRAINT') && !msg.text().includes('INSERT')
            && !msg.text().includes('integer') && !msg.text().includes('text NOT')
            && !msg.text().includes('timestamp') && !msg.text().includes('pg_get')
            && !msg.text().includes('setval') && !msg.text().includes('nextval')
            && !msg.text().includes('FOREIGN') && !msg.text().includes('PRIMARY')
            && !msg.text().includes('PostsPostId') && !msg.text().includes('GREATEST')
            && !msg.text().includes('SELECT MAX') && !msg.text().includes('false)'))
            consoleErrors.push(msg.text());
    });

    await page.goto('/');
    await expect(page.locator('nav')).toBeVisible({ timeout: 90000 });
    await expect(page.locator('#monaco-editor-container .monaco-editor')).toBeVisible({ timeout: 60000 });
    await page.waitForTimeout(2000);

    await page.getByRole('button', { name: 'Groupement par auteur' }).click();
    await page.waitForTimeout(500);
    await page.getByRole('button', { name: /Exécuter/ }).click();

    // Wait for Blazor error
    await page.waitForTimeout(5000);
    
    console.log('=== PAGE ERRORS ===');
    for (const e of pageErrors) console.log(e);
    console.log('=== CONSOLE ERRORS ===');  
    for (const e of consoleErrors) console.log(e);
    
    const blazorErr = await page.locator('#blazor-error-ui').isVisible();
    console.log('BlazorError:', blazorErr);
});
