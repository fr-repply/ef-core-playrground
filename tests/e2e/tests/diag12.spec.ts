import { test, expect } from '@playwright/test';

test('test which examples crash', async ({ page }) => {
    test.slow();
    const msgs: string[] = [];
    page.on('console', msg => {
        const text = msg.text();
        if (text.includes('[ExecuteCode]') || text.includes('[ResultsPanel]'))
            msgs.push(`[${msg.type()}] ${text}`);
    });

    await page.goto('/');
    await expect(page.locator('nav')).toBeVisible({ timeout: 90000 });
    await expect(page.locator('#monaco-editor-container .monaco-editor')).toBeVisible({ timeout: 60000 });
    await page.waitForTimeout(3000);

    // Test 1: Default code
    await page.getByRole('button', { name: /Exécuter/ }).click();
    await page.waitForTimeout(8000);
    const defaultBlazorErr = await page.locator('#blazor-error-ui').isVisible();
    console.log('Default code: BlazorError=' + defaultBlazorErr);
    if (defaultBlazorErr) return;

    // Test 2: Lister tous les blogs
    await page.getByRole('button', { name: 'Lister tous les blogs' }).click();
    await page.waitForTimeout(500);
    await page.getByRole('button', { name: /Exécuter/ }).click();
    await page.waitForTimeout(8000);
    console.log('Lister blogs: BlazorError=' + await page.locator('#blazor-error-ui').isVisible());
    if (await page.locator('#blazor-error-ui').isVisible()) return;

    // Test 3: Blogs avec rating
    await page.getByRole('button', { name: 'Blogs avec rating' }).click();
    await page.waitForTimeout(500);
    await page.getByRole('button', { name: /Exécuter/ }).click();
    await page.waitForTimeout(8000);
    console.log('Blogs rating: BlazorError=' + await page.locator('#blazor-error-ui').isVisible());
    if (await page.locator('#blazor-error-ui').isVisible()) return;

    // Test 4: Groupement
    await page.getByRole('button', { name: 'Groupement par auteur' }).click();
    await page.waitForTimeout(500);
    await page.getByRole('button', { name: /Exécuter/ }).click();
    await page.waitForTimeout(8000);
    console.log('Groupement: BlazorError=' + await page.locator('#blazor-error-ui').isVisible());
    
    for (const m of msgs) console.log(m);
});
