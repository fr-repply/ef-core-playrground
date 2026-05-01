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
using System.Security.Cryptography;
using System.Text;
using EfCorePlayground.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: precompiler <EfCorePlayground.dll> <output-dir> [<Projectables.Generator.dll>]");
    return 1;
}

var efDllPath      = args[0];
var outputDir      = args[1];
var generatorPath  = args.Length > 2 ? args[2] : null;

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
return failed > 0 ? 1 : 0;

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

    // 2. Force-load assemblies that may be referenced by EF Core but not yet in AppDomain
    _ = typeof(System.ComponentModel.DataAnnotations.RequiredAttribute);
    _ = typeof(System.Linq.Queryable);
    _ = typeof(EntityFrameworkCore.Projectables.ProjectableAttribute);

    // 3. All assemblies already loaded in this process (EF Core, Npgsql, BCL …)
    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic))
        TryAdd(asm.Location);

    return result;
}

static CSharpCompilation CreateCompilation(string code, List<MetadataReference> refs)
{
    var tree = CSharpSyntaxTree.ParseText(code);
    return CSharpCompilation.Create(
        "UserCodeAssembly",
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



