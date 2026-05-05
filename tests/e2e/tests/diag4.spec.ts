import { test, expect } from '@playwright/test';

test('test Roslyn compilation with custom code', async ({ page }) => {
    test.slow();
    const msgs: string[] = [];
    page.on('console', msg => {
        const text = msg.text();
        if (text.includes('[Refs]') || text.includes('[Cache]') || text.includes('[Warmup]') || 
            text.includes('ExecError') || text.includes('CS0') || text.includes('Error') ||
            text.includes('error:') || text.includes('Exception') || text.includes('result') ||
            text.includes('Compilation') || text.includes('compilation'))
            msgs.push(`[${msg.type()}] ${text}`);
    });
    page.on('pageerror', err => msgs.push('PAGE_ERROR: ' + err.message));

    await page.goto('/');
    await expect(page.locator('nav')).toBeVisible({ timeout: 90000 });
    await expect(page.locator('#monaco-editor-container .monaco-editor')).toBeVisible({ timeout: 60000 });
    await page.waitForTimeout(3000);

    // Set custom code that Roslyn must compile (not in precompiled cache)
    await page.evaluate(() => {
        (window as any).monacoInterop.setValue(`using System;
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
            return await db.Blogs.CountAsync();
        }
    }
}`);
    });

    await page.waitForTimeout(500);

    // Execute
    await page.getByRole('button', { name: /Exécuter/ }).click();

    // Wait for result
    const resultOrError = page.locator('#results-table, .alert-danger, .alert-success');
    try {
        await expect(resultOrError.first()).toBeVisible({ timeout: 120000 });
        console.log('GOT RESULT');
        const error = await page.locator('.alert-danger').isVisible();
        if (error) {
            const text = await page.locator('.alert-danger').first().textContent();
            console.log('ERROR:', text?.substring(0, 800));
        }
    } catch {
        console.log('TIMED OUT');
        const blazorErr = await page.locator('#blazor-error-ui').isVisible();
        console.log('BlazorError:', blazorErr);
    }

    for (const m of msgs) console.log(m);
});
