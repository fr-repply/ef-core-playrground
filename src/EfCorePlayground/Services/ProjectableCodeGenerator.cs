using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EfCorePlayground.Services;

/// <summary>
/// Lightweight runtime equivalent of the EntityFrameworkCore.Projectables source generator.
/// Since the real source generator can't run during runtime Roslyn compilation in WASM
/// (version mismatch), this class generates the companion expression classes that the
/// Projectables infrastructure expects at runtime.
/// </summary>
public static class ProjectableCodeGenerator
{
    /// <summary>
    /// Scans a compilation for members annotated with [Projectable] and generates
    /// companion expression classes + a ProjectionRegistry, returning them as additional
    /// syntax trees to include in the compilation.
    /// </summary>
    public static List<SyntaxTree> GenerateProjectableSources(CSharpCompilation compilation)
    {
        var generatedTrees = new List<SyntaxTree>();
        var registryEntries = new List<RegistryEntry>();

        foreach (var tree in compilation.SyntaxTrees)
        {
            var root = tree.GetRoot();
            var semanticModel = compilation.GetSemanticModel(tree);

            // Find all members with [Projectable] attribute
            var members = root.DescendantNodes()
                .OfType<MemberDeclarationSyntax>()
                .Where(m => HasProjectableAttribute(m));

            foreach (var member in members)
            {
                var generated = GenerateExpressionClass(member, semanticModel);
                if (generated != null)
                {
                    generatedTrees.Add(CSharpSyntaxTree.ParseText(generated.Value.Source));
                    registryEntries.Add(generated.Value.RegistryEntry);
                }
            }
        }

        if (registryEntries.Count > 0)
        {
            var registrySource = GenerateProjectionRegistry(registryEntries);
            generatedTrees.Add(CSharpSyntaxTree.ParseText(registrySource));
        }

        return generatedTrees;
    }

    private static bool HasProjectableAttribute(MemberDeclarationSyntax member)
    {
        return member.AttributeLists
            .SelectMany(al => al.Attributes)
            .Any(a =>
            {
                var name = a.Name.ToString();
                return name is "Projectable" or "ProjectableAttribute"
                    or "EntityFrameworkCore.Projectables.Projectable"
                    or "EntityFrameworkCore.Projectables.ProjectableAttribute";
            });
    }

    private static (string Source, RegistryEntry RegistryEntry)? GenerateExpressionClass(
        MemberDeclarationSyntax member, SemanticModel semanticModel)
    {
        return member switch
        {
            PropertyDeclarationSyntax prop => GenerateForProperty(prop, semanticModel),
            MethodDeclarationSyntax method => GenerateForMethod(method, semanticModel),
            _ => null
        };
    }

    private static (string Source, RegistryEntry RegistryEntry)? GenerateForProperty(
        PropertyDeclarationSyntax prop, SemanticModel semanticModel)
    {
        // Get expression body
        var expressionBody = prop.ExpressionBody?.Expression;
        if (expressionBody == null)
            return null;

        var symbol = semanticModel.GetDeclaredSymbol(prop);
        if (symbol == null)
            return null;

        var containingType = symbol.ContainingType;
        var isStatic = prop.Modifiers.Any(SyntaxKind.StaticKeyword);

        // Build naming info
        var namespaceName = containingType.ContainingNamespace.IsGlobalNamespace
            ? null : containingType.ContainingNamespace.ToDisplayString();
        var className = containingType.Name;
        var memberName = symbol.Name;
        var nestedClassNames = GetNestedClassNames(containingType);

        var generatedClassName = BuildGeneratedClassName(namespaceName, nestedClassNames, memberName, null);

        // Build return type
        var returnType = symbol.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        // Build parameter list and func type args
        string paramList;
        string funcTypeArgs;

        if (isStatic)
        {
            // Static property - no parameters
            paramList = "";
            funcTypeArgs = returnType;
        }
        else
        {
            // Instance property - add @this parameter
            var thisType = containingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            paramList = $"{thisType} @this";
            funcTypeArgs = $"{thisType}, {returnType}";

            // Rewrite expression body to replace implicit this references with @this
            expressionBody = RewriteThisReferences(expressionBody, semanticModel, containingType);
        }

        var usingDirectives = CollectUsingDirectives(prop.SyntaxTree);

        var source = BuildExpressionClassSource(
            usingDirectives, namespaceName, generatedClassName,
            funcTypeArgs, paramList, expressionBody.ToFullString());

        var registryEntry = new RegistryEntry
        {
            DeclaringTypeFullName = containingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            MemberLookupName = memberName,
            GeneratedClassFullName = $"EntityFrameworkCore.Projectables.Generated.{generatedClassName}",
            MemberKind = RegistryMemberKind.Property,
            ParameterTypeNames = Array.Empty<string>()
        };

        return (source, registryEntry);
    }

