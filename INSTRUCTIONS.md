# EF Core 10 Playground — Guide utilisateur

## 🎯 Présentation

Environnement interactif de type REPL pour **Entity Framework Core 10** : écrivez et exécutez des requêtes LINQ directement dans le navigateur, sans installation côté serveur.

> **Architecture, stack technique, modèle de données, conventions de code** → voir [`.github/copilot-instructions.md`](.github/copilot-instructions.md).

## 🚀 Démarrage rapide

### Prérequis
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Node.js 18+](https://nodejs.org/) (pour Vite et les tests Playwright)

### Lancer le playground en local

```bash
cd src/EfCorePlayground
npm install     # Installe Monaco, Bootstrap, Vite
dotnet run      # Vite build + Precompiler + serveur Blazor
```

Ouvrir http://localhost:5000 dans le navigateur.

### Lancer les tests Playwright

```bash
cd tests/e2e
npm install
npx playwright install chromium
npx playwright test
```

## ✍️ Comment écrire des requêtes

Le code est injecté dans une méthode `async Task<object?> Execute(PlaygroundDbContext db)`. Retournez un résultat avec `return`.

```csharp
// Lister tous les blogs
return await db.Blogs.ToListAsync();

// Filtrage + tri
return await db.Blogs
    .Where(b => b.Rating >= 4)
    .OrderByDescending(b => b.Rating)
    .ToListAsync();

// Jointures (Include ou Select)
return await db.Posts
    .Include(p => p.Author)
    .Select(p => new { p.Title, Auteur = p.Author.Name })
    .ToListAsync();

// Agrégation
return await db.Blogs
    .Select(b => new { Blog = b.Name, NbPosts = b.Posts.Count() })
    .ToListAsync();

// Many-to-Many
return await db.Posts
    .Include(p => p.Tags)
    .Select(p => new { p.Title, Tags = string.Join(", ", p.Tags.Select(t => t.Name)) })
    .ToListAsync();
```

### Bonnes pratiques pour les apprenants

- **Toujours utiliser `await`** : les requêtes EF Core sont asynchrones.
- **`Select` avant `ToList`** : projetez vos données pour éviter de charger des entités complètes.
- **Évitez les N+1** : utilisez `Include()` ou projetez directement avec `Select()`.
- **`AsNoTracking()`** : pour les requêtes en lecture seule, améliore les performances.

## 🌐 Déploiement statique (GitHub Pages)

```bash
cd src/EfCorePlayground
npm ci
dotnet publish -c Release -o ../../dist
# Déployez dist/wwwroot/ sur GitHub Pages, Netlify, Vercel, etc.
```

### Workflow GitHub Actions

```yaml
name: Deploy to GitHub Pages

on:
  push:
    branches: [main]

permissions:
  pages: write
  id-token: write

jobs:
  deploy:
    runs-on: ubuntu-latest
    environment:
      name: github-pages
      url: ${{ steps.deployment.outputs.page_url }}
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
      - uses: actions/setup-node@v4
        with:
          node-version: '20'
      - run: cd src/EfCorePlayground && npm ci
      - run: dotnet publish src/EfCorePlayground -c Release -o dist
      - run: cp dist/wwwroot/index.html dist/wwwroot/404.html
      - uses: actions/upload-pages-artifact@v3
        with:
          path: dist/wwwroot
      - id: deployment
        uses: actions/deploy-pages@v4
```

> Pour un sous-chemin GitHub Pages (ex: `/ef-core-playground/`), modifiez `<base href="/">` en `<base href="/ef-core-playground/">` dans `wwwroot/index.html`.

## 🔮 Évolutions futures

- [ ] Autocomplétion C# contextuelle dans Monaco
- [x] Charger du code depuis une URL (tutoriels externes)
- [ ] Mode « pas à pas » pour décomposer les requêtes LINQ
- [x] Sauvegarder/partager des requêtes via URL
- [ ] Autres schémas de base de données (e-commerce, réseau social…)
- [ ] Exercices guidés avec validation automatique
- [x] ~~Migrer vers PGlite~~ — PostgreSQL WASM intégré via PGlite
- [x] ~~Afficher le SQL généré~~ — panneau SQL intégré via `PgLiteSqlCapture`
- [x] ~~Cache des assemblies compilées~~ — pré-compilation au build + cache mémoire
