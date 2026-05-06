# EF Core 10 Playground 🎮

Un environnement interactif de type REPL pour **Entity Framework Core 10**, entièrement dans le navigateur.

Écrivez des requêtes LINQ, compilez-les avec Roslyn, exécutez-les sur une base SQLite — le tout via WebAssembly, sans serveur.

## ✨ Fonctionnalités

- 🖊️ **Monaco Editor** — Éditeur de code riche (même moteur que VS Code)
- ⚡ **Compilation C# en temps réel** — Roslyn compile votre code dans le navigateur
- 🗄️ **Base de données pré-seedée** — 4 blogs, 3 auteurs, 10 posts, 8 tags
- 📊 **Affichage tabulaire** — Résultats présentés sous forme de tableau
- 🎓 **Exemples intégrés** — 8 requêtes LINQ prêtes à l'emploi
- 🌐 **100% statique** — Déployable sur GitHub Pages, Netlify, Vercel

## 🚀 Démarrage rapide

```bash
# Prérequis : .NET 10 SDK + Node.js 18+
cd src/EfCorePlayground
npm install     # Monaco Editor, Bootstrap (via Vite)
dotnet run      # Build Vite + serveur Blazor
# Ouvrir http://localhost:5000
```

## 🧪 Tests

```bash
cd tests/e2e
npm install
npx playwright install chromium
npx playwright test
```

## 📖 Documentation

Voir [INSTRUCTIONS.md](INSTRUCTIONS.md) pour la documentation complète, l'architecture, les bonnes pratiques et le guide de déploiement.