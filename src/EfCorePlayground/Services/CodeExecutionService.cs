using System.Collections;
using System.Reflection;
using System.Reflection.Metadata;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Update;
using Microsoft.JSInterop;
using EfCorePlayground.Models;

namespace EfCorePlayground.Services;

public class CodeExecutionService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles
    };

    private readonly IJSRuntime _jsRuntime;
    private readonly HttpClient _http;
    private byte[]? _generatorDllCache;

    public CodeExecutionService(IJSRuntime jsRuntime, HttpClient http)
    {
        _jsRuntime = jsRuntime;
        _http = http;
    }

    /// <summary>Number of boilerplate lines before user code in the full template.</summary>
    public const int PrefixLineCount = 14;

    /// <summary>Number of boilerplate lines after user code in the full template.</summary>
    public const int SuffixLineCount = 3;

    /// <summary>
    /// Wraps a body code snippet into the full compilable template shown in the editor.
    /// </summary>
    public static string BuildFullCode(string bodyCode)
    {
        var lines = bodyCode.Split('\n');
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

    public async Task<ExecutionResult> ExecuteAsync(string fullCode)
    {
        try
        {
            var compilation = CreateCompilation(fullCode);

            // Run the official Projectables source generator on user code when needed
            if (fullCode.Contains("[Projectable]") || fullCode.Contains("[Projectable("))
                compilation = await RunProjectablesGeneratorAsync(compilation);

            using var ms = new MemoryStream();
            var emitResult = compilation.Emit(ms);

            if (!emitResult.Success)
            {
                var errors = emitResult.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => new CompilationError
                    {
                        Id = d.Id,
                        Message = d.GetMessage(),
                        Line = d.Location.GetMappedLineSpan().StartLinePosition.Line + 1,
                        Column = d.Location.GetMappedLineSpan().StartLinePosition.Character + 1
                    })
                    .ToList();

                return new ExecutionResult
                {
                    Success = false,
                    Errors = errors,
                    Output = "Compilation failed. Check errors below."
                };
            }

            ms.Seek(0, SeekOrigin.Begin);
            var assembly = Assembly.Load(ms.ToArray());

            return await RunCompiledCode(assembly);
        }
        catch (Exception ex)
        {
            // Walk the full exception chain for diagnosis
            var msgs = new System.Text.StringBuilder();
            var e = ex;
            int depth = 0;
            while (e != null && depth < 6)
            {
                msgs.Append($"[{e.GetType().Name}] {e.Message}");
                if (e.StackTrace != null)
                    msgs.Append($"\n  at: {e.StackTrace.Split('\n').FirstOrDefault()?.Trim()}");
                msgs.Append('\n');
                e = e.InnerException;
                depth++;
            }
            Console.Error.WriteLine("[ExecError]\n" + msgs);

            return new ExecutionResult
            {
                Success = false,
                Output = $"Runtime error: {ex.Message}",
                Errors = new List<CompilationError>
                {
                    new()
                    {
                        Message = BuildExceptionMessage(ex),
                        Id = "RUNTIME"
                    }
                }
            };
        }
    }

    private CSharpCompilation CreateCompilation(string code)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(code);
        var references = GetMetadataReferences();

        return CSharpCompilation.Create(
            "UserCodeAssembly",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithOptimizationLevel(OptimizationLevel.Release)
                .WithPlatform(Platform.AnyCpu));
    }

    // ──────────────────────────────────────────────────────────────────
    //  Run the official Projectables source generator at runtime
    // ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Fetches <c>EntityFrameworkCore.Projectables.Generator.dll</c> from <c>wwwroot/bin/</c>,
    /// loads <c>ProjectionExpressionGenerator</c> and runs it via
    /// <see cref="CSharpGeneratorDriver"/> so the companion expression classes are compiled
    /// into the user assembly — exactly as the build-time source generator would do.
    /// </summary>
    private async Task<CSharpCompilation> RunProjectablesGeneratorAsync(CSharpCompilation compilation)
    {
        try
        {
            _generatorDllCache ??= await _http.GetByteArrayAsync("bin/EntityFrameworkCore.Projectables.Generator.dll");
            var genAssembly = Assembly.Load(_generatorDllCache);

            var genType = genAssembly.GetType("EntityFrameworkCore.Projectables.ProjectionExpressionGenerator")
                          ?? genAssembly.GetTypes().FirstOrDefault(t => t.Name == "ProjectionExpressionGenerator");

            if (genType == null)
            {
                Console.Error.WriteLine("[Projectables] ProjectionExpressionGenerator type not found in generator DLL.");
                return compilation;
            }

            var instance = Activator.CreateInstance(genType);

            if (instance is IIncrementalGenerator incrementalGen)
            {
                var driver = CSharpGeneratorDriver.Create(incrementalGen);
                driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out var genDiags);

                foreach (var d in genDiags.Where(d => d.Severity == DiagnosticSeverity.Error))
                    Console.Error.WriteLine($"[Projectables generator] {d.GetMessage()}");

                return (CSharpCompilation)updated;
            }

#pragma warning disable CS0618
            if (instance is ISourceGenerator sourceGen)
            {
                var driver = CSharpGeneratorDriver.Create(sourceGen);
                driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out var genDiags);

                foreach (var d in genDiags.Where(d => d.Severity == DiagnosticSeverity.Error))
                    Console.Error.WriteLine($"[Projectables generator] {d.GetMessage()}");

                return (CSharpCompilation)updated;
            }