    private static (string Source, RegistryEntry RegistryEntry)? GenerateForMethod(
        MethodDeclarationSyntax method, SemanticModel semanticModel)
    {
        // Get expression body
        var expressionBody = method.ExpressionBody?.Expression;
        if (expressionBody == null)
            return null;

        var symbol = semanticModel.GetDeclaredSymbol(method) as IMethodSymbol;
        if (symbol == null)
            return null;

        var containingType = symbol.ContainingType;
        var isStatic = method.Modifiers.Any(SyntaxKind.StaticKeyword);
        var isExtension = symbol.IsExtensionMethod;

        // Build naming info
        var namespaceName = containingType.ContainingNamespace.IsGlobalNamespace
            ? null : containingType.ContainingNamespace.ToDisplayString();
        var memberName = symbol.Name;
        var nestedClassNames = GetNestedClassNames(containingType);

        // Build parameter type names for overload disambiguation
        var parameterTypeNames = symbol.Parameters
            .Select(p => p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
            .ToArray();

        var generatedClassName = BuildGeneratedClassName(namespaceName, nestedClassNames, memberName,
            parameterTypeNames.Length > 0 ? parameterTypeNames : null);

        // Build return type
        var returnType = symbol.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        // Build parameter list
        var paramParts = new List<string>();
        var funcTypeArgParts = new List<string>();

        if (!isStatic)
        {
            // Instance method - add @this parameter
            var thisType = containingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            paramParts.Add($"{thisType} @this");
            funcTypeArgParts.Add(thisType);

            // Rewrite expression body
            expressionBody = RewriteThisReferences(expressionBody, semanticModel, containingType);
        }

        // Add regular parameters (removing 'this' modifier for extension methods)
        foreach (var param in method.ParameterList.Parameters)
        {
            var paramSymbol = semanticModel.GetDeclaredSymbol(param);
            if (paramSymbol == null) continue;

            var paramType = paramSymbol.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            paramParts.Add($"{paramType} {param.Identifier.Text}");
            funcTypeArgParts.Add(paramType);
        }

        funcTypeArgParts.Add(returnType);

        var paramList = string.Join(", ", paramParts);
        var funcTypeArgs = string.Join(", ", funcTypeArgParts);

        var usingDirectives = CollectUsingDirectives(method.SyntaxTree);

        var source = BuildExpressionClassSource(
            usingDirectives, namespaceName, generatedClassName,
            funcTypeArgs, paramList, expressionBody.ToFullString());

        var registryEntry = new RegistryEntry
        {
            DeclaringTypeFullName = containingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            MemberLookupName = memberName,
            GeneratedClassFullName = $"EntityFrameworkCore.Projectables.Generated.{generatedClassName}",
            MemberKind = RegistryMemberKind.Method,
            ParameterTypeNames = parameterTypeNames
        };

        return (source, registryEntry);
    }

    /// <summary>
    /// Rewrites implicit this references in an expression to use @this.
    /// For example: `Posts.Count` becomes `@this.Posts.Count` when Posts is an instance member.
    /// </summary>
    private static ExpressionSyntax RewriteThisReferences(
        ExpressionSyntax expression, SemanticModel semanticModel, INamedTypeSymbol containingType)
    {
        var rewriter = new ThisReferenceSyntaxRewriter(semanticModel, containingType);
        return (ExpressionSyntax)rewriter.Visit(expression);
    }

    private static List<string> GetNestedClassNames(INamedTypeSymbol type)
    {
        var names = new List<string>();
        var current = type;
        while (current != null)
        {
            names.Insert(0, current.Name);
            current = current.ContainingType;
        }
        return names;
    }

    private static string BuildGeneratedClassName(
        string? namespaceName, List<string> nestedClassNames, string memberName,
        string[]? parameterTypeNames)
    {
        var sb = new StringBuilder();

        if (namespaceName != null)
        {
            foreach (var c in namespaceName)
                sb.Append(c == '.' ? '_' : c);
        }

        sb.Append('_');

        foreach (var className in nestedClassNames)
        {
            sb.Append(className);
            sb.Append('_');
        }

        sb.Append(memberName);

        if (parameterTypeNames != null)
        {
            for (int i = 0; i < parameterTypeNames.Length; i++)
            {
                sb.Append("_P");
                sb.Append(i);
                sb.Append('_');
                AppendSanitizedTypeName(sb, parameterTypeNames[i]);
            }
        }

        return sb.ToString();
    }

    private static void AppendSanitizedTypeName(StringBuilder sb, string typeName)
    {
        const string globalPrefix = "global::";

        int i = 0;
        while (i < typeName.Length)
        {
            if (typeName[i] == 'g'
                && i + globalPrefix.Length <= typeName.Length
                && string.CompareOrdinal(typeName, i, globalPrefix, 0, globalPrefix.Length) == 0)
            {
                i += globalPrefix.Length;
                continue;
            }

            var c = typeName[i];
            sb.Append(IsInvalidIdentifierChar(c) ? '_' : c);
            i++;
        }
    }

    private static bool IsInvalidIdentifierChar(char c) =>
        c == '.' || c == '<' || c == '>' || c == ',' || c == ' ' ||
        c == '[' || c == ']' || c == '`' || c == ':' || c == '?';

    private static string CollectUsingDirectives(SyntaxTree tree)
    {
        var usings = tree.GetRoot().DescendantNodes()
            .OfType<UsingDirectiveSyntax>()
            .Select(u => u.ToFullString().Trim());
        return string.Join("\n", usings);
    }

    private static string BuildExpressionClassSource(
        string usingDirectives, string? namespaceName, string generatedClassName,
        string funcTypeArgs, string paramList, string expressionBody)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable disable");
        sb.AppendLine(usingDirectives);

        if (namespaceName != null)
        {
            sb.AppendLine($"using {namespaceName};");
        }

        sb.AppendLine("using System.Linq.Expressions;");
        sb.AppendLine();
        sb.AppendLine("namespace EntityFrameworkCore.Projectables.Generated");
        sb.AppendLine("{");
        sb.AppendLine($"    [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
        sb.AppendLine($"    static class {generatedClassName}");
        sb.AppendLine("    {");
        sb.AppendLine($"        static Expression<Func<{funcTypeArgs}>> Expression()");
        sb.AppendLine("        {");
        sb.AppendLine($"            return ({paramList}) => {expressionBody};");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private static string GenerateProjectionRegistry(List<RegistryEntry> entries)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable disable");
        sb.AppendLine();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.Linq.Expressions;");
        sb.AppendLine("using System.Reflection;");
        sb.AppendLine();
        sb.AppendLine("namespace EntityFrameworkCore.Projectables.Generated");
        sb.AppendLine("{");
        sb.AppendLine("    [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
        sb.AppendLine("    internal static class ProjectionRegistry");
        sb.AppendLine("    {");
        sb.AppendLine("        private static Dictionary<nint, LambdaExpression> Build()");
        sb.AppendLine("        {");
        sb.AppendLine("            const BindingFlags allFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;");
        sb.AppendLine("            var map = new Dictionary<nint, LambdaExpression>();");
        sb.AppendLine();

        foreach (var entry in entries)
        {
            var memberExpr = entry.MemberKind switch
            {
                RegistryMemberKind.Property =>
                    $"typeof({entry.DeclaringTypeFullName}).GetProperty(\"{entry.MemberLookupName}\", allFlags)?.GetMethod",
                RegistryMemberKind.Method =>
                    $"typeof({entry.DeclaringTypeFullName}).GetMethod(\"{entry.MemberLookupName}\", allFlags, null, {BuildTypeArrayExpr(entry.ParameterTypeNames)}, null)",
                _ => null
            };

            if (memberExpr != null)
            {
                sb.AppendLine($"            Register(map, {memberExpr}, \"{entry.GeneratedClassFullName}\");");
            }
        }

        sb.AppendLine();
        sb.AppendLine("            return map;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        private static readonly Dictionary<nint, LambdaExpression> _map = Build();");
        sb.AppendLine();
        sb.AppendLine("        public static LambdaExpression TryGet(MemberInfo member)");
        sb.AppendLine("        {");
        sb.AppendLine("            var handle = member switch");
        sb.AppendLine("            {");
        sb.AppendLine("                MethodInfo m      => (nint?)m.MethodHandle.Value,");
        sb.AppendLine("                PropertyInfo p    => p.GetMethod?.MethodHandle.Value,");
        sb.AppendLine("                ConstructorInfo c => (nint?)c.MethodHandle.Value,");
        sb.AppendLine("                _                 => null");
        sb.AppendLine("            };");
        sb.AppendLine();
        sb.AppendLine("            return handle.HasValue && _map.TryGetValue(handle.Value, out var expr) ? expr : null;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        private static void Register(Dictionary<nint, LambdaExpression> map, MethodBase m, string exprClass)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (m is null) return;");
        sb.AppendLine("            var exprType = m.DeclaringType?.Assembly.GetType(exprClass);");
        sb.AppendLine("            var exprMethod = exprType?.GetMethod(\"Expression\", BindingFlags.Static | BindingFlags.NonPublic);");
        sb.AppendLine("            if (exprMethod is not null)");
        sb.AppendLine("                map[m.MethodHandle.Value] = (LambdaExpression)exprMethod.Invoke(null, null)!;");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private static string BuildTypeArrayExpr(string[] parameterTypeNames)
    {
        if (parameterTypeNames.Length == 0)
            return "global::System.Type.EmptyTypes";

        var typeofExprs = string.Join(", ", parameterTypeNames.Select(name => $"typeof({name})"));
        return $"new global::System.Type[] {{ {typeofExprs} }}";
    }

    /// <summary>
    /// Syntax rewriter that replaces implicit this references with @this.
    /// Handles: this.X → @this.X, and bare identifiers like Posts → @this.Posts
    /// </summary>
    private class ThisReferenceSyntaxRewriter : CSharpSyntaxRewriter
    {
        private readonly SemanticModel _semanticModel;
        private readonly INamedTypeSymbol _containingType;

        public ThisReferenceSyntaxRewriter(SemanticModel semanticModel, INamedTypeSymbol containingType)
        {
            _semanticModel = semanticModel;
            _containingType = containingType;
        }

        public override SyntaxNode? VisitThisExpression(ThisExpressionSyntax node)
        {
            return SyntaxFactory.IdentifierName("@this");
        }

        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
        {
            // Don't rewrite if it's already part of a member access (e.g., x.Posts - don't touch Posts)
            if (node.Parent is MemberAccessExpressionSyntax memberAccess && memberAccess.Name == node)
                return base.VisitIdentifierName(node);

            // Don't rewrite if it's a type name, namespace, or declaration
            if (node.Parent is QualifiedNameSyntax || node.Parent is UsingDirectiveSyntax)
                return base.VisitIdentifierName(node);

            // Check if the identifier refers to an instance member of the containing type
            var symbolInfo = _semanticModel.GetSymbolInfo(node);
            var symbol = symbolInfo.Symbol;

            if (symbol != null && !symbol.IsStatic && SymbolEqualityComparer.Default.Equals(symbol.ContainingType, _containingType))
            {
                // This is an implicit this reference - rewrite to @this.Identifier
                return SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    SyntaxFactory.IdentifierName("@this"),
                    node);
            }

            return base.VisitIdentifierName(node);
        }
    }

    internal enum RegistryMemberKind
    {
        Property,
        Method
    }

    internal class RegistryEntry
    {
        public string DeclaringTypeFullName { get; set; } = "";
        public string MemberLookupName { get; set; } = "";
        public string GeneratedClassFullName { get; set; } = "";
        public RegistryMemberKind MemberKind { get; set; }
        public string[] ParameterTypeNames { get; set; } = Array.Empty<string>();
    }
}
