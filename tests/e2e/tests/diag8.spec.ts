import { test, expect } from '@playwright/test';

test('groupement as first execution', async ({ page }) => {
    test.slow();
    const msgs: string[] = [];
    page.on('console', msg => {
        const text = msg.text();
        if (!text.includes('RAW SQL') && !text.includes('Named param') && !text.includes('VALUES') &&
            !text.includes('CREATE TABLE') && !text.includes('CONSTRAINT') &&
            !text.includes('INSERT') && !text.includes('FOREIGN') && !text.includes('PRIMARY') &&
            !text.includes('PostsPostId') && !text.includes('GREATEST') && !text.includes('SELECT MAX') &&
            !text.includes('false)') && !text.includes('integer GEN') && !text.includes('text NOT') &&
            !text.includes('timestamp') && !text.includes('pg_get') && !text.includes('setval') &&
            !text.includes('nextval'))
            msgs.push(`[${msg.type()}] ${text}`);
    });
    page.on('pageerror', err => msgs.push('PAGEERROR: ' + err.message));

    await page.goto('/');
    await expect(page.locator('nav')).toBeVisible({ timeout: 90000 });
    await expect(page.locator('#monaco-editor-container .monaco-editor')).toBeVisible({ timeout: 60000 });
    await page.waitForTimeout(3000);

    // Load Groupement as the FIRST thing
    await page.getByRole('button', { name: 'Groupement par auteur' }).click();
    await page.waitForTimeout(1000);
    await page.getByRole('button', { name: /Exécuter/ }).click();

    // Wait for result
    const resultOrError = page.locator('#results-table, .alert-danger');
    try {
        await expect(resultOrError.first()).toBeVisible({ timeout: 60000 });
        const table = await page.locator('#results-table').isVisible();
        const error = await page.locator('.alert-danger').isVisible();
        console.log(`Table=${table} Error=${error}`);
        if (table) {
            const rows = await page.locator('#results-table tbody tr').count();
            console.log(`Rows: ${rows}`);
        }
        if (error) {
            const text = await page.locator('.alert-danger').first().textContent();
            console.log(`Error: ${text?.substring(0, 500)}`);
        }
    } catch {
        const blazorErr = await page.locator('#blazor-error-ui').isVisible();
        console.log(`TIMEOUT BlazorError=${blazorErr}`);
    }

    for (const m of msgs) console.log(m);
});
