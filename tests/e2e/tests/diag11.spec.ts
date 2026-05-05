import { test, expect } from '@playwright/test';

test('manually type groupement query', async ({ page }) => {
    test.slow();
    const msgs: string[] = [];
    page.on('console', msg => {
        const text = msg.text();
        if (text.includes('[RunCompiledCode]') || text.includes('[ExecuteCode]') || 
            text.includes('[ResultsPanel]') || text.includes('ExecError') || text.includes('CATCH'))
            msgs.push(`[${msg.type()}] ${text}`);
    });

    await page.goto('/');
    await expect(page.locator('nav')).toBeVisible({ timeout: 90000 });
    await expect(page.locator('#monaco-editor-container .monaco-editor')).toBeVisible({ timeout: 60000 });
    await page.waitForTimeout(3000);

    // Set the groupement code manually (not from example button)
    await page.evaluate(() => {
        (window as any).monacoInterop.setValue(`return await db.Authors
    .Select(a => new {
        Auteur = a.Name,
        NbPosts = a.Posts.Count(),
        NbBlogs = a.Posts.Select(p => p.BlogId).Distinct().Count()
    })
    .ToListAsync();`);
    });

    await page.waitForTimeout(500);
    await page.getByRole('button', { name: /Exécuter/ }).click();
    await page.waitForTimeout(20000);

    console.log('=== STEPS ===');
    for (const s of msgs) console.log(s);
    
    const blazorErr = await page.locator('#blazor-error-ui').isVisible();
    const table = await page.locator('#results-table').isVisible();
    const error = await page.locator('.alert-danger').isVisible();
    console.log(`BlazorError=${blazorErr} Table=${table} Error=${error}`);
});
