using System.Collections;
using System.Reflection;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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

    private SqliteConnection? _connection;

    /// <summary>
    /// Number of boilerplate lines before user code in the full template.
    /// </summary>
    public const int PrefixLineCount = 13;

    /// <summary>
    /// Number of boilerplate lines after user code in the full template.
    /// </summary>
    public const int SuffixLineCount = 3;

    /// <summary>
    /// Wraps a body code snippet (e.g. "return await db.Blogs.ToListAsync();")
    /// into the full compilable template shown in the editor.
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
            return new ExecutionResult
            {
                Success = false,
                Output = $"Runtime error: {ex.Message}",
                Errors = new List<CompilationError>
                {
                    new() { Message = ex.Message, Id = "RUNTIME" }
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

    private List<MetadataReference> GetMetadataReferences()
    {
        // Force-load assemblies that EF Core depends on but may not be loaded yet in WASM
        _ = typeof(System.ComponentModel.IListSource);
        _ = typeof(System.ComponentModel.TypeConverter);
        _ = typeof(System.ComponentModel.DataAnnotations.RequiredAttribute);

        // Explicitly load assemblies by name that WASM might not resolve via typeof alone
        foreach (var name in new[]
        {
            "System.ComponentModel.TypeConverter",
            "System.ComponentModel.Primitives",
            "System.ComponentModel.Annotations",
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
                // Try to get raw metadata (works in .NET 9+)
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

    private async Task<ExecutionResult> RunCompiledCode(Assembly assembly)
    {
        // Create a fresh database for each execution
        _connection?.Dispose();
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();

        var options = new DbContextOptionsBuilder<PlaygroundDbContext>()
            .UseSqlite(_connection)
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

        var resultTask = (Task<object?>?)method.Invoke(null, new object[] { context });
        var result = resultTask != null ? await resultTask : null;

        // Get the generated SQL if possible
        string? generatedSql = null;

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
                        row[prop.Name] = $"[Collection]";
                    else if (value != null && !value.GetType().IsPrimitive && value is not string && value is not DateTime && value is not decimal && !value.GetType().IsEnum)
                        row[prop.Name] = value.ToString();
                    else
                        row[prop.Name] = value;
                }
                rows.Add(row);
            }

            var output = $"{items.Count} result(s)";
            return (output, columns, rows);
        }

        // Single value
        var json = JsonSerializer.Serialize(result, JsonOptions);
        return (json, null, null);
    }

    public void Dispose()
    {
        _connection?.Dispose();
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
