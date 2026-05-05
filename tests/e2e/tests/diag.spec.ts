import { test, expect } from '@playwright/test';

test('diagnostic: default code execution', async ({ page }) => {
    test.slow();
    const consoleMessages: string[] = [];
    page.on('console', msg => {
        if (msg.type() === 'log' || msg.type() === 'error') {
            consoleMessages.push(`[${msg.type()}] ${msg.text()}`);
        }
    });
    page.on('pageerror', err => consoleMessages.push(`[PAGE_ERROR] ${err.message}`));

    await page.goto('/');
    await expect(page.locator('nav')).toBeVisible({ timeout: 90000 });
    await expect(page.locator('#monaco-editor-container .monaco-editor')).toBeVisible({ timeout: 60000 });
    
    // Execute default code
    await page.getByRole('button', { name: /Exécuter/ }).click();
    
    // Wait for execution to complete (spinner disappears)
    await page.waitForTimeout(30000);
    
    // Check what's visible
    const hasTable = await page.locator('#results-table').isVisible();
    const hasTreeView = await page.locator('.json-tree-viewer').isVisible();
    const hasError = await page.locator('.alert-danger').isVisible();
    const hasSpinner = await page.locator('.spinner-border').isVisible();
    const hasBlazorError = await page.locator('#blazor-error-ui').isVisible();
    const resultText = await page.locator('#results-panel').textContent().catch(() => 'N/A');
    
    console.log('=== VISIBILITY ===');
    console.log(`table: ${hasTable}, tree: ${hasTreeView}, error: ${hasError}, spinner: ${hasSpinner}, blazorError: ${hasBlazorError}`);
    console.log(`result text (first 200): ${resultText?.substring(0, 200)}`);
    
    console.log('=== CONSOLE MESSAGES ===');
    consoleMessages.forEach(m => console.log(m));
    
    // Always pass - this is diagnostic only
    expect(true).toBe(true);
});
