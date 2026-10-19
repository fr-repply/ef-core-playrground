using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EfCorePlayground.Services;

/// <summary>
/// Tells whether source code really declares a <c>[Projectable]</c> member, so the Projectables
/// source generator only runs when it has something to generate.
/// A plain text search is not enough: a comment such as <c>// TODO : [Projectable] public static bool EstActif(...)</c>
/// or a string literal would trigger the generator, which then contributes nothing and the execution
/// fails with "The [Projectable] source generator did not contribute any code".
/// Linked into the Precompiler so both pipelines take the same decision.
/// </summary>
public static class ProjectableAttributeDetector
{
    /// <summary>
    /// Returns <c>true</c> when the tree contains an attribute named <c>Projectable</c> or
    /// <c>ProjectableAttribute</c>, qualified or not (<c>[EntityFrameworkCore.Projectables.Projectable]</c>).
    /// Comments and string literals are trivia or tokens, never <see cref="AttributeSyntax"/> nodes.
    /// </summary>
    public static bool HasProjectableAttribute(SyntaxTree syntaxTree)
        => syntaxTree.GetRoot()
            .DescendantNodes()
            .OfType<AttributeSyntax>()
            .Any(attribute => IsProjectableName(attribute.Name));

    private static bool IsProjectableName(NameSyntax name)
    {
        var simpleName = name switch
        {
            QualifiedNameSyntax qualified => qualified.Right,
            AliasQualifiedNameSyntax aliasQualified => aliasQualified.Name,
            SimpleNameSyntax simple => simple,
            _ => null,
        };

        return simpleName?.Identifier.ValueText is "Projectable" or "ProjectableAttribute";
    }
}
