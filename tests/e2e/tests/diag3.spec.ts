import { test, expect } from '@playwright/test';

test('capture ALL console messages', async ({ page }) => {
    test.slow();
    const all: string[] = [];
    page.on('console', msg => all.push(`[${msg.type()}] ${msg.text()}`));
    page.on('pageerror', err => all.push('PAGE_ERROR: ' + err.message + '\nSTACK: ' + err.stack));

    await page.goto('/');
    await expect(page.locator('nav')).toBeVisible({ timeout: 90000 });
    await expect(page.locator('#monaco-editor-container .monaco-editor')).toBeVisible({ timeout: 60000 });
    await page.waitForTimeout(3000);

    // Click Groupement par auteur
    await page.getByRole('button', { name: 'Groupement par auteur' }).click();
    await page.waitForTimeout(1000);

    // Mark the start
    all.push('=== EXECUTE CLICKED ===');
    await page.getByRole('button', { name: /Exécuter/ }).click();
    
    // Wait 10 seconds and capture everything
    await page.waitForTimeout(10000);

    // Print ALL messages
    for (const m of all) console.log(m);
});
