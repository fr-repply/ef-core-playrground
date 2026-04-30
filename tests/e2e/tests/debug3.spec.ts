import { test, expect } from '@playwright/test';
test('debug simple query', async ({ page }) => {
    test.slow();
    await page.goto('/');
    await expect(page.locator('nav')).toBeVisible({ timeout: 90000 });
    await expect(page.locator('#monaco-editor-container .monaco-editor')).toBeVisible({ timeout: 60000 });
    
    // Test with a simple query using collection navigation
    await page.evaluate(() => {
        const code = `using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using EntityFrameworkCore.Projectables;
using EfCorePlayground.Models;

namespace EfCorePlayground.UserCode
{
    public static class UserQuery
    {
        public static async Task<object?> Execute(PlaygroundDbContext db)
        {
            return await db.Authors
                .Select(a => new { a.Name, NbPosts = a.Posts.Count() })
                .ToListAsync();
        }
    }
}`;
        (window as any).monacoInterop.setValue(code);
    });
    
    await page.getByRole('button', { name: /Exécuter/ }).click();
    const resultOrError = page.locator('#results-table, .alert-danger');
    await expect(resultOrError.first()).toBeVisible({ timeout: 180000 });
    const errors = await page.locator('.alert-danger').allTextContents();
    if (errors.length > 0) console.log('ERRORS:', JSON.stringify(errors));
    const table = page.locator('#results-table');
    if (await table.isVisible()) console.log('SUCCESS: table visible');
});
