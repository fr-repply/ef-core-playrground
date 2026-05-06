# GitHub Copilot Instructions — EF Core 10 Playground

> Pour le contexte complet (schéma BDD, exemples LINQ, déploiement), voir [`INSTRUCTIONS.md`](../INSTRUCTIONS.md).

## ⚠️ Règle de maintenance de ce fichier

**Ce fichier DOIT être maintenu à jour à chaque modification significative du projet.**
Toute nouvelle fonctionnalité, convention, contrainte technique, ou décision d'architecture doit être consignée ici immédiatement. Ne pas laisser ce fichier se désynchroniser du code réel.

---

## Stack technique

- **Blazor WebAssembly** (.NET 10) — tout tourne dans le navigateur, pas de serveur
- **Roslyn** (`Microsoft.CodeAnalysis.CSharp`) — compilation C# dynamique in-browser
- **EF Core 10 + PGlite** (`@electric-sql/pglite`) — backend PostgreSQL WASM via JS interop (pas SQLite)
- **Npgsql** — provider EF Core PostgreSQL (avec services remplacés pour PGlite)
- **EntityFrameworkCore.Projectables** — traduit des propriétés C# en SQL via source generator
- **Monaco Editor** + **Bootstrap 5** — bundlés localement via **Vite** (aucun CDN)

### Quirk critique — `Npgsql.EnableLegacyTimestampBehavior`

Dans `Program.cs`, le switch suivant est activé **avant tout** :
```csharp
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
```
Sans ce switch, Npgsql lève une exception pour les `DateTime` sans `DateTimeKind.Utc`. **Ne pas supprimer.**

---

## Architecture des services clés

### `CodeExecutionService`

Pipeline d'exécution en **4 niveaux de cache** :

1. **L1 — Cache mémoire** (`Dictionary<string, byte[]>` statique, clé = SHA-256 du code compilé)
2. **L2 — Fichiers statiques pré-compilés** (`wwwroot/precompiled/{hash}.dll`) — générés au build par `tools/EfCorePlayground.Precompiler`
3. **L3 — localStorage** via `efCacheInterop` (JS) — persistance entre sessions navigateur
4. **L4 — Compilation Roslyn** à la volée → résultat mis en cache niveaux L1 & L3

> Restauration L3 → L1 au démarrage via `LoadCacheFromStorageAsync()`.
> Warm-up Roslyn fire-and-forget via `WarmUpRoslynBackground()` (JIT + pré-chargement des refs).

**`CodeExecutionService` n'est pas enregistré dans le DI container.** Il est instancié directement dans `Playground.razor` :
```csharp
executionService = new CodeExecutionService(JS, Http);
```

Le code utilisateur est enveloppé dans un template fixe via `BuildFullCode()` :
```csharp
namespace EfCorePlayground.UserCode
{
    public static class UserQuery
    {
        public static async Task<object?> Execute(PlaygroundDbContext db) { /* code utilisateur */ }
    }
}
```
`PrefixLineCount = 14` / `SuffixLineCount = 3` permettent de mapper les erreurs sur les lignes affichées dans l'éditeur.

**Détection automatique `[Projectable]`** : dans `ExecuteAsync`, si le code source contient `[Projectable]` ou `[Projectable(`, le source generator `RunProjectablesGeneratorAsync()` est déclenché automatiquement.

**Métadonnées Roslyn** : récupérées depuis `_framework/blazor.boot.json` (ou `dotnet.boot.json`). Seules les DLLs PE managées (avec header CLI) sont acceptées — les artifacts natifs WASM sont filtrés via `IsManagedPe()`. En cas d'échec, une liste de fallback codée en dur est utilisée.

### `PgLite*` (Services/)

Couche d'adaptation EF Core → PGlite :
- `PgLiteRelationalConnection`, `PgLiteDbConnection`, `PgLiteDbDataReader`
- `PgLiteModificationCommandBatch`, `PgLiteDatabaseCreator`

Tous les appels SQL transitent via `IJSRuntime` → `window.pgliteInterop` (JS).

