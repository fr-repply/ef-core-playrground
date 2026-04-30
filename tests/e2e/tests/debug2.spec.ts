import { test, expect } from '@playwright/test';
test('debug groupement', async ({ page }) => {
    test.slow();
    await page.goto('/');
    await expect(page.locator('nav')).toBeVisible({ timeout: 90000 });
    await expect(page.locator('#monaco-editor-container .monaco-editor')).toBeVisible({ timeout: 60000 });
    await page.getByText('Schéma').click();
    await page.getByRole('button', { name: 'Groupement par auteur' }).click();
    await page.getByRole('button', { name: /Exécuter/ }).click();
    const resultOrError = page.locator('#results-table, .alert-danger');
    await expect(resultOrError.first()).toBeVisible({ timeout: 180000 });
    const errors = await page.locator('.alert-danger').allTextContents();
    if (errors.length > 0) console.log('ERRORS:', JSON.stringify(errors));
    const table = page.locator('#results-table');
    if (await table.isVisible()) console.log('TABLE visible');
    const output = await page.locator('#results-panel').textContent();
    console.log('PANEL:', output?.substring(0, 500));
});
