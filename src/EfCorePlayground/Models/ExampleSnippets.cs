using System.Diagnostics.CodeAnalysis;

namespace EfCorePlayground.Models;

/// <summary>
/// Catalogue of example snippets shown in ExamplesPanel and used for cache pre-warming.
/// </summary>
public static class ExampleSnippets
{
    public record Example(string Title, [StringSyntax("C#")] string Code, bool IsFullCode = false);

    public static readonly Example[] All =
    [
        new("Lister tous les blogs",
            "return await db.Blogs.ToListAsync();"),

        new("Blogs avec rating ≥ 4",
            "return await db.Blogs\n    .Where(b => b.Rating >= 4)\n    .OrderByDescending(b => b.Rating)\n    .ToListAsync();"),

        new("Posts avec leur auteur",
            "return await db.Posts\n    .Include(p => p.Author)\n    .Select(p => new { p.Title, Auteur = p.Author.Name, p.PublishedDate })\n    .ToListAsync();"),

        new("Nombre de posts par blog",
            "return await db.Blogs\n    .Select(b => new { Blog = b.Name, NbPosts = b.Posts.Count() })\n    .OrderByDescending(x => x.NbPosts)\n    .ToListAsync();"),

        new("Posts avec tags (Many-to-Many)",
            "return await db.Posts\n    .Include(p => p.Tags)\n    .Select(p => new { p.Title, Tags = string.Join(\", \", p.Tags.Select(t => t.Name)) })\n    .ToListAsync();"),

        new("Recherche par mot-clé",
            "return await db.Posts\n    .Where(p => p.Title.Contains(\"EF Core\"))\n    .Select(p => new { p.Title, p.PublishedDate })\n    .ToListAsync();"),

        new("Groupement par auteur",
            "return await db.Authors\n    .Select(a => new {\n        Auteur = a.Name,\n        NbPosts = a.Posts.Count(),\n        DernierePublication = a.Posts.Max(p => (DateTime?)p.PublishedDate),\n        NbBlogs = a.Posts.Select(p => p.BlogId).Distinct().Count()\n    })\n    .ToListAsync();"),

        new("Top 2 posts par auteur (LATERAL)",
            "// Requête utilisant les jointures latérales (PostgreSQL)\n// Impossible sur SQLite, fonctionne grâce à PGlite !\nreturn await db.Authors\n    .SelectMany(\n        a => a.Posts.OrderByDescending(p => p.PublishedDate).Take(2),\n        (a, p) => new { Auteur = a.Name, p.Title, p.PublishedDate })\n    .ToListAsync();"),

        new("Top 5 posts récents",
            "return await db.Posts\n    .OrderByDescending(p => p.PublishedDate)\n    .Take(5)\n    .Select(p => new { p.Title, p.PublishedDate, Blog = p.Blog.Name })\n    .ToListAsync();"),

        new("✏️ INSERT : Ajouter un blog",
            "// Ajouter un nouveau blog en base\nvar newBlog = new Blog\n{\n    Name = \"Mon Nouveau Blog\",\n    Url = \"https://mon-blog.example.com\",\n    Rating = 5,\n    CreatedAt = DateTime.UtcNow\n};\ndb.Blogs.Add(newBlog);\nawait db.SaveChangesAsync();\n\n// Vérifier l'insertion\nreturn await db.Blogs\n    .OrderByDescending(b => b.BlogId)\n    .Select(b => new { b.BlogId, b.Name, b.Url, b.Rating })\n    .ToListAsync();"),

        new("✏️ UPDATE : Modifier un blog",
            "// Modifier le rating d'un blog existant\nvar blog = await db.Blogs.FirstAsync(b => b.Name == \"Web Dev Tips\");\nblog.Rating = 5;\nblog.Name = \"Web Dev Tips (Mis à jour)\";\nawait db.SaveChangesAsync();\n\n// Vérifier la modification\nreturn await db.Blogs\n    .Select(b => new { b.BlogId, b.Name, b.Rating })\n    .ToListAsync();"),

        new("✏️ DELETE : Supprimer un blog",
            "// Supprimer un blog (et ses posts en cascade)\nvar blog = await db.Blogs\n    .Include(b => b.Posts)\n    .FirstAsync(b => b.Name == \"Web Dev Tips\");\ndb.Blogs.Remove(blog);\nawait db.SaveChangesAsync();\n\n// Vérifier la suppression\nreturn await db.Blogs\n    .Select(b => new { b.BlogId, b.Name })\n    .ToListAsync();"),

        new("✏️ INSERT multiple + Transaction",
            "// Insérer plusieurs entités en une transaction\nvar author = new Author\n{\n    Name = \"Nouvel Auteur\",\n    Email = \"nouveau@example.com\",\n    Bio = \"Développeur passionné\"\n};\n\nvar blog = new Blog\n{\n    Name = \"Blog Fresh\",\n    Url = \"https://fresh.example.com\",\n    Rating = 4,\n    CreatedAt = DateTime.UtcNow\n};\n\ndb.Authors.Add(author);\ndb.Blogs.Add(blog);\nawait db.SaveChangesAsync();\n\n// Ajouter un post lié\nvar post = new Post\n{\n    Title = \"Premier article\",\n    Content = \"Contenu du premier article...\",\n    PublishedDate = DateTime.UtcNow,\n    Blog = blog,\n    Author = author\n};\ndb.Posts.Add(post);\nawait db.SaveChangesAsync();\n\nreturn await db.Posts\n    .Where(p => p.Author.Name == \"Nouvel Auteur\")\n    .Select(p => new { p.Title, Blog = p.Blog.Name, Auteur = p.Author.Name })\n    .ToListAsync();"),

        new("⚡ Projectable: Blogs populaires",
            "// Utilise la propriété [Projectable] IsPopular\n// traduite automatiquement en SQL\nreturn await db.Blogs\n    .Where(b => b.IsPopular)\n    .Select(b => new { b.Name, b.Rating, b.PostCount })\n    .ToListAsync();"),

        new("⚡ Projectable: Auteurs productifs",
            "// Utilise la propriété [Projectable] IsProductive\n// (auteurs avec 3+ posts)\nreturn await db.Authors\n    .Where(a => a.IsProductive)\n    .Select(a => new { a.Name, a.PostCount, a.Email })\n    .ToListAsync();"),

        new("⚡ Projectable: Posts récents avec tags",
            "// Utilise les propriétés [Projectable] IsRecent et TagCount\nreturn await db.Posts\n    .Where(p => p.IsRecent)\n    .Select(p => new { p.Title, p.TagCount, p.PublishedDate, Blog = p.Blog.Name })\n    .OrderByDescending(p => p.PublishedDate)\n    .ToListAsync();"),

        // ── Pièges de performance ────────────────────────────────────────

        new("⚠️ Piège N+1 — Include vs projection",
            "// ❌ PROBLÈME (sans lazy loading) : accéder à blog.Posts non chargé\n// retourne une collection vide. Avec lazy loading activé, déclenche\n// 1 requête SQL PAR blog → N+1 requêtes au total !\n//\n// ✅ SOLUTION A — Include() : 1 JOIN, toutes les données en une requête\nvar avecInclude = await db.Blogs\n    .TagWith(\"✅ Include() — 1 requête avec JOIN\")\n    .Include(b => b.Posts)\n    .Select(b => new { b.Name, NbPosts = b.Posts.Count })\n    .ToListAsync();\n\n// ✅ SOLUTION B (meilleure) — projection directe sans charger les entités\nvar avecProjection = await db.Blogs\n    .TagWith(\"✅ Projection directe — sous-requête COUNT, 0 entité chargée\")\n    .Select(b => new { b.Name, NbPosts = b.Posts.Count() })\n    .ToListAsync();\n\n// Comparez les deux SQL dans le panneau → même résultat, requêtes différentes\nreturn avecProjection;"),

        new("⚠️ Piège ToList() trop tôt — évaluation client",
            "// ❌ PROBLÈME : ToList() rapatrie TOUTE la table en mémoire avant de filtrer\n// Le Where s'applique en C#, pas en SQL (\"client evaluation\")\nvar tousLesPosts = await db.Posts\n    .TagWith(\"❌ SELECT * sans filtre — toute la table en mémoire !\")\n    .ToListAsync();\nvar recentsCote = tousLesPosts                        // filtre C#, pas SQL\n    .Where(p => p.PublishedDate > DateTime.UtcNow.AddMonths(-6))\n    .Select(p => new { p.Title, p.PublishedDate })\n    .ToList();\n\n// ✅ SOLUTION : composer la requête complète avant d'exécuter\nvar recentsSQL = await db.Posts\n    .TagWith(\"✅ WHERE en SQL — seules les lignes utiles sont transférées\")\n    .Where(p => p.PublishedDate > DateTime.UtcNow.AddMonths(-6))\n    .Select(p => new { p.Title, p.PublishedDate })\n    .ToListAsync();\n\n// Regardez le panneau SQL : 1re requête ramène tout, 2e est filtrée\nreturn recentsSQL;"),

        new("⚠️ Piège Count() > 0 — utiliser Any()",
            "// ❌ Count() > 0 : PostgreSQL exécute COUNT(*) sur tous les enregistrements\nvar avecCount = await db.Blogs\n    .TagWith(\"❌ Count() > 0 — parcourt TOUTE la table pour compter\")\n    .Where(b => b.Posts.Count() > 0)\n    .Select(b => new { b.Name })\n    .ToListAsync();\n\n// ✅ Any() : génère EXISTS(...) → s'arrête à la 1re correspondance\nvar avecAny = await db.Blogs\n    .TagWith(\"✅ Any() — EXISTS(...) s'arrête dès la 1re ligne trouvée\")\n    .Where(b => b.Posts.Any())\n    .Select(b => new { b.Name })\n    .ToListAsync();\n\n// Comparez les deux SQL dans le panneau : COUNT(*) vs EXISTS\nreturn avecAny;"),

        new("⚠️ Piège cartésien — AsSplitQuery()",
            "// ❌ Include() sur plusieurs collections → produit cartésien\n// 4 blogs × N posts × M tags = lignes explosives transférées\nvar avecCartesien = await db.Blogs\n    .TagWith(\"❌ Multi-Include — produit cartésien Blogs × Posts × Tags\")\n    .Include(b => b.Posts).ThenInclude(p => p.Tags)\n    .Include(b => b.Posts).ThenInclude(p => p.Author)\n    .Select(b => new { b.Name, NbPosts = b.Posts.Count })\n    .ToListAsync();\n\n// ✅ AsSplitQuery() : EF Core envoie N requêtes séparées ciblées\n// Pas de cartésien, chaque relation est chargée indépendamment\nvar avecSplit = await db.Blogs\n    .TagWith(\"✅ AsSplitQuery() — requêtes séparées, pas de cartésien\")\n    .AsSplitQuery()\n    .Include(b => b.Posts).ThenInclude(p => p.Tags)\n    .Include(b => b.Posts).ThenInclude(p => p.Author)\n    .Select(b => new { b.Name, NbPosts = b.Posts.Count })\n    .ToListAsync();\n\n// Panneau SQL : voyez les requêtes multiples de AsSplitQuery\nreturn avecSplit;"),

        new("⚠️ Piège tracking — AsNoTracking()",
            "// ❌ Sans AsNoTracking : EF Core crée un snapshot de chaque entité\n// pour détecter les changements → surcoût mémoire et CPU inutile\nvar avecTracking = await db.Posts\n    .TagWith(\"❌ Avec tracking — snapshot mémoire de chaque entité\")\n    .Select(p => new { p.Title, p.PublishedDate })\n    .ToListAsync();\n\n// ✅ AsNoTracking() : aucun snapshot, lecture pure\n// Gain significatif sur les grosses lectures (reporting, affichage)\nvar sansTracking = await db.Posts\n    .TagWith(\"✅ AsNoTracking() — lecture pure, 0 overhead de tracking\")\n    .AsNoTracking()\n    .Select(p => new { p.Title, p.PublishedDate })\n    .ToListAsync();\n\n// Le SQL est identique — la différence est dans l'overhead C# côté client\nreturn sansTracking;"),

        new("⚠️ Piège pagination sans OrderBy",
            "// ❌ Skip/Take sans OrderBy → ordre indéterminé par PostgreSQL\n// Les résultats peuvent changer à chaque exécution !\nvar sansTri = await db.Posts\n    .TagWith(\"❌ Pagination sans ORDER BY — ordre non garanti\")\n    .Skip(0)\n    .Take(3)\n    .Select(p => new { p.Title, p.PublishedDate })\n    .ToListAsync();\n\n// ✅ OrderBy stable + ThenBy comme tie-breaker sur la PK\n// Résultats déterministes + index SQL exploitable\nvar avecTri = await db.Posts\n    .TagWith(\"✅ ORDER BY + ThenBy(PK) — pagination déterministe\")\n    .OrderByDescending(p => p.PublishedDate)\n    .ThenBy(p => p.PostId)   // tie-breaker unique\n    .Skip(0)\n    .Take(3)\n    .Select(p => new { p.Title, p.PublishedDate })\n    .ToListAsync();\n\n// Comparez les SQL : le 2e a un ORDER BY explicite\nreturn avecTri;"),

        // ── Projectables ─────────────────────────────────────────────────

        new("⚡ Projectable custom (extension)",
            "using System;\nusing System.Collections.Generic;\nusing System.Linq;\nusing System.Threading.Tasks;\nusing Microsoft.EntityFrameworkCore;\nusing EntityFrameworkCore.Projectables;\nusing EfCorePlayground.Models;\n\nnamespace EfCorePlayground.UserCode\n{\n    public static class BlogExtensions\n    {\n        [Projectable]\n        public static int TotalPosts(this Blog b) => b.Posts.Count;\n    }\n\n    public static class UserQuery\n    {\n        public static async Task<object?> Execute(PlaygroundDbContext db)\n        {\n            // Utilise une extension [Projectable] définie par l'utilisateur\n            return await db.Blogs\n                .Select(b => new { b.Name, Total = b.TotalPosts() })\n                .ToListAsync();\n        }\n    }\n}",
            IsFullCode: true),
    ];
}

