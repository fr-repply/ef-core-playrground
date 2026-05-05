import { test, expect } from '@playwright/test';

test('groupement trace all steps', async ({ page }) => {
    test.slow();
    const steps: string[] = [];
    page.on('console', msg => {
        const text = msg.text();
        if (text.includes('[RunCompiledCode]') || text.includes('[ExecuteCode]') || 
            text.includes('ExecError') || text.includes('CATCH'))
            steps.push(`[${msg.type()}] ${text}`);
    });

    await page.goto('/');
    await expect(page.locator('nav')).toBeVisible({ timeout: 90000 });
    await expect(page.locator('#monaco-editor-container .monaco-editor')).toBeVisible({ timeout: 60000 });
    await page.waitForTimeout(3000);

    await page.getByRole('button', { name: 'Groupement par auteur' }).click();
    await page.waitForTimeout(500);
    await page.getByRole('button', { name: /Exécuter/ }).click();
    await page.waitForTimeout(15000);

    console.log('=== STEPS ===');
    for (const s of steps) console.log(s);
    console.log('BlazorError:', await page.locator('#blazor-error-ui').isVisible());
});