`PgLiteJsRuntime.Instance` est un **singleton** alimenté dans `RunCompiledCode()` avant chaque exécution :
```csharp
PgLiteJsRuntime.Instance = _jsRuntime;
```

**Une nouvelle instance PGlite est créée à chaque exécution** (`pgliteInterop.init()`), donc la base est remise à zéro à chaque run. Les données de seed sont réappliquées via `EnsureCreatedAsync()`.

Services Npgsql remplacés pour contourner les casts directs vers `NpgsqlConnection` :
- `IRelationalConnection` → `PgLiteRelationalConnection`
- `IRelationalDatabaseCreator` → `PgLiteDatabaseCreator`
- `IModificationCommandBatchFactory` → `PgLiteModificationCommandBatchFactory`

### `PgLiteSqlCapture`

Singleton qui intercepte les commandes SQL entre `.Start()` et `.Stop()` pour les afficher dans le panneau SQL de l'UI. L'interception démarre **après** `EnsureCreatedAsync()` (les requêtes de création de schéma ne sont pas capturées).

Méthode `AnnotateLast(int count)` : associe un badge "nombre d'entités trackées" à la dernière requête capturée. Utilisée via `db.AnnotateLastQueryWithTracking()` dans les exemples de tracking.

---

## Modèle de données

| Entité | Relations | Propriétés clés |
|--------|-----------|-----------------|
| `Blog` | 1 → * `Post` | `BlogId`, `Name`, `Url`, `Rating`, `CreatedAt`, `Posts` |
| `Author` | 1 → * `Post` | `AuthorId`, `Name`, `Email`, `Bio`, `Posts` |
| `Post` | * → * `Tag` (table `PostTag`), FK `BlogId`, FK `AuthorId` | `PostId`, `Title`, `Content`, `PublishedDate`, `BlogId`, `AuthorId`, `Blog`, `Author`, `Tags` |
| `Tag` | * → * `Post` | `TagId`, `Name`, `Posts` |

### Propriétés `[Projectable]` (traduites en SQL)

Définies sur les entités via `EntityFrameworkCore.Projectables` :
- `Blog.IsPopular` — `Rating >= 4`
- `Blog.PostCount` — `Posts.Count`
- `Author.IsProductive` — `Posts.Count >= 3`
- `Author.PostCount` — `Posts.Count`
- `Post.IsRecent` — `PublishedDate > DateTime.Now.AddMonths(-6)`
- `Post.TagCount` — `Tags.Count`

### Seed data (ne pas modifier sans mettre à jour `ExampleSnippets.cs`)

- **4 blogs** : "Le Blog .NET" (rating 5), "Architecture Moderne" (4), "Data & EF Core" (5), "Web Dev Tips" (3)
- **3 auteurs** : Alice Martin, Bob Dupont, Claire Bernard
- **10 posts** répartis sur les 4 blogs (IDs 1–10)
- **8 tags** : EF Core, LINQ, C#, Architecture, Performance, Database, Web, Blazor

`PlaygroundDbContext` contient le seed data. **Ne pas modifier le seed** sans mettre à jour les exemples dans `Models/ExampleSnippets.cs`.

---

## Exemples & pré-compilation

`Models/ExampleSnippets.cs` est la **source unique** des exemples affichés (`ExamplesPanel.razor`) et compilés par le Precompiler. Toute modification d'exemple doit s'y faire — ne pas dupliquer dans l'UI.

Les exemples avec `IsFullCode: true` contiennent le code complet (namespace, classe, méthode) et sont chargés via le préfixe `FULLCODE:` dans `LoadExample()`.

Le projet `tools/EfCorePlayground.Precompiler` est lancé automatiquement via une MSBuild target (`AfterTargets="Build"`) et produit des `.dll` dans `wwwroot/precompiled/`.

---

## Fonctionnalités UI

### Partage de code via URL

Le bouton "Partager" encode le code courant de l'éditeur en **base64** et l'injecte dans le paramètre `?code=` de l'URL. Au chargement, `Playground.razor` détecte ce paramètre et initialise Monaco avec le code décodé.

