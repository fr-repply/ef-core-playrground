# EF Core 10 Playground — Instructions & Bonnes Pratiques

## 🎯 Présentation

Ce playground est un environnement interactif de type REPL pour **Entity Framework Core 10**. Il permet aux apprenants d'écrire et d'exécuter des requêtes LINQ directement dans le navigateur, sans aucune installation côté serveur.

### Architecture

```
┌──────────────────────────────────────────────┐
│              Navigateur Web                   │
│                                              │
│  ┌─────────────┐  ┌────────────────────────┐ │
│  │ Monaco Editor│  │ Résultats (table/arbre)│ │
│  │ (C# / LINQ) │  │                        │ │
│  └──────┬──────┘  └────────────▲───────────┘ │
│         │                      │             │
│  ┌──────▼──────────────────────┴───────────┐ │
│  │         .NET WASM Runtime                │ │
│  │  ┌──────────┐  ┌──────────┐ ┌────────┐  │ │
│  │  │  Roslyn   │  │ EF Core  │ │ SQLite │  │ │
│  │  │(compile)  │  │  10      │ │ (WASM) │  │ │
│  │  └──────────┘  └──────────┘ └────────┘  │ │
│  └─────────────────────────────────────────┘ │
└──────────────────────────────────────────────┘
```

**Technologies :**
- **Blazor WebAssembly** (.NET 10) — Runtime .NET dans le navigateur
- **Roslyn** (Microsoft.CodeAnalysis.CSharp) — Compilation C# dynamique in-browser
- **EF Core 10 + SQLite** — ORM + base de données en mémoire (WASM)
- **Monaco Editor** — Éditeur de code riche (même éditeur que VS Code)
- **Bootstrap 5** — Framework CSS pour l'interface utilisateur

## 🚀 Démarrage rapide

