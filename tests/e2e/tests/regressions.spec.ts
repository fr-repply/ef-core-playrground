import { test, expect, Page } from '@playwright/test';

// Regressions found while replaying the snippets of an internal EF Core training course.
// Each test runs the real scenario end to end: Roslyn in WASM, EF Core, PGlite.

/** Same template as CodeExecutionService.BuildFullCode(): the editor holds the full code. */
function fullCode(body: string): string {
    return [
        'using System;',
        'using System.Collections.Generic;',
        'using System.Linq;',
        'using System.Threading.Tasks;',
        'using Microsoft.EntityFrameworkCore;',
        'using EntityFrameworkCore.Projectables;',
        'using EfCorePlayground.Models;',
        '',
        'namespace EfCorePlayground.UserCode',
        '{',
        '    public static class UserQuery',
        '    {',
        '        public static async Task<object?> Execute(PlaygroundDbContext db)',
        '        {',
        body,
        '        }',
        '    }',
        '}',
    ].join('\n');
}

/** Runs the code and returns the results table, after checking that no error is shown. */
async function runAndShowTable(page: Page, code: string) {
    await page.evaluate((c) => (window as any).monacoInterop.setValue(c), code);
    await page.getByRole('button', { name: /Exécuter/ }).click();

    const resultOrError = page.locator('.json-tree-viewer, #results-table, .alert-danger');
    await expect(resultOrError.first()).toBeVisible({ timeout: 180000 });
    await expect(page.locator('.alert-danger')).toHaveCount(0);

    // Tree view is the default when a rich JSON is available
    const tableToggle = page.getByRole('button', { name: 'Tableau' });
    if (await tableToggle.isVisible()) {
        await tableToggle.click();
    }

    const table = page.locator('#results-table');
    await expect(table).toBeVisible();
    return table;
}

test.describe('EF Core Playground - Régressions', () => {

    test.beforeEach(async ({ page }) => {
        await page.goto('/');
        await expect(page.locator('nav')).toBeVisible({ timeout: 90000 });
        await expect(page.locator('#monaco-editor-container .monaco-editor')).toBeVisible({ timeout: 60000 });
    });

    test('pgliteInterop.query keeps two columns with the same name', async ({ page }) => {
        const result = await page.evaluate(async () => {
            const interop = (window as any).pgliteInterop;
            await interop.init();
            return interop.query(`SELECT 'Le Blog .NET' AS "Name", 'Alice Martin' AS "Name"`, []);
        });

        expect(result.columns).toEqual(['Name', 'Name']);
        expect(result.rows).toEqual([['Le Blog .NET', 'Alice Martin']]);
    });

    test('projection of two homonymous columns shows both values', async ({ page }) => {
        test.slow();

        // With a collection in the projection, EF Core no longer aliases the columns:
        // b."Name", a."Name" and t."Name" come back from PostgreSQL under the same name
        const table = await runAndShowTable(page, fullCode(`
            return await db.Posts
                .OrderByDescending(p => p.PublishedDate)
                .Select(p => new
                {
                    p.Title,
                    Blog = p.Blog.Name,
                    Auteur = p.Author.Name,
                    Tags = string.Join(", ", p.Tags.Select(t => t.Name)),
                })
                .ToListAsync();`));

        const firstRow = table.locator('tbody tr').first();
        await expect(firstRow).toContainText('APIs REST avec .NET');
        await expect(firstRow).toContainText('Web Dev Tips');
        await expect(firstRow).toContainText('Bob Dupont');
        await expect(firstRow).toContainText('C#, Web');
    });

    test('[Projectable] quoted in a comment does not trigger the source generator', async ({ page }) => {
        test.slow();

        const table = await runAndShowTable(page, fullCode(`
            // Blog.IsPopular et Blog.PostCount sont des propriétés [Projectable] du modèle.
            return await db.Blogs
                .Where(b => b.IsPopular)
                .OrderByDescending(b => b.PostCount)
                .Select(b => new { b.Name, b.Rating, b.PostCount })
                .ToListAsync();`));

        await expect(table.locator('tbody tr')).toHaveCount(3);
    });

    test('list of scalars is shown in a single Value column', async ({ page }) => {
        test.slow();

        const table = await runAndShowTable(page, fullCode(`
            return await db.Posts
                .OrderBy(p => p.PostId)
                .Select(p => p.Title)
                .ToListAsync();`));

        await expect(table.locator('thead')).toContainText('Value');
        await expect(table.locator('tbody tr')).toHaveCount(10);
        await expect(table.locator('tbody tr').first()).toContainText('Introduction à EF Core');
    });

    test('inserting a graph renumbers the parameters of each batched statement', async ({ page }) => {
        test.slow();

        const table = await runAndShowTable(page, fullCode(`
            var auteur = new Author { Name = "Denis Nouveau", Email = "denis@example.com", Bio = "Invité" };
            var blog = new Blog
            {
                Name = "Le Blog du Graphe",
                Url = "https://graphe.example.com",
                Rating = 4,
                CreatedAt = DateTime.UtcNow,
                Posts =
                {
                    new Post { Title = "Premier post", Content = "...", PublishedDate = DateTime.UtcNow, Author = auteur },
                    new Post { Title = "Deuxième post", Content = "...", PublishedDate = DateTime.UtcNow, Author = auteur },
                },
            };

            db.Blogs.Add(blog);
            await db.SaveChangesAsync();

            return await db.Posts
                .Where(p => p.BlogId == blog.BlogId)
                .OrderBy(p => p.PostId)
                .Select(p => new { p.Title, Blog = p.Blog.Name, Auteur = p.Author.Name })
                .ToListAsync();`));

        await expect(table.locator('tbody tr')).toHaveCount(2);
        await expect(table.locator('tbody tr').first()).toContainText('Le Blog du Graphe');
        await expect(table.locator('tbody tr').first()).toContainText('Denis Nouveau');
    });

    test('seed e-mail literals are not rewritten as parameters', async ({ page }) => {
        test.slow();

        const table = await runAndShowTable(page, fullCode(`
            return await db.Authors
                .OrderBy(a => a.AuthorId)
                .Select(a => new { a.Name, a.Email })
                .ToListAsync();`));

        await expect(table.locator('tbody tr').first()).toContainText('alice@example.com');
        await expect(table).not.toContainText('$1');
    });
});