#pragma warning restore CS0618

            Console.Error.WriteLine($"[Projectables] Generator type '{genType.FullName}' does not implement IIncrementalGenerator or ISourceGenerator.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Projectables] Failed to run generator: {ex.Message}");
        }

        return compilation;
    }

    // ──────────────────────────────────────────────────────────────────
    //  Metadata references
    // ──────────────────────────────────────────────────────────────────

    private List<MetadataReference> GetMetadataReferences()
    {
        // Force-load assemblies that EF Core depends on but may not be loaded yet in WASM
        _ = typeof(System.ComponentModel.IListSource);
        _ = typeof(System.ComponentModel.TypeConverter);
        _ = typeof(System.ComponentModel.DataAnnotations.RequiredAttribute);
        _ = typeof(EntityFrameworkCore.Projectables.ProjectableAttribute);
        // Force-load System.Linq.Queryable (needed for IQueryable Select/Where/etc.)
        _ = typeof(System.Linq.Queryable);

        // Explicitly load assemblies by name that WASM might not resolve via typeof alone
        foreach (var name in new[]
        {
            "System.ComponentModel.TypeConverter",
            "System.ComponentModel.Primitives",
            "System.ComponentModel.Annotations",
            "EntityFrameworkCore.Projectables.Abstractions",
            "System.Linq.Queryable",
        })
        {
            try { Assembly.Load(name); } catch { }
        }

        var references = new List<MetadataReference>();

        var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic);

        foreach (var assembly in loadedAssemblies)
        {
            try
            {
                unsafe
                {
                    if (assembly.TryGetRawMetadata(out byte* blob, out int length))
                    {
                        references.Add(AssemblyMetadata
                            .Create(ModuleMetadata.CreateFromMetadata((nint)blob, length))
                            .GetReference());
                        continue;
                    }
                }
            }
            catch
            {
                // Fallback: try loading from file location
            }

            try
            {
                if (!string.IsNullOrEmpty(assembly.Location))
                {
                    references.Add(MetadataReference.CreateFromFile(assembly.Location));
                }
            }
            catch
            {
                // Skip assemblies we can't reference
            }
        }

        return references;
    }

    // ──────────────────────────────────────────────────────────────────
    //  Code execution helpers
    // ──────────────────────────────────────────────────────────────────

    private async Task<ExecutionResult> RunCompiledCode(Assembly assembly)
    {
        // Initialize a fresh PGlite instance for each execution
        await _jsRuntime.InvokeVoidAsync("pgliteInterop.init");

        // Make JS runtime available to PgLiteDbCommand
        PgLiteJsRuntime.Instance = _jsRuntime;

        var options = new DbContextOptionsBuilder<PlaygroundDbContext>()
            .UseNpgsql("Host=pglite;Database=playground")
            .ReplaceService<IRelationalConnection, PgLiteRelationalConnection>()
            .ReplaceService<IRelationalDatabaseCreator, PgLiteDatabaseCreator>()
            // Bypass NpgsqlModificationCommandBatch which hard-casts DbConnection to NpgsqlConnection.
            // PgLiteModificationCommandBatch uses AffectedCountModificationCommandBatch (EF Core Relational)
            // which goes through the standard IRelationalCommand path — no Npgsql-specific casts.
            .ReplaceService<IModificationCommandBatchFactory, PgLiteModificationCommandBatchFactory>()
            .Options;

        await using var context = new PlaygroundDbContext(options);
        await context.Database.EnsureCreatedAsync();

        var type = assembly.GetType("EfCorePlayground.UserCode.UserQuery");
        var method = type?.GetMethod("Execute");

        if (method == null)
        {
            return new ExecutionResult
            {
                Success = false,
                Output = "Could not find Execute method in compiled code."
            };
        }

        // Start capturing SQL from user query execution (not schema creation)
        PgLiteSqlCapture.Start();

        var resultTask = (Task<object?>?)method.Invoke(null, new object[] { context });
        var result = resultTask != null ? await resultTask : null;

        // Collect captured SQL
        var capturedSql = PgLiteSqlCapture.Stop();
        string? generatedSql = capturedSql.Count > 0
            ? string.Join("\n\n", capturedSql)
            : null;

        // Format the result
        var (output, columns, rows) = FormatResult(result);

        return new ExecutionResult
        {
            Success = true,
            Output = output,
            Columns = columns,
            Rows = rows,
            GeneratedSql = generatedSql
        };
    }

    private (string output, List<string>? columns, List<Dictionary<string, object?>>? rows) FormatResult(object? result)
    {
        if (result == null)
            return ("null", null, null);

        if (result is string str)
            return (str, null, null);

        if (result is IEnumerable enumerable && result is not string)
        {
            var items = new List<object>();
            foreach (var item in enumerable)
            {
                items.Add(item);
            }

            if (items.Count == 0)
                return ("Empty collection (0 results)", new List<string>(), new List<Dictionary<string, object?>>());

            // Extract columns from the first item
            var firstItem = items[0];
            var properties = firstItem.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
            var columns = properties.Select(p => p.Name).ToList();

            var rows = new List<Dictionary<string, object?>>();
            foreach (var item in items)
            {
                var row = new Dictionary<string, object?>();
                foreach (var prop in properties)
                {
                    var value = prop.GetValue(item);
                    // Avoid serializing navigation properties (circular refs)
                    if (value is IEnumerable && value is not string && value is not Array)
                        row[prop.Name] = "[Collection]";
                    else if (value != null && !value.GetType().IsPrimitive && value is not string
                             && value is not DateTime && value is not decimal && !value.GetType().IsEnum)
                        row[prop.Name] = value.ToString();
                    else
                        row[prop.Name] = value;
                }
                rows.Add(row);
            }

            return ($"{items.Count} result(s)", columns, rows);
        }

        // Single value
        var json = JsonSerializer.Serialize(result, JsonOptions);
        return (json, null, null);
    }

    public void Dispose()
    {
        // PGlite cleanup is handled via JS interop when needed
    }

    /// <summary>
    /// Builds a detailed error message chain including exception type names and top stack frame.
    /// E.g. "[DbUpdateException] ... | [InvalidCastException] ... at ..."
    /// </summary>
    private static string BuildExceptionMessage(Exception ex)
    {
        var sb = new System.Text.StringBuilder();
        var e = ex;
        int depth = 0;
        while (e != null && depth < 6)
        {
            if (depth > 0) sb.Append(" | ");
            sb.Append($"[{e.GetType().Name}] {e.Message}");
            var firstFrame = e.StackTrace?.Split('\n').FirstOrDefault(l => l.TrimStart().StartsWith("at "))?.Trim();
            if (firstFrame != null)
                sb.Append($"  ({firstFrame})");
            e = e.InnerException;
            depth++;
        }
        return sb.ToString();
    }
}

public class ExecutionResult
{
    public bool Success { get; set; }
    public string Output { get; set; } = string.Empty;
    public List<CompilationError>? Errors { get; set; }
    public List<string>? Columns { get; set; }
    public List<Dictionary<string, object?>>? Rows { get; set; }
    public string? GeneratedSql { get; set; }
}

public class CompilationError
{
    public string Id { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public int Line { get; set; }
    public int Column { get; set; }
}
