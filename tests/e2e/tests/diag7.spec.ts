import { test, expect } from '@playwright/test';

test('run groupement query directly in PGlite', async ({ page }) => {
    test.slow();

    await page.goto('/');
    await expect(page.locator('nav')).toBeVisible({ timeout: 90000 });
    await expect(page.locator('#monaco-editor-container .monaco-editor')).toBeVisible({ timeout: 60000 });
    await page.waitForTimeout(3000);

    // First run default code to initialize PGlite with tables and seed data
    await page.getByRole('button', { name: /Exécuter/ }).click();
    const firstResult = page.locator('#results-table, .alert-danger');
    await expect(firstResult.first()).toBeVisible({ timeout: 120000 });
    console.log('First execution done - PGlite initialized');

    // Now run the groupement query directly in PGlite from the browser
    const result = await page.evaluate(async () => {
        try {
            const r = await (window as any).pgliteInterop.query(`
                SELECT a."Name" AS "Auteur", (
                    SELECT count(*)::int
                    FROM "Posts" AS p
                    WHERE a."AuthorId" = p."AuthorId") AS "NbPosts", (
                    SELECT max(p0."PublishedDate")
                    FROM "Posts" AS p0
                    WHERE a."AuthorId" = p0."AuthorId") AS "DernierePublication", (
                    SELECT count(*)::int
                    FROM (
                        SELECT DISTINCT p1."BlogId"
                        FROM "Posts" AS p1
                        WHERE a."AuthorId" = p1."AuthorId"
                    ) AS p2) AS "NbBlogs"
                FROM "Authors" AS a
            `, []);
            return JSON.stringify(r, null, 2);
        } catch (e: any) {
            return 'ERROR: ' + e.message;
        }
    });

    console.log('PGlite direct query result:', result);
});
