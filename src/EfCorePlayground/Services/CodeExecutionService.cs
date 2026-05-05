using System.Collections;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
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
    /// <summary>
    /// Triggers a background trivial compilation to JIT-initialise Roslyn internals and
    /// pre-load all metadata references. Returns a Task that completes when warm-up finishes.
    /// The caller should await this or observe exceptions to avoid Blazor error overlay.
    /// </summary>
    public async Task WarmUpRoslynAsync()
    {
        try
        {
            await Task.Yield(); // Allow UI to continue rendering
            // JIT-warm Roslyn parsing, compilation creation, and emit pipeline.
            // Does NOT load metadata references (HTTP fetch in WASM can trigger error overlay).
            // Refs are loaded lazily on first real compilation.
            var tree = CSharpSyntaxTree.ParseText(BuildFullCode("return null;"));
            var compilation = CSharpCompilation.Create("warmup", new[] { tree },
                Array.Empty<MetadataReference>(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            compilation.Emit(Stream.Null);
            Console.WriteLine("[Warmup] Roslyn JIT warm-up completed");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Warmup] Roslyn warm-up failed (best-effort): {ex.GetType().Name}: {ex.Message}");
        }
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

    private static int _assemblyCounter;

    private async Task<CSharpCompilation> CreateCompilationAsync(string code)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(code);
        var references = await GetMetadataReferencesAsync();

        // Use a unique assembly name for each compilation to avoid WASM Assembly.Load
        // conflicts when loading multiple assemblies with the same name.
        var assemblyName = $"UserCode_{Interlocked.Increment(ref _assemblyCounter)}";
        return CSharpCompilation.Create(
            assemblyName,
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
    /// Builds a full set of Roslyn metadata references for user-code compilation.
    ///
    /// Root cause of CS0009 in WasmBuildNative=true:
    ///   Assembly.TryGetRawMetadata() returns AOT-compiled native WASM bytecode (a valid PE
    ///   file but without a COR/CLI header).  Roslyn accepts the bytes at reference creation
    ///   time (lazy validation), then emits CS0009 for every such reference when it tries to
    ///   read the metadata tables — effectively providing zero usable references.
    ///
    /// Fix — load every assembly via HTTP from _framework/:
    ///   The _framework/ directory contains the original managed IL .dll files alongside the
    ///   native dotnet.native.wasm.  These are valid managed PEs (have a CLI header) and are
    ///   exactly what Roslyn needs.  We use blazor.boot.json to discover the exact URLs
    ///   (which may be fingerprinted in publish mode) and load all of them in parallel.
    ///   Each loaded file is validated with PEReader.HasMetadata to silently skip any
    ///   native-only artifacts.
    ///
    /// The result is cached for all subsequent compilations.
    /// </summary>
    private async Task<List<MetadataReference>> GetMetadataReferencesAsync()
    {
        if (_cachedBclRefs == null)
        {
            await _refLoadLock.WaitAsync();
            try
            {
                if (_cachedBclRefs == null) // double-checked locking
                {
                    var refs = await BuildAllFrameworkRefsAsync();
                    // Only cache if we actually loaded refs — avoids permanently caching
                    // an empty result when HTTP loading fails (e.g. dev mode with native WASM).
                    if (refs.Length > 0)
                        _cachedBclRefs = refs;
                    else
                        return new List<MetadataReference>();
                }
            }
            finally
            {
                _refLoadLock.Release();
            }
        }

        return new List<MetadataReference>(_cachedBclRefs);
    }

    /// <summary>
    /// Loads metadata references for Roslyn compilation.
    /// Strategy (in order):
    ///   1. <c>wwwroot/ref/manifest.txt</c> + <c>ref/*.bin</c> — pre-built managed DLLs
    ///      from the WASM build output. Works in both dev and publish mode.
    ///   2. <c>_framework/blazor.boot.json</c> → HTTP load — works in older .NET / publish mode
    ///      where _framework/ still serves managed DLLs.
    /// </summary>
    private async Task<MetadataReference[]> BuildAllFrameworkRefsAsync()
    {
        // ── Strategy 1: Load from wwwroot/ref/ (pre-built by Precompiler) ────
        var refRefs = await TryLoadFromRefDirectoryAsync();
        if (refRefs.Length > 0)
        {
            Console.WriteLine($"[Refs] {refRefs.Length} managed refs loaded from ref/");
            return refRefs;
        }

        // ── Strategy 2: Discover from _framework/ via boot.json ──────────────
        var urls = await DiscoverFrameworkAssemblyUrlsAsync();
        Console.WriteLine($"[Refs] {urls.Count} assembly URLs to fetch from _framework/");

        var loadTasks = urls.Select(url => LoadManagedRefAsync(url));
        var results = await Task.WhenAll(loadTasks);

        var refs = results.OfType<MetadataReference>().ToArray();
        Console.WriteLine($"[Refs] {refs.Length}/{urls.Count} managed refs ready (rest were native/invalid)");
        return refs;
    }

    /// <summary>
    /// Tries to load metadata references from <c>wwwroot/ref/manifest.txt</c> + <c>ref/*.bin</c>.
    /// These are managed PE DLLs copied from the WASM build output by the Precompiler.
    /// Returns empty array on failure.
    /// </summary>
    private async Task<MetadataReference[]> TryLoadFromRefDirectoryAsync()
    {
        try
        {
            var response = await _http.GetAsync("ref/manifest.txt", HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode) return Array.Empty<MetadataReference>();

            var manifest = await response.Content.ReadAsStringAsync();
            var names = manifest.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (names.Length == 0) return Array.Empty<MetadataReference>();

            Console.WriteLine($"[Refs] ref/manifest.txt: {names.Length} assemblies listed");

            var loadTasks = names.Select(async name =>
            {
                try
                {
                    var resp = await _http.GetAsync($"ref/{name}.bin", HttpCompletionOption.ResponseHeadersRead);
                    if (!resp.IsSuccessStatusCode) return null;
                    var bytes = await resp.Content.ReadAsByteArrayAsync();
                    return IsManagedPe(bytes) ? MetadataReference.CreateFromImage(bytes) : null;
                }
                catch { return null; }
            });

            var results = await Task.WhenAll(loadTasks);
            return results.OfType<MetadataReference>().ToArray();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Refs] Failed to load from ref/: {ex.Message}");
            return Array.Empty<MetadataReference>();
        }
    }

    /// <summary>
    /// Tries to load <paramref name="url"/> as a managed assembly metadata reference.
    /// Returns <c>null</c> (silently) if the download fails or the file is not a managed PE.
    /// </summary>
    private async Task<MetadataReference?> LoadManagedRefAsync(string url)
    {
        try
        {
            var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode) return null;
            var bytes = await response.Content.ReadAsByteArrayAsync();
            return IsManagedPe(bytes) ? MetadataReference.CreateFromImage(bytes) : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Reads <c>_framework/blazor.boot.json</c> (or <c>dotnet.boot.json</c>) to get the
    /// exact published filenames of all framework assemblies, then returns their
    /// <c>_framework/{name}</c> URLs.  Falls back to a hardcoded list on failure.
    /// </summary>
    private async Task<List<string>> DiscoverFrameworkAssemblyUrlsAsync()
    {
        // Try standard boot config locations used by Blazor WASM (.NET 8/9/10)
        foreach (var bootPath in new[] { "_framework/blazor.boot.json", "_framework/dotnet.boot.json" })
        {
            try
            {
                var json = await _http.GetStringAsync(bootPath);
                var urls = ParseAssemblyUrlsFromBootJson(json);
                if (urls.Count > 0)
                {
                    Console.WriteLine($"[Refs] boot.json @ {bootPath}: {urls.Count} assemblies");
                    return urls;
                }
            }
            catch { /* try next location */ }
        }

        // Fallback: known-good list when boot.json is unavailable
        Console.Error.WriteLine("[Refs] blazor.boot.json not found — falling back to hardcoded assembly list");
        return GetFallbackAssemblyUrls();
    }

    private static List<string> ParseAssemblyUrlsFromBootJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Standard Blazor WASM format: { "resources": { "assembly": { "Foo.dll": "sha256-..." } } }
            if (root.TryGetProperty("resources", out var resources) &&
                resources.TryGetProperty("assembly", out var assemblies))
            {
                return assemblies.EnumerateObject()
                    .Select(e => e.Name)
                    .Where(n => n.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    .Select(n => $"_framework/{n}")
                    .ToList();
            }
        }
        catch { /* malformed JSON */ }

        return new List<string>();
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="bytes"/> is a managed .NET PE (has a CLI/COR header).
    /// Rejects native WASM PE files (valid PE, no managed metadata) to prevent CS0009.
    /// Uses an unsafe pointer construction to avoid copying the byte array.
    /// </summary>
    private static bool IsManagedPe(byte[] bytes)
    {
        if (bytes.Length < 64 || bytes[0] != 0x4D || bytes[1] != 0x5A)
            return false;
        try
        {
            unsafe
            {
                fixed (byte* p = bytes)
                {
                    using var pe = new PEReader(p, bytes.Length);
                    return pe.HasMetadata;
                }
            }
        }
        catch { return false; }
    }

    /// <summary>
    /// Fallback assembly list used when <c>blazor.boot.json</c> is unavailable.
    /// Covers BCL + EF Core + Npgsql + Projectables + app assembly.
    /// </summary>
    private static List<string> GetFallbackAssemblyUrls() => new()
    {
        // ── BCL ─────────────────────────────────────────────────────────────────
        "_framework/System.Private.CoreLib.dll",
        "_framework/System.Runtime.dll",
        "_framework/System.Collections.dll",
        "_framework/System.Collections.Concurrent.dll",
        "_framework/System.Linq.dll",
        "_framework/System.Linq.Expressions.dll",
        "_framework/System.Linq.Queryable.dll",
        "_framework/System.Threading.dll",
        "_framework/System.Threading.Tasks.dll",
        "_framework/System.Threading.Tasks.Extensions.dll",
        "_framework/System.Text.Json.dll",
        "_framework/System.ComponentModel.Annotations.dll",
        "_framework/System.ComponentModel.Primitives.dll",
        "_framework/System.ComponentModel.TypeConverter.dll",
        "_framework/System.Console.dll",
        "_framework/System.ObjectModel.dll",
        "_framework/System.Runtime.InteropServices.dll",
        "_framework/netstandard.dll",
        // ── EF Core & extensions ─────────────────────────────────────────────
        "_framework/Microsoft.EntityFrameworkCore.dll",
        "_framework/Microsoft.EntityFrameworkCore.Relational.dll",
        "_framework/Microsoft.Extensions.Caching.Abstractions.dll",
        "_framework/Microsoft.Extensions.Caching.Memory.dll",
        "_framework/Microsoft.Extensions.Configuration.Abstractions.dll",
        "_framework/Microsoft.Extensions.DependencyInjection.Abstractions.dll",
        "_framework/Microsoft.Extensions.DependencyInjection.dll",
        "_framework/Microsoft.Extensions.Logging.Abstractions.dll",
        "_framework/Microsoft.Extensions.Logging.dll",
        "_framework/Microsoft.Extensions.Options.dll",
        "_framework/Microsoft.Extensions.Primitives.dll",
        // ── Npgsql ───────────────────────────────────────────────────────────
        "_framework/Npgsql.dll",
        "_framework/Npgsql.EntityFrameworkCore.PostgreSQL.dll",
        // ── Projectables ─────────────────────────────────────────────────────
        "_framework/EntityFrameworkCore.Projectables.dll",
        "_framework/EntityFrameworkCore.Projectables.Abstractions.dll",
        // ── App ──────────────────────────────────────────────────────────────
        "_framework/EfCorePlayground.dll",
    };


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