### Prérequis
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Node.js 18+](https://nodejs.org/) (pour les tests Playwright)

### Lancer le playground en local

```bash
cd src/EfCorePlayground
dotnet run
```

Ouvrir http://localhost:5000 dans le navigateur.

### Lancer les tests Playwright

```bash
cd tests/e2e
npm install
npx playwright install chromium
npx playwright test
```

## 📐 Structure du projet

```
├── src/EfCorePlayground/
│   ├── Components/              # Composants Blazor réutilisables
│   │   ├── SchemaPanel.razor    # Affichage du schéma de la BDD
│   │   ├── ExamplesPanel.razor  # Liste d'exemples cliquables
│   │   └── ResultsPanel.razor   # Affichage des résultats (table/erreurs)
│   ├── Layout/
│   │   └── MainLayout.razor     # Layout principal (navbar)
│   ├── Models/
│   │   ├── Blog.cs              # Entité Blog
│   │   ├── Post.cs              # Entité Post
│   │   ├── Author.cs            # Entité Author
│   │   ├── Tag.cs               # Entité Tag
│   │   └── PlaygroundDbContext.cs # DbContext avec seed data
│   ├── Pages/
│   │   └── Playground.razor     # Page principale du playground
│   ├── Services/
│   │   └── CodeExecutionService.cs # Service de compilation/exécution Roslyn
│   ├── wwwroot/
│   │   ├── css/app.css          # Styles personnalisés
│   │   ├── js/monaco-interop.js # Interop JS pour Monaco Editor
│   │   └── index.html           # Page hôte
│   ├── Program.cs               # Point d'entrée Blazor WASM
│   └── EfCorePlayground.csproj  # Fichier projet .NET
├── tests/e2e/
│   ├── tests/playground.spec.ts # Tests Playwright E2E
│   └── playwright.config.ts     # Configuration Playwright
├── INSTRUCTIONS.md              # Ce fichier
└── README.md
```

## 🗄️ Schéma de la base de données

### Entités

| Table    | Colonnes                                       |
|----------|------------------------------------------------|
| Blogs    | BlogId (PK), Name, Url, Rating, CreatedAt      |
| Posts    | PostId (PK), Title, Content, PublishedDate, BlogId (FK), AuthorId (FK) |
| Authors  | AuthorId (PK), Name, Email, Bio                |
| Tags     | TagId (PK), Name                               |

### Relations
- **Blog** 1──* **Post** (un blog a plusieurs posts)
- **Author** 1──* **Post** (un auteur a plusieurs posts)
- **Post** \*──\* **Tag** (relation many-to-many via table de jointure `PostTag`)

### Données seedées
- 4 blogs, 3 auteurs, 10 posts, 8 tags avec des relations réalistes

## ✍️ Comment écrire des requêtes

Le code que vous écrivez est injecté dans une méthode async qui reçoit un `PlaygroundDbContext db`. Vous devez retourner un résultat avec `return`.

### Exemples

```csharp
// Lister tous les blogs
return await db.Blogs.ToListAsync();

// Filtrage
return await db.Blogs
    .Where(b => b.Rating >= 4)
    .OrderByDescending(b => b.Rating)
    .ToListAsync();

// Jointures (Include)
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

## 🏗️ Bonnes pratiques

### Pour les contributeurs

1. **Composants Blazor** : Gardez les composants petits et focalisés. Chaque composant ne devrait avoir qu'une seule responsabilité.

2. **JS Interop** : Minimisez les appels JS interop. Regroupez les opérations quand c'est possible. Utilisez `IJSRuntime` pour les appels unidirectionnels et `DotNetObjectReference` pour les callbacks.

3. **Gestion mémoire WASM** : Le runtime .NET WASM a des contraintes mémoire. Disposez toujours les `SqliteConnection` et `DbContext` après utilisation.

4. **Roslyn dans WASM** : La compilation Roslyn dans le navigateur est lente (~1-3s). N'optimisez pas prématurément mais gardez le code compilé minimal.

5. **Tests E2E** : Les tests Playwright doivent attendre le chargement complet du WASM (~30s au premier lancement). Utilisez des timeouts appropriés.

### Pour les apprenants

1. **Toujours utiliser `await`** : Les requêtes EF Core sont asynchrones. N'oubliez pas `await` et `ToListAsync()`.

2. **`Select` avant `ToList`** : Projetez vos données avec `Select()` pour éviter de charger des entités complètes.

3. **Évitez les N+1** : Utilisez `Include()` pour les relations que vous allez accéder, ou projetez directement avec `Select()`.

4. **AsNoTracking** : Pour les requêtes en lecture seule, `AsNoTracking()` améliore les performances.

## 🌐 Déploiement statique (GitHub Pages)

Le projet se déploie comme un site statique :

```bash
cd src/EfCorePlayground
dotnet publish -c Release -o ../../dist

# Les fichiers statiques sont dans dist/wwwroot/
# Déployez ce dossier sur GitHub Pages, Netlify, Vercel, etc.
```

### Configuration GitHub Pages

1. Allez dans **Settings > Pages** de votre repository
2. Source : **GitHub Actions**
3. Créez un workflow `.github/workflows/deploy.yml` (voir ci-dessous)

### Workflow de déploiement

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
      - run: dotnet publish src/EfCorePlayground -c Release -o dist
      - run: cp dist/wwwroot/index.html dist/wwwroot/404.html
      - uses: actions/upload-pages-artifact@v3
        with:
          path: dist/wwwroot
      - id: deployment
        uses: actions/deploy-pages@v4
```

> **Note :** Pour GitHub Pages avec un sous-chemin (ex: `/ef-core-playground/`), modifiez la balise `<base href="/">` dans `index.html` en `<base href="/ef-core-playground/">`.

## 🔮 Évolutions futures

- [ ] Ajouter la coloration syntaxique complète C# avec autocomplétion contextuelle
- [ ] Afficher le SQL généré par EF Core pour chaque requête
- [ ] Supporter le chargement de code depuis une URL (tutoriels externes)
- [ ] Ajouter un mode « pas à pas » pour décomposer les requêtes LINQ
- [ ] Sauvegarder/partager des requêtes via URL avec paramètres
- [ ] Ajouter d'autres schémas de base de données (e-commerce, réseau social, etc.)
- [ ] Migrer vers PGlite quand le support WASM sera mature
- [ ] Ajouter des exercices guidés avec validation automatique
