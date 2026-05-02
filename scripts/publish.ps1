#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Build et publie EF Core Playground pour un hébergement statique (GitHub Pages, Netlify, Vercel…).

.DESCRIPTION
    1. Installe les dépendances npm (si nécessaire)
    2. Lance `dotnet publish -c Release`
    3. Ajoute .nojekyll et 404.html
    4. Optionnel : remplace le <base href> si un sous-chemin est fourni

.PARAMETER OutputDir
    Dossier de sortie pour le publish (défaut : dist/ à la racine du repo)

.PARAMETER BasePath
    Sous-chemin de déploiement (ex: /ef-core-playground/).
    Laissez vide pour un déploiement à la racine (/).

.EXAMPLE
    .\scripts\publish.ps1
    .\scripts\publish.ps1 -OutputDir "C:\deploy\out" -BasePath "/ef-core-playground/"
#>

param(
    [string]$OutputDir = "",
    [string]$BasePath = ""
)

$ErrorActionPreference = "Stop"

# --- Chemins ---
$RepoRoot    = (Resolve-Path "$PSScriptRoot\..").Path
$ProjectDir  = Join-Path $RepoRoot "src\EfCorePlayground"
$ProjectFile = Join-Path $ProjectDir "EfCorePlayground.csproj"

if (-not $OutputDir) {
    $OutputDir = Join-Path $RepoRoot "dist"
}
$WwwrootDir = Join-Path $OutputDir "wwwroot"

Write-Host ""
Write-Host "=== EF Core Playground — Publication ===" -ForegroundColor Cyan
Write-Host "  Projet    : $ProjectFile"
Write-Host "  Sortie    : $WwwrootDir"
if ($BasePath) {
    Write-Host "  Base href : $BasePath" -ForegroundColor Yellow
}
Write-Host ""

# --- 1. npm ci (si node_modules absent ou package-lock.json plus récent) ---
$NodeModules = Join-Path $ProjectDir "node_modules"
$PackageLock = Join-Path $ProjectDir "package-lock.json"

if (-not (Test-Path $NodeModules) -or `
    ((Get-Item $PackageLock).LastWriteTime -gt (Get-Item $NodeModules).LastWriteTime)) {
    Write-Host ">>> npm ci" -ForegroundColor DarkCyan
    Push-Location $ProjectDir
    npm ci
    if ($LASTEXITCODE -ne 0) { throw "npm ci a échoué (code $LASTEXITCODE)" }
    Pop-Location
} else {
    Write-Host ">>> node_modules à jour, npm ci ignoré." -ForegroundColor DarkGray
}

# --- 2. dotnet publish ---
Write-Host ""
Write-Host ">>> dotnet publish -c Release" -ForegroundColor DarkCyan
dotnet publish $ProjectFile -c Release -o $OutputDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish a échoué (code $LASTEXITCODE)" }

# --- 3. Remplacement du <base href> (si sous-chemin fourni) ---
$IndexHtml = Join-Path $WwwrootDir "index.html"
if ($BasePath) {
    if (-not $BasePath.StartsWith("/")) { $BasePath = "/$BasePath" }
    if (-not $BasePath.EndsWith("/"))   { $BasePath = "$BasePath/" }

    Write-Host ""
    Write-Host ">>> Mise à jour de <base href> → $BasePath" -ForegroundColor DarkCyan
    $content = Get-Content $IndexHtml -Raw
    $content = $content -replace '<base href="[^"]*" />', "<base href=`"$BasePath`" />"
    Set-Content $IndexHtml $content -NoNewline
}

# --- 4. .nojekyll (GitHub Pages ignore les dossiers _framework sans ce fichier) ---
$NoJekyll = Join-Path $WwwrootDir ".nojekyll"
if (-not (Test-Path $NoJekyll)) {
    Write-Host ""
    Write-Host ">>> Création de .nojekyll" -ForegroundColor DarkCyan
    New-Item -ItemType File -Path $NoJekyll | Out-Null
}

# --- 5. 404.html (routing SPA) ---
$NotFound = Join-Path $WwwrootDir "404.html"
Write-Host ""
Write-Host ">>> Copie index.html → 404.html (routing SPA)" -ForegroundColor DarkCyan
Copy-Item $IndexHtml $NotFound -Force

# --- Résultat ---
Write-Host ""
Write-Host "=== Publication terminée avec succès ===" -ForegroundColor Green
Write-Host "  Dossier à déployer : $WwwrootDir"
Write-Host ""

