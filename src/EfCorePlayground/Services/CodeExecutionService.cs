using System.Collections;
using System.Reflection;
using System.Reflection.Metadata;
using System.Security.Cryptography;
using System.Text;
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

    /// <summary>
    /// Cache of compiled assembly bytes keyed by SHA-256 hash of the full source code.
    /// Avoids recompiling the same code on every execution.
    /// </summary>
    private static readonly Dictionary<string, byte[]> _compilationCache = new();

    private readonly IJSRuntime _jsRuntime;
    private readonly HttpClient _http;
    private byte[]? _generatorDllCache;

    // ── BCL reference-assembly cache (loaded from wwwroot/ref/*.bin) ──────────
    private static MetadataReference[]? _cachedBclRefs;
    private static readonly SemaphoreSlim _refLoadLock = new(1, 1);


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

    /// <summary>
    /// Computes a SHA-256 hex string for the given source code, used as a cache key.
    /// </summary>
    private static string ComputeCodeHash(string code)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(code));
        return Convert.ToHexString(bytes);
    }

    /// <summary>
    /// Triggers a background trivial compilation to JIT-initialise Roslyn internals and
    /// pre-load all metadata references. The result is discarded — this is a pure warm-up.
    /// Fire-and-forget: call without await.
    /// </summary>
    public void WarmUpRoslynBackground()
    {
        _ = Task.Run(async () =>
        {
            try { (await CreateCompilationAsync(BuildFullCode("return null;"))).Emit(Stream.Null); }
            catch { /* best-effort */ }
        });
    }


    /// <summary>Clears the in-memory and persistent (localStorage) cache.</summary>
    public async Task ClearCacheAsync()
    {
        _compilationCache.Clear();
        try { await _jsRuntime.InvokeVoidAsync("efCacheInterop.clear"); }
        catch (Exception ex) { Console.Error.WriteLine($"[Cache] clear failed: {ex.Message}"); }
    }

    /// <summary>
    /// Restores previously compiled assemblies from localStorage into the in-memory cache.
    /// Call this once at startup before the first execution.
    /// </summary>
    public async Task LoadCacheFromStorageAsync()
    {
        try
        {
            var stored = await _jsRuntime.InvokeAsync<Dictionary<string, string>>("efCacheInterop.loadAll");
            foreach (var (hash, base64) in stored)
            {
                if (!_compilationCache.ContainsKey(hash))
                    _compilationCache[hash] = Convert.FromBase64String(base64);
            }

            Console.WriteLine($"[Cache] Loaded {stored.Count} assembly(ies) from localStorage.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Cache] LoadCacheFromStorageAsync failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Tries to fetch a pre-compiled assembly DLL from <c>wwwroot/precompiled/{hash}.dll</c>.
    /// Returns <c>null</c> silently on any error or 404 (e.g. user-written code has no static asset).
    /// </summary>
    private async Task<byte[]?> TryLoadFromStaticFileAsync(string hash)
    {
        try
        {
            var response = await _http.GetAsync(
                $"precompiled/{hash}.dll",
                HttpCompletionOption.ResponseHeadersRead);

            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadAsByteArrayAsync();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Persists a compiled assembly to localStorage.</summary>
    private async Task SaveToStorageAsync(string hash, byte[] bytes)
    {
        try
        {
            await _jsRuntime.InvokeVoidAsync("efCacheInterop.save", hash, Convert.ToBase64String(bytes));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Cache] SaveToStorageAsync failed: {ex.Message}");
        }
    }

    public async Task<ExecutionResult> ExecuteAsync(string fullCode)
    {

        try
        {
            var cacheKey = ComputeCodeHash(fullCode);
            byte[] assemblyBytes;

            if (_compilationCache.TryGetValue(cacheKey, out var cached))
            {
                // L1 — in-memory cache hit
                assemblyBytes = cached;
            }
            else
            {
                // L2 — try pre-compiled static asset (wwwroot/precompiled/{hash}.dll)
                assemblyBytes = await TryLoadFromStaticFileAsync(cacheKey);
                if (assemblyBytes != null)
                {
                    _compilationCache[cacheKey] = assemblyBytes;
                }
                else
                {
                    // L3 — compile with Roslyn (user-written code, or first run without static file)
                    var compilation = await CreateCompilationAsync(fullCode);

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

                    assemblyBytes = ms.ToArray();
                    _compilationCache[cacheKey] = assemblyBytes;
                    // Persist to localStorage so future sessions skip compilation
                    await SaveToStorageAsync(cacheKey, assemblyBytes);
                }
            }

            var assembly = Assembly.Load(assemblyBytes);

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

    private async Task<CSharpCompilation> CreateCompilationAsync(string code)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(code);
        var references = await GetMetadataReferencesAsync();

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

    /// <summary>
    /// Builds the full list of Roslyn metadata references used for user-code compilation.
    ///
    /// Strategy (two-tier):
    ///  1. BCL types  → loaded from <c>wwwroot/ref/*.bin</c>.  These are the runtime
    ///     TypeForwardedTo facade DLLs (e.g. System.Runtime ~44 KB, NOT the SDK ref pack
    ///     ~862 KB).  Each facade TypeForwards every type to System.Private.CoreLib so Roslyn
    ///     resolves to one canonical definition — no CS0433.  They are generated at build time
    ///     by the Precompiler from the WASM build-output DLLs.
    ///  2. Non-BCL types (EF Core, Projectables, app models, …) + System.Private.CoreLib →
    ///     extracted directly from in-process runtime assemblies via TryGetRawMetadata.
    ///
    /// Why CoreLib MUST be included alongside the facade refs:
    ///   In WASM publish mode the IL linker rewrites type references in app assemblies to use
    ///   System.Private.CoreLib/7cec85d7bea7798e directly.  The facade System.Runtime.bin
    ///   TypeForwards to CoreLib, so Roslyn follows the chain and finds the single definition
    ///   in CoreLib — no CS0433.  If CoreLib were absent, those IL-linked references would be
    ///   unresolvable → CS0012.
    ///
    /// The BCL refs are fetched once and cached in a static field for all subsequent compilations.
    /// </summary>
    private async Task<List<MetadataReference>> GetMetadataReferencesAsync()
    {
        // ── Step 1: load (and cache) BCL reference assemblies from wwwroot/ref/*.bin ──
        if (_cachedBclRefs == null)
        {
            await _refLoadLock.WaitAsync();
            try
            {
                if (_cachedBclRefs == null) // double-checked
                {
                    var bclRefs = new List<MetadataReference>();
                    try
                    {
                        var manifest = await _http.GetStringAsync("ref/manifest.txt");
                        foreach (var line in manifest.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                        {
                            var name = line.Trim();
                            if (string.IsNullOrEmpty(name)) continue;
                            try
                            {
                                var bytes = await _http.GetByteArrayAsync($"ref/{name}.bin");
                                bclRefs.Add(MetadataReference.CreateFromImage(bytes));
                            }
                            catch (Exception ex)
                            {
                                Console.Error.WriteLine($"[Refs] Skip ref/{name}.bin: {ex.Message}");
                            }
                        }
                        Console.WriteLine($"[Refs] Loaded {bclRefs.Count} BCL reference assemblies from wwwroot/ref/.");
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"[Refs] Could not load BCL refs: {ex.Message}");
                    }
                    _cachedBclRefs = bclRefs.ToArray();
                }
            }
            finally
            {
                _refLoadLock.Release();
            }
        }

        var references = new List<MetadataReference>(_cachedBclRefs);

        // ── Step 2: runtime assemblies for non-BCL packages + System.Private.CoreLib ─────
        // The bclNames set lists assemblies already covered by ref/*.bin (TypeForwardedTo
        // facades); adding them AGAIN from the runtime would double-register the same assembly
        // identity on the Roslyn reference list (harmless but wasteful — skip them).
        // System.Private.CoreLib is intentionally NOT in this exclusion list: CoreLib must
        // come from the WASM AppDomain so Roslyn can resolve the TypeForwardedTo chains from
        // the facade refs down to the single canonical CoreLib definition.
        var bclNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
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
            // legacy aliases
            "mscorlib",
        };

        // Pinned assemblies — always added (then filtered by bclNames below).
        // typeof(object).Assembly = System.Private.CoreLib — kept because it is NOT in bclNames;
        // this ensures WASM-linked assemblies that reference CoreLib identity resolve correctly.
        var pinned = new[]
        {
            typeof(object).Assembly,                                                    // System.Private.CoreLib
            typeof(Microsoft.EntityFrameworkCore.DbContext).Assembly,                  // EF Core
            typeof(EntityFrameworkCore.Projectables.ProjectableAttribute).Assembly,    // Projectables
            typeof(EfCorePlayground.Models.Blog).Assembly,                             // App models
            Assembly.GetExecutingAssembly(),
        };

        var byName = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in pinned.Where(a => !a.IsDynamic))
        {
            var n = a.GetName().Name;
            if (n != null) byName.TryAdd(n, a);
        }
        foreach (var a in AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic))
        {
            var n = a.GetName().Name;
            if (n != null) byName.TryAdd(n, a);
        }

        // Remove BCL names — already covered by ref/*.bin
        foreach (var name in bclNames) byName.Remove(name);

        foreach (var assembly in byName.Values)
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
            catch { /* fall through */ }

            try
            {
                if (!string.IsNullOrEmpty(assembly.Location))
                    references.Add(MetadataReference.CreateFromFile(assembly.Location));
            }
            catch { /* skip */ }
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
        var capturedQueries = PgLiteSqlCapture.Stop();

        // Format the result
        var (output, columns, rows) = FormatResult(result);

        // Full JSON serialization (includes navigation properties) for tree view
        string? richJson = null;
        if (result != null && result is not string && !(result.GetType().IsPrimitive) && !(result is decimal) && !(result is DateTime))
        {
            try { richJson = JsonSerializer.Serialize(result, JsonOptions); }
            catch { /* best-effort */ }
        }

        return new ExecutionResult
        {
            Success = true,
            Output = output,
            Columns = columns,
            Rows = rows,
            RichJson = richJson,
            CapturedQueries = capturedQueries.Count > 0 ? capturedQueries : null
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
    public string? RichJson { get; set; }
    public List<CapturedQuery>? CapturedQueries { get; set; }

    /// <summary>Full SQL text for the Monaco viewer, joining all captured queries.</summary>
    public string? GeneratedSql => CapturedQueries is { Count: > 0 }
        ? string.Join("\n\n", CapturedQueries.Select(q => q.Sql))
        : null;
}

public class CompilationError
{
    public string Id { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public int Line { get; set; }
    public int Column { get; set; }
}
