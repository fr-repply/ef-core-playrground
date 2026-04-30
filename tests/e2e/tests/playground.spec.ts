import { test, expect } from '@playwright/test';

test.describe('EF Core Playground', () => {

    test.beforeEach(async ({ page }) => {
        await page.goto('/');
        // Wait for the Blazor app to fully load - the navbar should appear
        await expect(page.locator('nav')).toBeVisible({ timeout: 90000 });
        // Wait for the playground page to render (schema panel is server-rendered by Blazor)
        await expect(page.getByText('Schéma')).toBeVisible({ timeout: 30000 });
    });

    test('should display the page title and navbar', async ({ page }) => {
        await expect(page).toHaveTitle('EF Core 10 Playground');
        await expect(page.locator('nav')).toContainText('EF Core 10 Playground');
    });

    test('should display the schema panel with tables', async ({ page }) => {
        await expect(page.getByText('Schéma')).toBeVisible();
        await expect(page.getByRole('heading', { name: 'Blogs' })).toBeVisible();
        await expect(page.getByRole('heading', { name: 'Posts' })).toBeVisible();
        await expect(page.getByRole('heading', { name: 'Authors' })).toBeVisible();
        await expect(page.getByRole('heading', { name: 'Tags' })).toBeVisible();
    });

    test('should display the examples panel', async ({ page }) => {
        await expect(page.getByText('Exemples')).toBeVisible();
        await expect(page.getByText('Lister tous les blogs')).toBeVisible();
    });

    test('should have the editor container', async ({ page }) => {
        const editorContainer = page.locator('#monaco-editor-container');
        await expect(editorContainer).toBeVisible();
    });

    test('should display results panel placeholder', async ({ page }) => {
        await expect(page.getByText('Résultats')).toBeVisible();
        await expect(page.getByRole('button', { name: /Exécuter/ })).toBeVisible();
    });

    test('should execute default code and show results', async ({ page }) => {
        // Wait for Monaco to load (CDN dependency, skip if not available)
        const monacoReady = await page.evaluate(() => {
            return typeof (window as any).monacoInterop?.editor !== 'undefined' &&
                   (window as any).monacoInterop.editor !== null;
        }).catch(() => false);

        if (!monacoReady) {
            // Monaco CDN might not be available; skip execution test
            test.skip();
            return;
        }

        // Click the execute button
        await page.getByRole('button', { name: /Exécuter/ }).click();

        // Wait for results to appear (table with blog data)
        await expect(page.locator('#results-table')).toBeVisible({ timeout: 60000 });

        // Should show the results table with blog columns
        await expect(page.locator('#results-table thead')).toContainText('BlogId');
        await expect(page.locator('#results-table thead')).toContainText('Name');

        // Should have rows with seed data
        await expect(page.locator('#results-table tbody tr')).toHaveCount(4);
    });

    test('should load example into editor', async ({ page }) => {
        const monacoReady = await page.evaluate(() => {
            return typeof (window as any).monacoInterop?.editor !== 'undefined' &&
                   (window as any).monacoInterop.editor !== null;
        }).catch(() => false);

        if (!monacoReady) {
            test.skip();
            return;
        }

        // Click on example
        await page.getByText('Blogs avec rating').click();

        // Verify editor content changed
        const editorValue = await page.evaluate(() => {
            return (window as any).monacoInterop.getValue();
        });
        expect(editorValue).toContain('Rating >= 4');
    });

    test('should show compilation errors for invalid code', async ({ page }) => {
        const monacoReady = await page.evaluate(() => {
            return typeof (window as any).monacoInterop?.editor !== 'undefined' &&
                   (window as any).monacoInterop.editor !== null;
        }).catch(() => false);

        if (!monacoReady) {
            test.skip();
            return;
        }

        // Set invalid code in Monaco editor
        await page.evaluate(() => {
            (window as any).monacoInterop.setValue('this is not valid C# code!!!');
        });

        // Execute
        await page.getByRole('button', { name: /Exécuter/ }).click();

        // Should show error
        await expect(page.locator('.alert-danger').first()).toBeVisible({ timeout: 60000 });
    });

    test('should reset code to default', async ({ page }) => {
        const monacoReady = await page.evaluate(() => {
            return typeof (window as any).monacoInterop?.editor !== 'undefined' &&
                   (window as any).monacoInterop.editor !== null;
        }).catch(() => false);

        if (!monacoReady) {
            test.skip();
            return;
        }

        // Load an example first
        await page.getByText('Lister tous les blogs').click();

        // Click reset button
        await page.getByRole('button', { name: 'Réinitialiser' }).click();

        // Verify editor has default code
        const editorValue = await page.evaluate(() => {
            return (window as any).monacoInterop.getValue();
        });
        expect(editorValue).toContain('Bienvenue');
    });

    test('should toggle schema panel visibility', async ({ page }) => {
        // Schema should be visible initially
        await expect(page.getByText('BlogId')).toBeVisible();

        // Click schema header to collapse
        await page.getByText('Schéma').click();

        // Schema details should be hidden
        await expect(page.getByText('BlogId')).not.toBeVisible();

        // Click again to expand
        await page.getByText('Schéma').click();

        // Schema details should be visible again
        await expect(page.getByText('BlogId')).toBeVisible();
    });
});
