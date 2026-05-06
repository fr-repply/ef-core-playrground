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
            """
            return await db.Blogs
                .Where(b => b.Rating >= 4)
                .OrderByDescending(b => b.Rating)
                .ToListAsync();
            """),

        new("Posts avec leur auteur",
            """
            return await db.Posts
                .Include(p => p.Author)
                .Select(p => new { p.Title, Auteur = p.Author.Name, p.PublishedDate })
                .ToListAsync();
            """),

        new("Nombre de posts par blog",
            """
            return await db.Blogs
                .Select(b => new { Blog = b.Name, NbPosts = b.Posts.Count() })
                .OrderByDescending(x => x.NbPosts)
                .ToListAsync();
            """),

        new("Posts avec tags (Many-to-Many)",
            """
            return await db.Posts
                .Include(p => p.Tags)
                .Select(p => new { p.Title, p.Tags })
                .ToListAsync();
            """),

        new("Recherche par mot-clé",
            """
            return await db.Posts
                .Where(p => p.Title.Contains("EF Core"))
                .Select(p => new { p.Title, p.PublishedDate })
                .ToListAsync();
            """),

        new("Groupement par auteur",
            """
            return await db.Authors
                .Select(a => new {
                    Auteur = a.Name,
                    NbPosts = a.Posts.Count(),
                    DernierePublication = a.Posts.Max(p => (DateTime?)p.PublishedDate),
                    NbBlogs = a.Posts.Select(p => p.BlogId).Distinct().Count()
                })
                .ToListAsync();
            """),

        new("Top 2 posts par auteur",
            """
            return await db.Authors
                .SelectMany(
                    a => a.Posts.OrderByDescending(p => p.PublishedDate).Take(2),
                    (a, p) => new { Auteur = a.Name, p.Title, p.PublishedDate })
                .ToListAsync();
            """),

        new("Top 5 posts récents",
            """
            return await db.Posts
                .OrderByDescending(p => p.PublishedDate)
                .Take(5)
                .Select(p => new { p.Title, p.PublishedDate, Blog = p.Blog.Name })
                .ToListAsync();
            """),

        new("✏️ INSERT : Ajouter un blog",
            """
            // Ajouter un nouveau blog en base
            var newBlog = new Blog
            {
                Name = "Mon Nouveau Blog",
                Url = "https://mon-blog.example.com",
                Rating = 5,
                CreatedAt = DateTime.UtcNow
            };
            db.Blogs.Add(newBlog);
            await db.SaveChangesAsync();

            // Vérifier l'insertion
            return await db.Blogs
                .OrderByDescending(b => b.BlogId)
                .Select(b => new { b.BlogId, b.Name, b.Url, b.Rating })
                .ToListAsync();
            """),

        new("✏️ UPDATE : Modifier un blog",
            """
            // Modifier le rating d'un blog existant
            var blog = await db.Blogs.FirstAsync(b => b.Name == "Web Dev Tips");
            blog.Rating = 5;
            blog.Name = "Web Dev Tips (Mis à jour)";
            await db.SaveChangesAsync();

            // Vérifier la modification
            return await db.Blogs
                .Select(b => new { b.BlogId, b.Name, b.Rating })
                .ToListAsync();
            """),

        new("✏️ DELETE : Supprimer un blog",
            """
            // Supprimer un blog (et ses posts en cascade)
            var blog = await db.Blogs
                .Include(b => b.Posts)
                .FirstAsync(b => b.Name == "Web Dev Tips");
            db.Blogs.Remove(blog);
            await db.SaveChangesAsync();

            // Vérifier la suppression
            return await db.Blogs
                .Select(b => new { b.BlogId, b.Name })
                .ToListAsync();
            """),

        new("✏️ INSERT multiple + Transaction",
            """
            // Insérer plusieurs entités en une transaction
            var author = new Author
            {
                Name = "Nouvel Auteur",
                Email = "nouveau@example.com",
                Bio = "Développeur passionné"
            };

            var blog = new Blog
            {
                Name = "Blog Fresh",
                Url = "https://fresh.example.com",
                Rating = 4,
                CreatedAt = DateTime.UtcNow
            };

            db.Authors.Add(author);
            db.Blogs.Add(blog);
            await db.SaveChangesAsync();

            // Ajouter un post lié
            var post = new Post
            {
                Title = "Premier article",
                Content = "Contenu du premier article...",
                PublishedDate = DateTime.UtcNow,
                Blog = blog,
                Author = author
            };
            db.Posts.Add(post);
            await db.SaveChangesAsync();

            return await db.Posts
                .Where(p => p.Author.Name == "Nouvel Auteur")
                .Select(p => new { p.Title, Blog = p.Blog.Name, Auteur = p.Author.Name })
                .ToListAsync();
            """),

        new("⚡ Projectable: Blogs populaires",
            """
            // Utilise la propriété [Projectable] IsPopular
            // traduite automatiquement en SQL
            return await db.Blogs
                .Where(b => b.IsPopular)
                .Select(b => new { b.Name, b.Rating, b.PostCount })
                .ToListAsync();
            """),

        new("⚡ Projectable: Auteurs productifs",
            """
            // Utilise la propriété [Projectable] IsProductive
            // (auteurs avec 3+ posts)
            return await db.Authors
                .Where(a => a.IsProductive)
                .Select(a => new { a.Name, a.PostCount, a.Email })
                .ToListAsync();
            """),

        new("⚡ Projectable: Posts récents avec tags",
            """
            // Utilise les propriétés [Projectable] IsRecent et TagCount
            return await db.Posts
                .Where(p => p.IsRecent)
                .Select(p => new { p.Title, p.TagCount, p.PublishedDate, Blog = p.Blog.Name })
                .OrderByDescending(p => p.PublishedDate)
                .ToListAsync();
            """),

        // ── Pièges de performance ────────────────────────────────────────

        new("⚠️ Piège N+1 — boucle vs projection",
            """
            // ❌ N+1 : 1 requête pour charger les blogs + 1 CountAsync PAR blog
            // → ici 4 blogs = 5 requêtes SQL au total !
            var blogs = await db.Blogs
                .TagWith("❌ Requête 1/N — charge tous les blogs")
                .ToListAsync();
            var resultN1 = new List<object>();
            foreach (var blog in blogs)
            {
                var nbPosts = await db.Posts
                    .TagWith("❌ Requête N+1 — 1 par blog")
                    .CountAsync(p => p.BlogId == blog.BlogId);
                resultN1.Add(new { blog.Name, NbPosts = nbPosts });
            }

            // ✅ 1 seule requête avec sous-requête COUNT intégrée
            var resultOk = await db.Blogs
                .TagWith("✅ 1 requête — sous-requête COUNT intégrée")
                .Select(b => new { b.Name, NbPosts = b.Posts.Count() })
                .ToListAsync();

            // Panneau SQL : 5 requêtes ❌ vs 1 requête ✅ — même résultat !
            return resultOk;
            """),

        new("⚠️ Piège ToList() trop tôt — évaluation client",
            """
            // ❌ PROBLÈME : ToList() rapatrie TOUTE la table en mémoire avant de filtrer
            // Le Where s'applique en C#, pas en SQL ("client evaluation")
            var tousLesPosts = await db.Posts
                .TagWith("❌ SELECT * sans filtre — toute la table en mémoire !")
                .ToListAsync();
            var recentsCote = tousLesPosts                        // filtre C#, pas SQL
                .Where(p => p.PublishedDate > DateTime.UtcNow.AddMonths(-6))
                .Select(p => new { p.Title, p.PublishedDate })
                .ToList();

            // ✅ SOLUTION : composer la requête complète avant d'exécuter
            var recentsSQL = await db.Posts
                .TagWith("✅ WHERE en SQL — seules les lignes utiles sont transférées")
                .Where(p => p.PublishedDate > DateTime.UtcNow.AddMonths(-6))
                .Select(p => new { p.Title, p.PublishedDate })
                .ToListAsync();

            // Regardez le panneau SQL : 1re requête ramène tout, 2e est filtrée
            return recentsSQL;
            """),

        new("⚠️ Piège Count() > 0 — utiliser Any()",
            """
            // Cas concret : vérifier si la table contient au moins 1 post
            //
            // ❌ CountAsync() : agrège TOUTES les lignes pour obtenir le total
            // même si on veut juste savoir s'il en existe une !
            var total = await db.Posts
                .TagWith("❌ CountAsync() — SELECT COUNT(*) sur toute la table")
                .CountAsync();
            var existeAvecCount = total > 0;

            // ✅ AnyAsync() : génère EXISTS(...), s'arrête à la 1ère ligne trouvée
            var existeAvecAny = await db.Posts
                .TagWith("✅ AnyAsync() — SELECT EXISTS(...), s'arrête immédiatement")
                .AnyAsync();

            // Regardez le SQL : COUNT(*) vs EXISTS — même résultat, coût très différent
            return new { AvecCount = existeAvecCount, AvecAny = existeAvecAny };
            """),

        new("⚠️ Piège cartésien — AsSplitQuery()",
            """
            // ❌ Multi-Include sans AsSplitQuery → 1 JOIN géant
            // EF Core génère Blogs INNER JOIN Posts INNER JOIN PostTag INNER JOIN Tags
            // Les lignes sont dupliquées : 1 ligne par combinaison Blog×Post×Tag
            var avecCartesien = await db.Blogs
                .TagWith("❌ JOIN classique — lignes dupliquées (produit cartésien)")
                .Include(b => b.Posts)
                    .ThenInclude(p => p.Tags)
                .ToListAsync(); // Pas de .Select() → Include() est respecté

            // ✅ AsSplitQuery() : 3 requêtes SQL simples et ciblées
            // Blogs, Posts et Tags chargés séparément — pas de duplication
            var avecSplit = await db.Blogs
                .TagWith("✅ AsSplitQuery() — requête 1/3 : Blogs")
                .AsSplitQuery()
                .Include(b => b.Posts)
                    .ThenInclude(p => p.Tags)
                .ToListAsync();

            // Panneau SQL : 1 requête avec JOIN ❌ vs 3 requêtes séparées ✅
            return avecSplit.Select(b => new
            {
                b.Name,
                NbPosts = b.Posts.Count,
                NbTags = b.Posts.Sum(p => p.Tags.Count)
            }).ToList();
            """),

        new("⚠️ Piège tracking — AsNoTracking()",
            """
            // ❌ Sans AsNoTracking : EF Core snapshot chaque entité chargée
            // Le badge du panneau SQL affichera le nombre d'entités en mémoire
            var avecTracking = await db.Posts
                .TagWith("❌ Avec tracking — EF Core snapshots les entités")
                .Take(5)
                .ToListAsync();
            db.AnnotateLastQueryWithTracking(); // → enregistre le nb d'entités trackées dans le badge
            db.ChangeTracker.Clear();

            // ✅ AsNoTracking() : EF Core ne snapshot pas → aucune entité en mémoire
            var sansTracking = await db.Posts
                .TagWith("✅ AsNoTracking() — lecture pure, aucun snapshot")
                .AsNoTracking()
                .Take(5)
                .ToListAsync();
            db.AnnotateLastQueryWithTracking(); // → enregistre 0 entité trackée dans le badge

            // SQL identique → regardez les badges : 5 entités trackées ❌ vs 0 ✅
            return sansTracking.Select(p => new { p.Title, p.PublishedDate }).ToList();
            """),

        new("⚠️ Piège pagination sans OrderBy",
            """
            // ❌ Skip/Take sans OrderBy → ordre indéterminé par PostgreSQL
            // Les résultats peuvent changer à chaque exécution !
            var sansTri = await db.Posts
                .TagWith("❌ Pagination sans ORDER BY — ordre non garanti")
                .Skip(0)
                .Take(3)
                .Select(p => new { p.Title, p.PublishedDate })
                .ToListAsync();

            // ✅ OrderBy stable + ThenBy comme tie-breaker sur la PK
            // Résultats déterministes + index SQL exploitable
            var avecTri = await db.Posts
                .TagWith("✅ ORDER BY + ThenBy(PK) — pagination déterministe")
                .OrderByDescending(p => p.PublishedDate)
                .ThenBy(p => p.PostId)   // tie-breaker unique
                .Skip(0)
                .Take(3)
                .Select(p => new { p.Title, p.PublishedDate })
                .ToListAsync();

            // Comparez les SQL : le 2e a un ORDER BY explicite
            return avecTri;
            """),

        // ── Projectables ─────────────────────────────────────────────────

        new("⚡ Projectable custom (extension)",
            """
            using System;
            using System.Collections.Generic;
            using System.Linq;
            using System.Threading.Tasks;
            using Microsoft.EntityFrameworkCore;
            using EntityFrameworkCore.Projectables;
            using EfCorePlayground.Models;

            namespace EfCorePlayground.UserCode
            {
                public static class BlogExtensions
                {
                    [Projectable]
                    public static int TotalPosts(this Blog b) => b.Posts.Count;
                }

                public static class UserQuery
                {
                    public static async Task<object?> Execute(PlaygroundDbContext db)
                    {
                        // Utilise une extension [Projectable] définie par l'utilisateur
                        return await db.Blogs
                            .Select(b => new { b.Name, Total = b.TotalPosts() })
                            .ToListAsync();
                    }
                }
            }
            """,
            IsFullCode: true),
    ];
}

