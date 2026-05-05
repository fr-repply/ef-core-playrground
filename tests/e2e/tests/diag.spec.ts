import { test, expect } from '@playwright/test';

test('diagnose groupement execution', async ({ page }) => {
    test.slow();
    const msgs: string[] = [];
    page.on('console', msg => {
        const text = msg.text();
        if (!text.includes('RAW SQL') && !text.includes('Named param') && 
            !text.includes('INSERT INTO') && !text.includes('VALUES') && 
            !text.includes('CREATE TABLE') && !text.includes('CONSTRAINT') &&
            !text.includes('integer GEN') && !text.includes('text NOT') && 
            !text.includes('timestamp') && !text.includes('pg_get_serial') &&
            !text.includes('nextval') && !text.includes('setval') &&
            !text.includes('FOREIGN KEY') && !text.includes('ON DELETE') &&
            !text.includes('PRIMARY KEY') && !text.includes('PostsPostId') &&
            !text.includes('TagsTagId'))
            msgs.push(`[${msg.type()}] ${text}`);
    });
    page.on('pageerror', err => msgs.push('PAGE_ERROR: ' + err.message));

    await page.goto('/');
    await expect(page.locator('nav')).toBeVisible({ timeout: 90000 });
    await expect(page.locator('#monaco-editor-container .monaco-editor')).toBeVisible({ timeout: 60000 });
    await page.waitForTimeout(3000);

    // Click the Groupement par auteur example
    await page.getByRole('button', { name: 'Groupement par auteur' }).click();
    await page.waitForTimeout(1000);

    // Execute
    await page.getByRole('button', { name: /Exécuter/ }).click();

    // Wait for result
    const resultOrError = page.locator('#results-table, .alert-danger');
    try {
        await expect(resultOrError.first()).toBeVisible({ timeout: 120000 });
        console.log('GOT RESULT');
    } catch {
        console.log('TIMED OUT - no result');
    }

    const blazorErr = await page.locator('#blazor-error-ui').isVisible();
    const spinner = await page.getByText('Exécution...').isVisible();
    const table = await page.locator('#results-table').isVisible();
    const error = await page.locator('.alert-danger').isVisible();
    console.log(`BlazorError=${blazorErr} Spinner=${spinner} Table=${table} Error=${error}`);

    for (const m of msgs) console.log(m);
});
