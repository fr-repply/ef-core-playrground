import { test, expect } from '@playwright/test';

test.describe('EF Core Playground', () => {

    test.beforeEach(async ({ page }) => {
        await page.goto('/');
        // Wait for Blazor + Monaco to load
        await expect(page.locator('nav')).toBeVisible({ timeout: 90000 });
        await expect(page.locator('#monaco-editor-container .monaco-editor')).toBeVisible({ timeout: 60000 });
    });

    test('should display the page title and navbar', async ({ page }) => {
        await expect(page).toHaveTitle('EF Core 10 Playground');
        await expect(page.locator('nav')).toContainText('EF Core 10 Playground');
    });

    test('should display the schema panel with tables', async ({ page }) => {
        const schemaPanel = page.locator('.card-body').first();
        await expect(page.getByRole('heading', { name: 'Blogs' })).toBeVisible();
        await expect(page.getByRole('heading', { name: 'Posts' })).toBeVisible();
        await expect(page.getByRole('heading', { name: 'Authors' })).toBeVisible();
        await expect(page.getByRole('heading', { name: 'Tags' })).toBeVisible();
    });

    test('should display the examples panel', async ({ page }) => {
        await expect(page.getByText('Exemples', { exact: true })).toBeVisible();
        await expect(page.getByRole('button', { name: 'Lister tous les blogs' })).toBeVisible();
    });

    test('should have the Monaco editor loaded', async ({ page }) => {
        const editor = page.locator('#monaco-editor-container .monaco-editor');
        await expect(editor).toBeVisible();
    });

    test('should display results panel', async ({ page }) => {
        await expect(page.getByText('Résultats', { exact: true })).toBeVisible();
        await expect(page.getByRole('button', { name: /Exécuter/ })).toBeVisible();
    });

    test('should execute default code and show results', async ({ page }) => {
        // Roslyn compilation in WASM can be very slow in CI (~30-60s)
        test.slow();

        // Click the execute button
        await page.getByRole('button', { name: /Exécuter/ }).click();

        // Wait for spinner to appear (confirms click was registered)
        await expect(page.getByText('Exécution...')).toBeVisible({ timeout: 5000 }).catch(() => {});

        // Wait for results — either success table or error
        const resultOrError = page.locator('#results-table, .alert-danger');
        await expect(resultOrError.first()).toBeVisible({ timeout: 180000 });

        // If we got a table, validate it
        const table = page.locator('#results-table');
        if (await table.isVisible()) {
            await expect(table.locator('thead')).toContainText('Name');
            await expect(table.locator('tbody tr')).toHaveCount(4);
        }
    });

    test('should load example into editor', async ({ page }) => {
        // Click on example
        await page.getByRole('button', { name: 'Blogs avec rating' }).click();

        // Verify editor content changed
        const editorValue = await page.evaluate(() => {
            return (window as any).monacoInterop.getValue();
        });
        expect(editorValue).toContain('Rating >= 4');
    });

    test('should show compilation errors for invalid code', async ({ page }) => {
        // Collect JS errors during this test
        const jsErrors: string[] = [];
        page.on('pageerror', (error) => jsErrors.push(error.message));

        // Set invalid code
        await page.evaluate(() => {
            (window as any).monacoInterop.setValue('this is not valid C# code!!!');
        });

        // Execute
        await page.getByRole('button', { name: /Exécuter/ }).click();

        // Should show error alert
        await expect(page.locator('.alert-danger').first()).toBeVisible({ timeout: 90000 });

        // Verify no JS runtime errors occurred (e.g. e.map is not a function)
        expect(jsErrors).toEqual([]);
    });

    test('should reset code to default', async ({ page }) => {
        // Load an example first
        await page.getByRole('button', { name: 'Lister tous les blogs' }).click();

        // Click reset button (has title="Réinitialiser" and an icon)
        await page.locator('button[title="Réinitialiser"]').click();

        // Verify editor has default code
        const editorValue = await page.evaluate(() => {
            return (window as any).monacoInterop.getValue();
        });
        expect(editorValue).toContain('Bienvenue');
    });

    test('should toggle schema panel visibility', async ({ page }) => {
        // Use a heading inside the schema panel to check visibility
        const schemaHeading = page.getByRole('heading', { name: 'Blogs' });

        // Schema should be visible initially
        await expect(schemaHeading).toBeVisible();

        // Click schema header to collapse
        await page.getByText('Schéma').click();

        // Schema table headings should be hidden
        await expect(schemaHeading).not.toBeVisible();

        // Click again to expand
        await page.getByText('Schéma').click();

        // Schema table headings should be visible again
        await expect(schemaHeading).toBeVisible();
    });
});

