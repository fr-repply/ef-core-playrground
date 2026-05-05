// Build-time precompiler: compiles every example snippet with Roslyn and saves the resulting
// assembly bytes as  wwwroot/precompiled/{hash}.dll  so the WASM runtime can fetch them as
// plain static files instead of running Roslyn on the first execution.
//
// Usage:
//   dotnet run --project tools/EfCorePlayground.Precompiler --
//       <EfCorePlayground.dll path>
//       <output directory>
//       [<EntityFrameworkCore.Projectables.Generator.dll path>]

using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using EfCorePlayground.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.EntityFrameworkCore;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: precompiler <EfCorePlayground.dll> <output-dir> [<Projectables.Generator.dll>] [<ref-output-dir>]");
    return 1;
}

var efDllPath      = args[0];
var outputDir      = args[1];
var generatorPath  = args.Length > 2 ? args[2] : null;
var refOutputDir   = args.Length > 3 ? args[3] : null;

if (!File.Exists(efDllPath))
{
    Console.Error.WriteLine($"[Precompiler] EfCorePlayground.dll not found: {efDllPath}");
    return 1;
}

Directory.CreateDirectory(outputDir);
Console.WriteLine($"[Precompiler] Output → {outputDir}");

var references = BuildReferences(efDllPath);

// Load Projectables generator if available
IIncrementalGenerator? projectablesGen = null;
#pragma warning disable CS0618
ISourceGenerator?      projectablesSrc = null;
#pragma warning restore CS0618

if (generatorPath != null && File.Exists(generatorPath))
{
    try
    {
        var genAsm  = Assembly.LoadFile(generatorPath);
        var genType = genAsm.GetType("EntityFrameworkCore.Projectables.ProjectionExpressionGenerator")
                      ?? genAsm.GetTypes().FirstOrDefault(t => t.Name == "ProjectionExpressionGenerator");

        if (genType != null)
        {
            var inst = Activator.CreateInstance(genType);
            projectablesGen = inst as IIncrementalGenerator;
#pragma warning disable CS0618
            projectablesSrc = inst as ISourceGenerator;
#pragma warning restore CS0618
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[Precompiler] Could not load generator: {ex.Message}");
    }
}

var compiled = 0;
var skipped  = 0;
var failed   = 0;

foreach (var example in ExampleSnippets.All)
{
    var fullCode = example.IsFullCode ? example.Code : BuildFullCode(example.Code);
    var hash     = ComputeHash(fullCode);
    var outPath  = Path.Combine(outputDir, $"{hash}.dll");

    if (File.Exists(outPath))
    {
        Console.WriteLine($"[Precompiler] SKIP  {example.Title}  ({hash[..8]}…)");
        skipped++;
        continue;
    }

    var comp = CreateCompilation(fullCode, references);

    // Run Projectables generator if the snippet defines [Projectable]
    if (fullCode.Contains("[Projectable]") || fullCode.Contains("[Projectable("))
        comp = RunGenerator(comp, projectablesGen, projectablesSrc);

    using var ms = new MemoryStream();
    var emit = comp.Emit(ms);

    if (!emit.Success)
    {
        foreach (var d in emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error))
            Console.Error.WriteLine($"  ERROR in '{example.Title}': {d.GetMessage()}");
        failed++;
        continue;
    }

    File.WriteAllBytes(outPath, ms.ToArray());
    Console.WriteLine($"[Precompiler] OK    {example.Title}  ({hash[..8]}…, {ms.Length / 1024.0:F1} KB)");
    compiled++;
}

Console.WriteLine($"[Precompiler] Done — {compiled} compiled, {skipped} skipped, {failed} failed.");

// Copy BCL reference assemblies so the WASM runtime can load them from wwwroot/ref/
// and use them as Roslyn metadata references instead of the trimmed/transformed WASM ones.
if (refOutputDir != null)
    CopyBclRefAssemblies(refOutputDir, efDllPath);

return failed > 0 ? 1 : 0;







