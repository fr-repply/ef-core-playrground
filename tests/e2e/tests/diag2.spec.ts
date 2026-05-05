import { test, expect } from '@playwright/test';

test('capture blazor error', async ({ page }) => {
    test.slow();
    const errors: string[] = [];
    page.on('console', msg => {
        const text = msg.text();
        if (text.includes('Exception') || text.includes('Error') || text.includes('error') || 
            text.includes('[Refs]') || text.includes('[Cache]') || text.includes('[Warmup]') ||
            text.includes('ExecError') || text.includes('CS0') || text.includes('PAGE_ERROR') ||
            text.includes('unhandled') || text.includes('fail') || text.includes('FAIL'))
            errors.push(`[${msg.type()}] ${text}`);
    });
    page.on('pageerror', err => errors.push('PAGE_ERROR: ' + err.message + '\n' + err.stack));

    await page.goto('/');
    await expect(page.locator('nav')).toBeVisible({ timeout: 90000 });
    await expect(page.locator('#monaco-editor-container .monaco-editor')).toBeVisible({ timeout: 60000 });
    await page.waitForTimeout(3000);

    // Click Groupement par auteur
    await page.getByRole('button', { name: 'Groupement par auteur' }).click();
    await page.waitForTimeout(1000);

    // Execute
    await page.getByRole('button', { name: /Exécuter/ }).click();
    
    // Wait until blazor-error-ui appears or we get a result
    for (let i = 0; i < 120; i++) {
        await page.waitForTimeout(1000);
        const blazorErr = await page.locator('#blazor-error-ui').isVisible();
        if (blazorErr) {
            console.log(`Blazor error appeared after ${i+1}s`);
            break;
        }
        const result = await page.locator('#results-table, .alert-danger').first().isVisible();
        if (result) {
            console.log(`Result appeared after ${i+1}s`);
            break;
        }
    }

    console.log('=== ALL ERROR MESSAGES ===');
    for (const m of errors) console.log(m);
});