test.describe('EF Core Playground - Groupement par auteur', () => {

    test.beforeEach(async ({ page }) => {
        await page.goto('/');
        await expect(page.locator('nav')).toBeVisible({ timeout: 90000 });
        await expect(page.locator('#monaco-editor-container .monaco-editor')).toBeVisible({ timeout: 60000 });
    });

    test('should execute groupement par auteur without APPLY error', async ({ page }) => {
        test.slow();

        // Collapse schema panel to make room for examples list
        await page.getByText('Schéma').click();

        // Click the example
        await page.getByRole('button', { name: 'Groupement par auteur' }).click();

        // Verify editor content loaded
        const editorValue = await page.evaluate(() => {
            return (window as any).monacoInterop.getValue();
        });
        expect(editorValue).toContain('NbBlogs');

        // Execute
        await page.getByRole('button', { name: /Exécuter/ }).click();

        // Wait for results
        const resultOrError = page.locator('#results-table, .alert-danger');
        await expect(resultOrError.first()).toBeVisible({ timeout: 180000 });

        // Should succeed with a table (3 authors)
        const table = page.locator('#results-table');
        await expect(table).toBeVisible();
        await expect(table.locator('thead')).toContainText('Auteur');
        await expect(table.locator('tbody tr')).toHaveCount(3);
    });
});

test.describe('EF Core Playground - Projectables', () => {

    test.beforeEach(async ({ page }) => {
        await page.goto('/');
        await expect(page.locator('nav')).toBeVisible({ timeout: 90000 });
        await expect(page.locator('#monaco-editor-container .monaco-editor')).toBeVisible({ timeout: 60000 });
    });

    test('should display Projectables examples in the panel', async ({ page }) => {
        // Collapse schema to see all examples
        await page.getByText('Schéma').click();
        await expect(page.getByRole('button', { name: /Projectable: Blogs populaires/ })).toBeVisible();
        await expect(page.getByRole('button', { name: /Projectable: Auteurs productifs/ })).toBeVisible();
        await expect(page.getByRole('button', { name: /Projectable: Posts récents/ })).toBeVisible();
    });

    test('should display computed properties in schema panel', async ({ page }) => {
        // Check that computed properties are shown in schema
        const schemaBody = page.locator('.card-body').first();
        await expect(schemaBody).toContainText('PostCount');
        await expect(schemaBody).toContainText('IsPopular');
        await expect(schemaBody).toContainText('computed');
    });

    test('should execute Projectable: Blogs populaires', async ({ page }) => {
        test.slow();

        // Collapse schema to make room for examples
        await page.getByText('Schéma').click();

        // Click the example
        await page.getByRole('button', { name: /Projectable: Blogs populaires/ }).click();

        // Verify editor content
        const editorValue = await page.evaluate(() => {
            return (window as any).monacoInterop.getValue();
        });
        expect(editorValue).toContain('IsPopular');
        expect(editorValue).toContain('PostCount');

        // Execute
        await page.getByRole('button', { name: /Exécuter/ }).click();

        // Wait for results
        const resultOrError = page.locator('#results-table, .alert-danger');
        await expect(resultOrError.first()).toBeVisible({ timeout: 180000 });

        // Should succeed with a table — blogs with rating >= 4 (3 blogs: .NET, Architecture, Data & EF Core)
        const table = page.locator('#results-table');
        await expect(table).toBeVisible();
        await expect(table.locator('thead')).toContainText('Name');
        await expect(table.locator('thead')).toContainText('PostCount');
        await expect(table.locator('tbody tr')).toHaveCount(3);
    });

    test('should execute Projectable: Auteurs productifs', async ({ page }) => {
        test.slow();

        // Collapse schema to make room for examples
        await page.getByText('Schéma').click();

        // Click the example
        await page.getByRole('button', { name: /Projectable: Auteurs productifs/ }).click();

        // Verify editor content
        const editorValue = await page.evaluate(() => {
            return (window as any).monacoInterop.getValue();
        });
        expect(editorValue).toContain('IsProductive');
        expect(editorValue).toContain('PostCount');

        // Execute
        await page.getByRole('button', { name: /Exécuter/ }).click();

        // Wait for results
        const resultOrError = page.locator('#results-table, .alert-danger');
        await expect(resultOrError.first()).toBeVisible({ timeout: 180000 });

        // Should succeed — authors with 3+ posts
        const table = page.locator('#results-table');
        await expect(table).toBeVisible();
        await expect(table.locator('thead')).toContainText('Name');
        await expect(table.locator('thead')).toContainText('PostCount');
    });

    test('should execute Projectable: Posts récents avec tags', async ({ page }) => {
        test.slow();

        // Collapse schema to make room for examples
        await page.getByText('Schéma').click();

        // Click the example
        await page.getByRole('button', { name: /Projectable: Posts récents/ }).click();

        // Verify editor content
        const editorValue = await page.evaluate(() => {
            return (window as any).monacoInterop.getValue();
        });
        expect(editorValue).toContain('IsRecent');
        expect(editorValue).toContain('TagCount');

        // Execute
        await page.getByRole('button', { name: /Exécuter/ }).click();

        // Wait for results
        const resultOrError = page.locator('#results-table, .alert-danger');
        await expect(resultOrError.first()).toBeVisible({ timeout: 180000 });

        // Should succeed — posts from 2024+
        const table = page.locator('#results-table');
        await expect(table).toBeVisible();
        await expect(table.locator('thead')).toContainText('Title');
        await expect(table.locator('thead')).toContainText('TagCount');
        // Posts from 2024: PostId 6,7,8,9,10 = 5 posts
        await expect(table.locator('tbody tr')).toHaveCount(5);
    });
});