// ──────────────────────────────────────────────────────────────────────────────
//  BCL reference assembly copy  (for WASM Roslyn compilation)
// ──────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Copies the minimal set of BCL reference assemblies to <paramref name="outputDir"/> so the
/// Blazor WASM page can download them and use them as Roslyn metadata references.
///
/// Strategy — we MUST use TypeForwardedTo facades (not the SDK ref pack) because in WASM the
/// IL linker rewrites app-assembly type references to use System.Private.CoreLib identity
/// (PublicKeyToken=7cec85d7bea7798e). If we included SDK ref-pack stubs (which *directly*
/// define the same types under System.Runtime/b03f5f7f11d50a3a), Roslyn would see the same
/// fully-qualified type name in two assemblies → CS0433.
///
/// TypeForwardedTo facades (the small ~40–600 KB runtime DLLs) are safe alongside CoreLib
/// because Roslyn follows the TypeForwardedTo chain to the one canonical CoreLib definition.
///
///   1. <paramref name="efDllPath"/> directory — the exact WASM build-output DLLs; these are
///      already the correct TypeForwardedTo facades.
///   2. AppDomain assemblies of this process — the desktop .NET shared-framework facades;
///      identical TypeForwardedTo behaviour, different platform but same metadata.
///
/// The SDK reference pack is intentionally skipped: using it alongside System.Private.CoreLib
/// always causes CS0433/CS0012 for types that appear in both.
///
/// Writes a <c>manifest.txt</c> listing the assembly simple names that were copied.
/// </summary>
static void CopyBclRefAssemblies(string outputDir, string efDllPath)
{
    // All assemblies needed for Roslyn WASM compilation of user code.
    // In .NET 10, _framework/ serves native .wasm files (not managed PEs), so we copy
    // the managed DLLs from the build output to wwwroot/ref/ where the WASM runtime
    // can fetch them via HTTP for Roslyn metadata references.
    var wanted = new[]
    {
        // ── BCL ──────────────────────────────────────────────────────────────
        // System.Private.CoreLib is NOT included — the WASM runtime has it in memory.
        // Including it causes type identity conflicts between the compilation-time
        // and runtime versions of CoreLib.
        "System.Runtime",
        "System.Collections",
        "System.Collections.Concurrent",
        "System.Linq",
        "System.Linq.Expressions",
        "System.Linq.Queryable",
        "System.Threading",
        "System.Threading.Tasks",
        "System.Threading.Tasks.Extensions",
        "System.ComponentModel.Primitives",
        "System.ComponentModel.Annotations",
        "System.ComponentModel.TypeConverter",
        "System.Text.Json",
        "System.Console",
        "System.ObjectModel",
        "System.Runtime.InteropServices",
        "netstandard",
        // ── EF Core & extensions ─────────────────────────────────────────────
        "Microsoft.EntityFrameworkCore",
        "Microsoft.EntityFrameworkCore.Abstractions",
        "Microsoft.EntityFrameworkCore.Relational",
        "Microsoft.Extensions.Caching.Abstractions",
        "Microsoft.Extensions.Caching.Memory",
        "Microsoft.Extensions.Configuration.Abstractions",
        "Microsoft.Extensions.DependencyInjection.Abstractions",
        "Microsoft.Extensions.DependencyInjection",
        "Microsoft.Extensions.Logging.Abstractions",
        "Microsoft.Extensions.Logging",
        "Microsoft.Extensions.Options",
        "Microsoft.Extensions.Primitives",
        // ── Npgsql ───────────────────────────────────────────────────────────
        "Npgsql",
        "Npgsql.EntityFrameworkCore.PostgreSQL",
        // ── Projectables ─────────────────────────────────────────────────────
        "EntityFrameworkCore.Projectables",
        "EntityFrameworkCore.Projectables.Abstractions",
        // ── App ──────────────────────────────────────────────────────────────
        "EfCorePlayground",
    };

    Directory.CreateDirectory(outputDir);

    // ── 1. Build a lookup from the WASM build-output directory ───────────────
    //    These are managed PE DLLs — the input to the WASM native compiler.
    //    They form a consistent set safe to use together as Roslyn refs.
    var efDir = Path.GetDirectoryName(efDllPath) ?? "";
    var efDirByName = Directory.Exists(efDir)
        ? Directory.GetFiles(efDir, "*.dll")
              .GroupBy(p => Path.GetFileNameWithoutExtension(p), StringComparer.OrdinalIgnoreCase)
              .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase)
        : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    Console.WriteLine($"[Precompiler] REF   efDir → {efDir} ({efDirByName.Count} DLLs)");

    // ── 2. Build a fallback lookup from this process's AppDomain assemblies ──
    var runtimeByName = AppDomain.CurrentDomain.GetAssemblies()
        .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location) && File.Exists(a.Location))
        .GroupBy(a => a.GetName().Name ?? "", StringComparer.OrdinalIgnoreCase)
        .ToDictionary(g => g.Key, g => g.First().Location, StringComparer.OrdinalIgnoreCase);

    // ── 3. Copy each wanted assembly ─────────────────────────────────────────
    var copied = new List<string>();
    foreach (var name in wanted)
    {
        var destPath = Path.Combine(outputDir, name + ".bin"); // .bin avoids WASM P/Invoke scanner

        // Priority: efDir (WASM build output — consistent set) → runtime AppDomain → skip
        string? srcPath = null;
        if (efDirByName.TryGetValue(name, out var efPath))
            srcPath = efPath;
        else if (runtimeByName.TryGetValue(name, out var runtimePath))
            srcPath = runtimePath;

        if (srcPath == null)
        {
            Console.WriteLine($"[Precompiler] REF   SKIP  {name}.dll (not found in efDir or runtime)");
            continue;
        }

        // Verify it's a valid managed PE (has CLI metadata)
        if (!IsManagedAssembly(srcPath))
        {
            Console.WriteLine($"[Precompiler] REF   SKIP  {name}.dll (not a managed PE)");
            continue;
        }

        try
        {
            File.Copy(srcPath, destPath, overwrite: true);
            copied.Add(name);
            Console.WriteLine($"[Precompiler] REF   OK    {name}.bin ({new FileInfo(destPath).Length / 1024.0:F0} KB)  ← {srcPath}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Precompiler] REF   FAIL  {name}.dll — {ex.Message}");
        }
    }

    // Write manifest so the WASM service knows which files are available
    File.WriteAllLines(Path.Combine(outputDir, "manifest.txt"), copied);
    Console.WriteLine($"[Precompiler] REF   manifest.txt written ({copied.Count} entries).");
}