### Panneau SQL

Affiche les requêtes SQL capturées par `PgLiteSqlCapture` avec badges optionnels (ex : nombre d'entités trackées). Viewer Monaco dédié, réinitialisé avant chaque exécution via `monacoInterop.disposeSqlViewer`.

### DataViewerPanel

Rafraîchi automatiquement après chaque exécution réussie via `dataViewerPanel.RefreshData()`.

---

## Conventions de code

- Les composants Blazor ont **une seule responsabilité** (voir `Components/`)
- Les appels JS interop sont regroupés au maximum
- Disposer systématiquement `DbContext` et connexions (`await using`)
- Utiliser `AsNoTracking()` pour toutes les requêtes en lecture seule dans les exemples
- Les exemples `[Projectable]` déclenchent le source generator Roslyn à l'exécution via `RunProjectablesGeneratorAsync()`
- Toujours utiliser `DateTimeKind.Utc` pour les `DateTime` (ou activer `EnableLegacyTimestampBehavior`)

---

## Contraintes WASM

- Pas d'accès fichier système → tout passe par HTTP ou `IJSRuntime`
- Mémoire limitée : éviter les grosses allocations, disposer les assemblies chargées dynamiquement
- Roslyn est coûteux (~1-3 s) — toujours passer par le cache avant de compiler
- Les assemblies pré-compilées sont servies comme assets statiques Blazor WASM
- `Assembly.TryGetRawMetadata()` retourne du bytecode WASM natif en mode `WasmBuildNative=true` → ne jamais l'utiliser pour les références Roslyn, toujours passer par HTTP `_framework/`

### Références Roslyn — assemblies managées (`wwwroot/ref/*.bin`)

En mode `WasmBuildNative=true` (obligatoire pour ce projet), les fichiers sous `_framework/` sont des modules Webcil (`.wasm`), **pas** des PE managés.
Le Precompiler copie les DLLs managées du répertoire de build (`bin/Debug/net10.0/`) vers `wwwroot/ref/` sous l'extension `.bin` pour les servir comme assets statiques.

**`System.Private.CoreLib` DOIT être inclus** dans les refs — les facades BCL (`System.Runtime`, `System.Threading.Tasks`…) utilisent `TypeForwardedTo` pour rediriger les types vers CoreLib. Sans lui, Roslyn ne peut pas résoudre les types fondamentaux (`int`, `string`, `Task<>`) et émet des erreurs CS0012/CS0518.

Le fichier `ref/manifest.txt` liste les assemblies disponibles. Le `CodeExecutionService` charge les refs depuis ce répertoire en priorité, puis tombe en fallback sur `_framework/` (qui ne fonctionne qu'en mode non-natif).

Pour charger les assemblies Webcil depuis `_framework/` (approche alternative utilisée par [FluentMigrator.Repl.Poc](https://github.com/PhenX/FluentMigrator.Repl.Poc)), il faut implémenter un convertisseur Webcil→PE.

---

## Points d'entrée

| Fichier | Rôle |
|---------|------|
| `Program.cs` | Bootstrap Blazor WASM, switch Npgsql, enregistrement des services |
| `Pages/Playground.razor` | Page principale, orchestre éditeur + exécution + résultats |
| `ClientApp/main.js` | Point d'entrée Vite (Monaco, Bootstrap, interops JS) |
| `ClientApp/pglite-interop.js` | Pont JS → PGlite (`init`, `query`) |
| `ClientApp/monaco-interop.js` | Pont JS → Monaco Editor (`initialize`, `getValue`, `setValue`, `setMarkers`, `clearMarkers`, `disposeSqlViewer`) |
| `ClientApp/cache-interop.js` | Pont JS → localStorage (`save`, `loadAll`, `clear`) |
| `wwwroot/index.html` | Page hôte Blazor |
| `Models/ExampleSnippets.cs` | Source unique des exemples |
| `Models/PlaygroundDbContext.cs` | DbContext + seed data + `AnnotateLastQueryWithTracking()` |
