import { test, expect } from '@playwright/test';

test.describe('EF Core Playground', () => {

    test.beforeEach(async ({ page }) => {
        await page.goto('/');
        // Wait for the Blazor app to fully load (Monaco editor should appear)
        await expect(page.locator('#monaco-editor-container .monaco-editor')).toBeVisible({ timeout: 90000 });
    });

    test('should display the page title and navbar', async ({ page }) => {
        await expect(page).toHaveTitle('EF Core 10 Playground');
        await expect(page.locator('nav')).toContainText('EF Core 10 Playground');
    });

    test('should display the schema panel with tables', async ({ page }) => {
        await expect(page.getByText('Schéma')).toBeVisible();
        await expect(page.getByText('Blogs')).toBeVisible();
        await expect(page.getByText('Posts')).toBeVisible();
        await expect(page.getByText('Authors')).toBeVisible();
        await expect(page.getByText('Tags')).toBeVisible();
    });

    test('should display the examples panel', async ({ page }) => {
        await expect(page.getByText('Exemples')).toBeVisible();
        await expect(page.getByText('Lister tous les blogs')).toBeVisible();
    });

    test('should have Monaco editor loaded', async ({ page }) => {
        const editor = page.locator('#monaco-editor-container .monaco-editor');
        await expect(editor).toBeVisible();
    });

    test('should display results panel placeholder', async ({ page }) => {
        await expect(page.getByText('Résultats')).toBeVisible();
        await expect(page.getByText('Exécuter')).toBeVisible();
    });

    test('should execute default code and show results', async ({ page }) => {
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

    test('should load and execute an example', async ({ page }) => {
        // Click on "Blogs avec rating ≥ 4" example
        await page.getByText('Blogs avec rating').click();

        // Execute
        await page.getByRole('button', { name: /Exécuter/ }).click();

        // Wait for results
        await expect(page.locator('#results-table')).toBeVisible({ timeout: 60000 });

        // Should have fewer results (only blogs with rating >= 4)
        const rows = page.locator('#results-table tbody tr');
        const count = await rows.count();
        expect(count).toBeLessThanOrEqual(4);
        expect(count).toBeGreaterThan(0);
    });

    test('should show compilation errors for invalid code', async ({ page }) => {
        // Set invalid code in Monaco editor
        await page.evaluate(() => {
            (window as any).monacoInterop.setValue('this is not valid C# code!!!');
        });

        // Execute
        await page.getByRole('button', { name: /Exécuter/ }).click();

        // Should show error
        await expect(page.locator('.alert-danger').first()).toBeVisible({ timeout: 60000 });
    });

    test('should display execution time after running', async ({ page }) => {
        await page.getByRole('button', { name: /Exécuter/ }).click();
        await expect(page.locator('#results-table')).toBeVisible({ timeout: 60000 });
        await expect(page.getByText(/\d+ ms/)).toBeVisible();
    });

    test('should reset code to default', async ({ page }) => {
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
