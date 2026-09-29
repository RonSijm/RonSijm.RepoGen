using FluentAssertions;
using Xunit;

namespace RonSijm.RepoGen.Generator.Tests;

public sealed class RepoGenGeneratorTests
{
    [Fact]
    public void GeneratesSelectorsAndRecordProjectionMethods()
    {
        const string source = """
                              using RonSijm.RepoGen;

                              namespace Demo;

                              [GenerateSelectors]
                              public sealed class Patient
                              {
                                  public System.Guid Id { get; set; }
                                  public string Name { get; set; } = "";
                                  public string Email { get; set; } = "";
                              }

                              [ProjectionFrom<Patient>]
                              public sealed record PatientSummaryDto(System.Guid Id, string Name);
                              """;

        var result = GeneratorTestHarness.Run(source);

        result.GeneratorDiagnostics.Should().BeEmpty();
        result.CompilationErrors.Should().BeEmpty();
        result.GeneratedSource.Should().Contain(
            "public sealed class PatientSelector : global::RonSijm.RepoGen.BaseEntitySelector<global::Demo.Patient>");
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
        result.GeneratedSource.Should().Contain("new global::Demo.PatientSummaryDto(entity.Id, entity.Name)");
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
                              public sealed class Patient
                              {
                                  public int Id { get; set; }
                                  public string? Nickname { get; set; }
                              }

                              [ProjectionFrom<Patient>]
                              public sealed class PatientView
                              {
                                  public int Id { get; init; }
                                  public string? Nickname { get; init; }
                              }
                              """;

        var result = GeneratorTestHarness.Run(source);

        result.GeneratorDiagnostics.Should().BeEmpty();
        result.CompilationErrors.Should().BeEmpty();
        result.GeneratedSource.Should().Contain(
            "new global::Demo.PatientView { Id = entity.Id, Nickname = entity.Nickname }");
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
                              public sealed class Patient
                              {
                                  public string FirstName { get; set; } = "";
                                  public string LastName { get; set; } = "";
                              }

                              [ProjectionFrom<Patient>(MethodName = "AsCard")]
                              public sealed record LegacyPatientCard(string Label)
                              {
                                  public static Expression<Func<Patient, LegacyPatientCard>> Projection =>
                                      patient => new(patient.FirstName + " " + patient.LastName);
                              }
                              """;

        var result = GeneratorTestHarness.Run(source);

        result.GeneratorDiagnostics.Should().BeEmpty();
        result.CompilationErrors.Should().BeEmpty();
        result.GeneratedSource.Should().Contain("AsCardAsync(");
        result.GeneratedSource.Should().Contain("AsCardsAsync(");
        result.GeneratedSource.Should().Contain("global::Demo.LegacyPatientCard.Projection");
    }

    [Fact]
    public void ReportsMissingEntityMember()
    {
        const string source = """
                              using RonSijm.RepoGen;
                              namespace Demo;
                              [GenerateSelectors] public sealed class Patient { public int Id { get; set; } }
                              [ProjectionFrom<Patient>] public sealed record PatientDto(int Missing);
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
                              [GenerateSelectors] public sealed class Patient { public int Id { get; set; } }
                              [ProjectionFrom<Patient>] public sealed record PatientDto(string Id);
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
                              public sealed class Patient { public int Id { get; set; } }
                              [ProjectionFrom<Patient>] public sealed record PatientDto(int Id);
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
                                  public sealed class Patient { public int Id { get; set; } }
                              }
                              namespace First
                              {
                                  [ProjectionFrom<Domain.Patient>]
                                  public sealed record PatientSummaryDto(int Id);
                              }
                              namespace Second
                              {
                                  [ProjectionFrom<Domain.Patient>]
                                  public sealed record PatientSummaryDto(int Id);
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
                              public sealed class Patient
                              {
                                  public int Id { get; set; }
                                  public string Name { get; set; } = "";
                              }

                              [GenerateSelectors]
                              public sealed class Practice
                              {
                                  public int Id { get; set; }
                                  public string Name { get; set; } = "";
                              }

                              [ProjectionFrom<Patient>]
                              public sealed record PatientSummaryDto(int Id, string Name);

                              [ProjectionFrom<Patient>]
                              public sealed record PatientIdentityDto(int Id);

                              [ProjectionFrom<Practice>]
                              public sealed record PracticeDto(int Id, string Name);
                              """;

        var result = GeneratorTestHarness.Run(source);

        result.GeneratorDiagnostics.Should().BeEmpty();
        result.CompilationErrors.Should().BeEmpty();
        result.RunResult.Results.SelectMany(static item => item.GeneratedSources).Should().HaveCount(3);
        result.GeneratedSource.Should().Contain("AsSummaryDtoAsync(");
        result.GeneratedSource.Should().Contain("AsIdentityDtoAsync(");
        result.GeneratedSource.Should().Contain("public sealed class PracticeSelector");
        result.GeneratedSource.Should().Contain("AsDtoAsync(");
    }

    [Fact]
    public void UsesInternalMethodsForInternalProjectionTypes()
    {
        const string source = """
                              using RonSijm.RepoGen;

                              namespace Demo;

                              [GenerateSelectors]
                              public sealed class Patient
                              {
                                  public int Id { get; set; }
                              }

                              [ProjectionFrom<Patient>]
                              internal sealed record PatientDto(int Id);
                              """;

        var result = GeneratorTestHarness.Run(source);

        result.GeneratorDiagnostics.Should().BeEmpty();
        result.CompilationErrors.Should().BeEmpty();
        result.GeneratedSource.Should().Contain(
            "internal static global::System.Threading.Tasks.Task<global::Demo.PatientDto?> AsDtoAsync(");
        result.GeneratedSource.Should().Contain(
            "internal static async global::System.Threading.Tasks.Task<global::System.Collections.Generic.IReadOnlyList<global::Demo.PatientDto>> AsDtosAsync(");
    }
}