/// <summary>
/// Returns <c>true</c> when the file at <paramref name="path"/> is a valid managed PE
/// with CLI metadata (as opposed to a native WASM module or corrupt file).
/// </summary>
static bool IsManagedAssembly(string path)
{
    try
    {
        using var fs = File.OpenRead(path);
        using var peReader = new PEReader(fs);
        return peReader.HasMetadata;
    }
    catch
    {
        return false;
    }
}

/// <summary>
/// Returns <c>true</c> when the assembly at <paramref name="path"/> is a pure
/// TypeForwardedTo facade — i.e. its TypeDef table contains at most the implicit
/// &lt;Module&gt; row and no actual named types are defined directly.
///
/// Such facades are safe to use alongside System.Private.CoreLib in Roslyn because
/// they merely redirect type lookups via TypeForwardedTo without creating duplicate
/// definitions (which would trigger CS0433).
/// </summary>
static bool IsFacadeAssembly(string path)
{
    try
    {
        using var fs = File.OpenRead(path);
        using var peReader = new PEReader(fs);
        if (!peReader.HasMetadata) return false;

        var mr = peReader.GetMetadataReader();
        // TypeDefinitionTable row 1 is always the implicit <Module> descriptor.
        // A pure facade has NO additional rows (count == 1 or 0).
        return mr.TypeDefinitions.Count <= 1;
    }
    catch
    {
        return false; // can't read → treat as non-facade (safe default: skip)
    }
}


// ──────────────────────────────────────────────────────────────────────────────
//  Helpers
// ──────────────────────────────────────────────────────────────────────────────

