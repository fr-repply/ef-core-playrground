import { test, expect } from '@playwright/test';

test('groupement steps trace', async ({ page }) => {
    test.slow();
    const steps: string[] = [];
    page.on('console', msg => {
        const text = msg.text();
        if (text.includes('[RunCompiledCode]') || text.includes('ExecError') || text.includes('WINDOW_ERROR') || text.includes('UNHANDLED'))
            steps.push(`[${msg.type()}] ${text}`);
    });
    page.on('pageerror', err => steps.push('PAGEERROR: ' + err.message));

    await page.addInitScript(() => {
        window.addEventListener('error', (e) => {
            console.error('WINDOW_ERROR:', e.message, e.filename, e.lineno);
        });
        window.addEventListener('unhandledrejection', (e) => {
            console.error('UNHANDLED_REJECTION:', (e as any).reason?.message || String((e as any).reason));
        });
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
    
    const blazorErr = await page.locator('#blazor-error-ui').isVisible();
    console.log('BlazorError:', blazorErr);
});
