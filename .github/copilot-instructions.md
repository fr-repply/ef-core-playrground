# GitHub Copilot Instructions — EF Core 10 Playground

> Pour le contexte complet (schéma BDD, exemples LINQ, déploiement), voir [`INSTRUCTIONS.md`](../INSTRUCTIONS.md).

## Stack technique

- **Blazor WebAssembly** (.NET 10) — tout tourne dans le navigateur, pas de serveur
- **Roslyn** (`Microsoft.CodeAnalysis.CSharp`) — compilation C# dynamique in-browser
- **EF Core 10 + PGlite** — backend PostgreSQL WASM via JS interop (pas SQLite)
- **EntityFrameworkCore.Projectables** — traduit des propriétés C# en SQL via source generator
- **Monaco Editor** + **Bootstrap 5** — bundlés localement via **Vite** (aucun CDN)

## Architecture des services clés

### `CodeExecutionService`
Pipeline d'exécution en 3 niveaux :
1. **Cache mémoire** (`Dictionary<string, byte[]>` statique, clé = SHA-256 du code)
2. **Fichiers statiques pré-compilés** (`wwwroot/precompiled/{hash}.dll`) — générés au build par `tools/EfCorePlayground.Precompiler`
3. **Compilation Roslyn** à la volée → résultat mis en cache niveaux 1 & 2

Le code utilisateur est enveloppé dans un template fixe via `BuildFullCode()`. `PrefixLineCount` / `SuffixLineCount` permettent de mapper les erreurs sur les lignes affichées dans l'éditeur.

### `PgLite*` (Services/)
Couche d'adaptation EF Core → PGlite : `PgLiteRelationalConnection`, `PgLiteDbConnection`, `PgLiteDbDataReader`, `PgLiteModificationCommandBatch`, `PgLiteDatabaseCreator`. Tous les appels SQL transitent via `IJSRuntime` → `pgliteInterop` (JS).

### `PgLiteSqlCapture`
Singleton qui intercepte les commandes SQL entre `.Start()` et `.Stop()` pour les afficher dans le panneau SQL de l'UI.

## Modèle de données

| Entité | Relations |
|--------|-----------|
| `Blog` | 1 → * `Post` |
| `Author` | 1 → * `Post` |
| `Post` | * → * `Tag` (table `PostTag`), FK `BlogId`, FK `AuthorId` |
| `Tag` | * → * `Post` |

`PlaygroundDbContext` contient le seed data. **Ne pas modifier le seed** sans mettre à jour les exemples dans `Models/ExampleSnippets.cs`.

## Exemples & pré-compilation

`Models/ExampleSnippets.cs` est la **source unique** des exemples affichés (`ExamplesPanel.razor`) et compilés par le Precompiler. Toute modification d'exemple doit s'y faire — ne pas dupliquer dans l'UI.

Le projet `tools/EfCorePlayground.Precompiler` est lancé automatiquement via une MSBuild target (`AfterTargets="Build"`) et produit des `.dll` dans `wwwroot/precompiled/`.

## Conventions de code

- Les composants Blazor ont **une seule responsabilité** (voir `Components/`)
- Les appels JS interop sont regroupés au maximum
- Disposer systématiquement `DbContext` et connexions (`await using`)
- Utiliser `AsNoTracking()` pour toutes les requêtes en lecture seule dans les exemples
- Les exemples `[Projectable]` déclenchent le source generator Roslyn à l'exécution via `RunProjectablesGeneratorAsync()`

## Contraintes WASM

- Pas d'accès fichier système → tout passe par HTTP ou `IJSRuntime`
- Mémoire limitée : éviter les gros allocations, disposer les assemblies chargées dynamiquement
- Roslyn est coûteux (~1-3 s) — toujours passer par le cache avant de compiler
- Les assemblies pré-compilées sont servies comme assets statiques Blazor WASM

## Points d'entrée

| Fichier | Rôle |
|---------|------|
| `Program.cs` | Bootstrap Blazor WASM, enregistrement des services |
| `Pages/Playground.razor` | Page principale, orchestre éditeur + exécution + résultats |
| `ClientApp/main.js` | Point d'entrée Vite (Monaco, Bootstrap, interops JS) |
| `wwwroot/index.html` | Page hôte Blazor |