static string ComputeHash(string code)
{
    var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(code));
    return Convert.ToHexString(bytes);
}

// Mirrors CodeExecutionService.BuildFullCode exactly.
static string BuildFullCode(string bodyCode)
{
    var lines   = bodyCode.Split('\n');
    var indented = string.Join("\n", lines.Select(l =>
        string.IsNullOrWhiteSpace(l) ? "" : "            " + l));

    return "using System;\n" +
           "using System.Collections.Generic;\n" +
           "using System.Linq;\n" +
           "using System.Threading.Tasks;\n" +
           "using Microsoft.EntityFrameworkCore;\n" +
           "using EntityFrameworkCore.Projectables;\n" +
           "using EfCorePlayground.Models;\n" +
           "\n" +
           "namespace EfCorePlayground.UserCode\n" +
           "{\n" +
           "    public static class UserQuery\n" +
           "    {\n" +
           "        public static async Task<object?> Execute(PlaygroundDbContext db)\n" +
           "        {\n" +
           indented + "\n" +
           "        }\n" +
           "    }\n" +
           "}";
}

static List<MetadataReference> BuildReferences(string efDllPath)
{
    var seen   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var result = new List<MetadataReference>();

    void TryAdd(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        if (!seen.Add(path)) return;
        try { result.Add(MetadataReference.CreateFromFile(path)); }
        catch { /* skip unreadable */ }
    }

    // 1. The main EfCorePlayground assembly (contains Models, PlaygroundDbContext …)
    TryAdd(efDllPath);

    // 2. All DLLs in the same output directory — includes EF Core, Npgsql and all transitive
    //    dependencies at the EXACT versions used to build EfCorePlayground.dll.
    //    This is more reliable than AppDomain scan which may load different versions.
    var efDir = Path.GetDirectoryName(efDllPath);
    if (efDir != null)
    {
        foreach (var dll in Directory.GetFiles(efDir, "*.dll"))
            TryAdd(dll);
    }

    // 3. Force-load specific assemblies that may not be in efDir (Projectables, analyzers)
    _ = typeof(System.ComponentModel.DataAnnotations.RequiredAttribute);
    _ = typeof(System.Linq.Queryable);
    _ = typeof(Microsoft.EntityFrameworkCore.DbContext);
    _ = typeof(Npgsql.NpgsqlConnection);
    _ = typeof(EntityFrameworkCore.Projectables.ProjectableAttribute);

    // 4. Remaining AppDomain assemblies (Roslyn, etc.) not already added
    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic))
        TryAdd(asm.Location);

    return result;
}

static CSharpCompilation CreateCompilation(string code, List<MetadataReference> refs, string? assemblyName = null)
{
    var tree = CSharpSyntaxTree.ParseText(code);
    // Use a unique assembly name for each compilation to avoid WASM Assembly.Load
    // conflicts when loading multiple assemblies with the same name.
    assemblyName ??= $"UserCode_{Guid.NewGuid():N}";
    return CSharpCompilation.Create(
        assemblyName,
        [tree],
        refs,
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            .WithOptimizationLevel(OptimizationLevel.Release)
            .WithPlatform(Platform.AnyCpu));
}

static CSharpCompilation RunGenerator(
    CSharpCompilation comp,
    IIncrementalGenerator? inc,
#pragma warning disable CS0618
    ISourceGenerator? src)
#pragma warning restore CS0618
{
    try
    {
        if (inc != null)
        {
            CSharpGeneratorDriver.Create(inc)
                .RunGeneratorsAndUpdateCompilation(comp, out var updated, out _);
            return (CSharpCompilation)updated;
        }

#pragma warning disable CS0618
        if (src != null)
        {
            CSharpGeneratorDriver.Create(src)
                .RunGeneratorsAndUpdateCompilation(comp, out var updated, out _);
            return (CSharpCompilation)updated;
        }
#pragma warning restore CS0618
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[Precompiler] Generator failed: {ex.Message}");
    }

    return comp;
}



