using EfCorePlayground.Services;
using Microsoft.CodeAnalysis.CSharp;

namespace EfCorePlayground.Tests;

public class ProjectableAttributeDetectorTests
{
    private static bool Detect(string fullCode)
        => ProjectableAttributeDetector.HasProjectableAttribute(CSharpSyntaxTree.ParseText(fullCode));

    [Fact]
    public void HasProjectableAttribute_WhenAttributeOnlyQuotedInAComment_ReturnsFalse()
    {
        // Snippet that uses the model's [Projectable] properties without declaring any
        var code = CodeExecutionService.BuildFullCode(
            "// Blog.IsPopular et Blog.PostCount sont des propriétés [Projectable] du modèle.\n" +
            "return await db.Blogs\n" +
            "    .Where(b => b.IsPopular)\n" +
            "    .Select(b => new { b.Name, b.PostCount })\n" +
            "    .ToListAsync();");

        Assert.False(Detect(code));
    }

    [Fact]
    public void HasProjectableAttribute_WhenExerciseStatementHasCommentedTodo_ReturnsFalse()
    {
        const string code = """
            using EntityFrameworkCore.Projectables;
            using EfCorePlayground.Models;

            namespace EfCorePlayground.UserCode
            {
                public static class AuthorExtensions
                {
                    // TODO 1 : [Projectable] public static bool EstActif(this Author a) => ...

                    /* TODO 2 : [Projectable(NullConditionalRewriteSupport = NullConditionalRewriteSupport.Ignore)] */
                }
            }
            """;

        Assert.False(Detect(code));
    }

    [Fact]
    public void HasProjectableAttribute_WhenAttributeOnlyInAStringLiteral_ReturnsFalse()
    {
        var code = CodeExecutionService.BuildFullCode("return \"[Projectable]\";");

        Assert.False(Detect(code));
    }

    [Theory]
    [InlineData("[Projectable]")]
    [InlineData("[Projectable(NullConditionalRewriteSupport = NullConditionalRewriteSupport.Ignore)]")]
    [InlineData("[ProjectableAttribute]")]
    [InlineData("[EntityFrameworkCore.Projectables.Projectable]")]
    [InlineData("[global::EntityFrameworkCore.Projectables.ProjectableAttribute]")]
    [InlineData("[Obsolete, Projectable]")]
    public void HasProjectableAttribute_WhenMemberDeclaresTheAttribute_ReturnsTrue(string attribute)
    {
        var code = $$"""
            using System;
            using EntityFrameworkCore.Projectables;
            using EfCorePlayground.Models;

            namespace EfCorePlayground.UserCode
            {
                public static class AuthorExtensions
                {
                    {{attribute}}
                    public static bool EstActif(this Author a) => a.Posts.Any(p => p.PublishedDate >= new DateTime(2024, 1, 1));
                }
            }
            """;

        Assert.True(Detect(code));
    }

    [Fact]
    public void HasProjectableAttribute_WhenOtherAttributeOnly_ReturnsFalse()
    {
        const string code = """
            public class Dto
            {
                [System.ComponentModel.DataAnnotations.Required]
                public string Name { get; set; } = "";
            }
            """;

        Assert.False(Detect(code));
    }
}
