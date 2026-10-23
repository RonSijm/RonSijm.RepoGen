using FluentAssertions;
using Xunit;

namespace RonSijm.RepoGen.Generator.Tests;

public sealed class RepoGenGeneratorTests
{
    [Fact]
    public void StreamsCheckCancellationAtBufferedYieldAndCompletionBoundaries()
    {
        var result = GeneratorTestHarness.Run("""
                                              using RonSijm.RepoGen;
                                              namespace Demo;
                                              [GenerateSelectors] public sealed class Item { public int Id { get; set; } }
                                              """);
        result.CompilationErrors.Should().BeEmpty();
        var syntax = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(result.GeneratedSource).GetRoot();
        var stream = syntax.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == "StreamAsync" && method.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.YieldStatementSyntax>().Any());
        stream.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>()
            .Count(call => call.ToString() == "cancellationToken.ThrowIfCancellationRequested()").Should().Be(2);
    }

    [Fact]
    public void GeneratesSelectorsAndRecordProjectionMethods()
    {
        const string source = """
                              using RonSijm.RepoGen;

                              namespace Demo;

                              [GenerateSelectors]
                              public sealed class Customer
                              {
                                  public System.Guid Id { get; set; }
                                  public string Name { get; set; } = "";
                                  public string Email { get; set; } = "";
                              }

                              [ProjectionFrom<Customer>]
                              public sealed record CustomerSummaryDto(System.Guid Id, string Name);
                              """;

        var result = GeneratorTestHarness.Run(source);

        result.GeneratorDiagnostics.Should().BeEmpty();
        result.CompilationErrors.Should().BeEmpty();
        result.GeneratedSource.Should().Contain(
            "public sealed class CustomerSelector : global::RonSijm.RepoGen.BaseEntitySelector<global::Demo.Customer>");
        result.GeneratedSource.Should().Contain("public abstract class BaseEntitySelector<TEntity>");
        result.GeneratedSource.Should().Contain("UpdateAsync(");
        result.GeneratedSource.Should().Contain("StageUpdateAsync(");
        result.GeneratedSource.Should().Contain("DeleteAsync(");
        result.GeneratedSource.Should().Contain("MarkForDeletionAsync(");
        result.GeneratedSource.Should().Contain("DeleteOperation Delete()");
        result.GeneratedSource.Should().Contain("UpdateOperation Update(");
        result.GeneratedSource.Should().Contain("AsNoTracking()");
        result.GeneratedSource.Should().Contain("internal global::System.Linq.IQueryable<TEntity> Query");
        result.GeneratedSource.Should().Contain("AsSummaryDtoAsync(");
        result.GeneratedSource.Should().Contain("AsSummaryDtosAsync(");
        result.GeneratedSource.Should().Contain("new global::Demo.CustomerSummaryDto(entity.Id, entity.Name)");
        result.GeneratedSource.Should().Contain("AsEntityAsync(");
        result.GeneratedSource.Should().Contain("CountAsync(");
        result.GeneratedSource.Should().Contain("GroupedCollectionSelector<TEntity, TKey>");
        result.GeneratedSource.Should().Contain("GroupBy<TKey>(");
        result.GeneratedSource.Should().Contain("PagedResult<T>");
        result.GeneratedSource.Should().Contain("ToPagedListAsync(");
        result.GeneratedSource.Should().Contain("FirstOrDefaultAsync(");
        result.GeneratedSource.Should().Contain("ToDictionaryAsync<TKey, TElement>(");
        result.GeneratedSource.Should().Contain("SelectorContext => Context");
        result.GeneratedSource.Should().Contain("cacheInvalidator");
    }

    [Fact]
    public void GeneratesObjectInitializerForPropertyBasedClass()
    {
        const string source = """
                              using RonSijm.RepoGen;

                              namespace Demo;

                              [GenerateSelectors]
                              public sealed class Customer
                              {
                                  public int Id { get; set; }
                                  public string? Nickname { get; set; }
                              }

                              [ProjectionFrom<Customer>]
                              public sealed class CustomerView
                              {
                                  public int Id { get; init; }
                                  public string? Nickname { get; init; }
                              }
                              """;

        var result = GeneratorTestHarness.Run(source);

        result.GeneratorDiagnostics.Should().BeEmpty();
        result.CompilationErrors.Should().BeEmpty();
        result.GeneratedSource.Should().Contain(
            "new global::Demo.CustomerView { Id = entity.Id, Nickname = entity.Nickname }");
        result.GeneratedSource.Should().Contain("AsViewsAsync(");
    }

    [Fact]
    public void UsesCustomProjectionExpressionAndMethodName()
    {
        const string source = """
                              using System;
                              using System.Linq.Expressions;
                              using RonSijm.RepoGen;

                              namespace Demo;

                              [GenerateSelectors]
                              public sealed class Customer
                              {
                                  public string FirstName { get; set; } = "";
                                  public string LastName { get; set; } = "";
                              }

                              [ProjectionFrom<Customer>(MethodName = "AsCard")]
                              public sealed record LegacyCustomerCard(string Label)
                              {
                                  public static Expression<Func<Customer, LegacyCustomerCard>> Projection =>
                                      customer => new(customer.FirstName + " " + customer.LastName);
                              }
                              """;

        var result = GeneratorTestHarness.Run(source);

        result.GeneratorDiagnostics.Should().BeEmpty();
        result.CompilationErrors.Should().BeEmpty();
        result.GeneratedSource.Should().Contain("AsCardAsync(");
        result.GeneratedSource.Should().Contain("AsCardsAsync(");
        result.GeneratedSource.Should().Contain("global::Demo.LegacyCustomerCard.Projection");
    }

    [Fact]
    public void ReportsMissingEntityMember()
    {
        const string source = """
                              using RonSijm.RepoGen;
                              namespace Demo;
                              [GenerateSelectors] public sealed class Customer { public int Id { get; set; } }
                              [ProjectionFrom<Customer>] public sealed record CustomerDto(int Missing);
                              """;

        var result = GeneratorTestHarness.Run(source);

        result.GeneratorDiagnostics.Should().ContainSingle(static diagnostic => diagnostic.Id == "RG001");
        result.GeneratedSource.Should().NotContain("AsDtoAsync(");
    }

    [Fact]
    public void ReportsIncompatibleEntityMember()
    {
        const string source = """
                              using RonSijm.RepoGen;
                              namespace Demo;
                              [GenerateSelectors] public sealed class Customer { public int Id { get; set; } }
                              [ProjectionFrom<Customer>] public sealed record CustomerDto(string Id);
                              """;

        var result = GeneratorTestHarness.Run(source);

        result.GeneratorDiagnostics.Should().ContainSingle(static diagnostic => diagnostic.Id == "RG003");
    }

    [Fact]
    public void ReportsProjectionForEntityWithoutSelectorGeneration()
    {
        const string source = """
                              using RonSijm.RepoGen;
                              namespace Demo;
                              public sealed class Customer { public int Id { get; set; } }
                              [ProjectionFrom<Customer>] public sealed record CustomerDto(int Id);
                              """;

        var result = GeneratorTestHarness.Run(source);

        result.GeneratorDiagnostics.Should().ContainSingle(static diagnostic => diagnostic.Id == "RG004");
        result.RunResult.Results.SelectMany(static item => item.GeneratedSources).Should().BeEmpty();
    }

    [Fact]
    public void ReportsDuplicateGeneratedMethodNames()
    {
        const string source = """
                              using RonSijm.RepoGen;
                              namespace Domain
                              {
                                  [GenerateSelectors]
                                  public sealed class Customer { public int Id { get; set; } }
                              }
                              namespace First
                              {
                                  [ProjectionFrom<Domain.Customer>]
                                  public sealed record CustomerSummaryDto(int Id);
                              }
                              namespace Second
                              {
                                  [ProjectionFrom<Domain.Customer>]
                                  public sealed record CustomerSummaryDto(int Id);
                              }
                              """;

        var result = GeneratorTestHarness.Run(source);

        result.GeneratorDiagnostics.Where(static diagnostic => diagnostic.Id == "RG002").Should().HaveCount(2);
        result.GeneratedSource.Should().NotContain("AsSummaryDtoAsync(");
    }

    [Fact]
    public void GeneratesMultipleProjectionsForMultipleEntities()
    {
        const string source = """
                              using RonSijm.RepoGen;

                              namespace Demo;

                              [GenerateSelectors]
                              public sealed class Customer
                              {
                                  public int Id { get; set; }
                                  public string Name { get; set; } = "";
                              }

                              [GenerateSelectors]
                              public sealed class Restaurant
                              {
                                  public int Id { get; set; }
                                  public string Name { get; set; } = "";
                              }

                              [ProjectionFrom<Customer>]
                              public sealed record CustomerSummaryDto(int Id, string Name);

                              [ProjectionFrom<Customer>]
                              public sealed record CustomerIdentityDto(int Id);

                              [ProjectionFrom<Restaurant>]
                              public sealed record RestaurantDto(int Id, string Name);
                              """;

        var result = GeneratorTestHarness.Run(source);

        result.GeneratorDiagnostics.Should().BeEmpty();
        result.CompilationErrors.Should().BeEmpty();
        result.RunResult.Results.SelectMany(static item => item.GeneratedSources).Should().HaveCount(3);
        result.GeneratedSource.Should().Contain("AsSummaryDtoAsync(");
        result.GeneratedSource.Should().Contain("AsIdentityDtoAsync(");
        result.GeneratedSource.Should().Contain("public sealed class RestaurantSelector");
        result.GeneratedSource.Should().Contain("AsDtoAsync(");
    }

    [Fact]
    public void UsesInternalMethodsForInternalProjectionTypes()
    {
        const string source = """
                              using RonSijm.RepoGen;

                              namespace Demo;

                              [GenerateSelectors]
                              public sealed class Customer
                              {
                                  public int Id { get; set; }
                              }

                              [ProjectionFrom<Customer>]
                              internal sealed record CustomerDto(int Id);
                              """;

        var result = GeneratorTestHarness.Run(source);

        result.GeneratorDiagnostics.Should().BeEmpty();
        result.CompilationErrors.Should().BeEmpty();
        result.GeneratedSource.Should().Contain(
            "internal static global::System.Threading.Tasks.Task<global::Demo.CustomerDto?> AsDtoAsync(");
        result.GeneratedSource.Should().Contain(
            "internal static async global::System.Threading.Tasks.Task<global::System.Collections.Generic.IReadOnlyList<global::Demo.CustomerDto>> AsDtosAsync(");
    }
}