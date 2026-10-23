using System.Linq.Expressions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.EntityFrameworkCore;
using RonSijm.RepoGen.Validation;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class ContractBoundaryTests
{
    public static TheoryData<Type, string> ForbiddenShapes => new()
    {
        { typeof(IDbContextTransaction), "EF-free" },
        { typeof(IRepositoryQueryRoot<ClosedContext, int, ClosedRowDto>), "EF-free" },
        { typeof(RepositoryQuerySources), "EF-free" },
        { typeof(RepositoryGenerationBundle), "EF-free" },
        { typeof(System.Data.Common.DbConnection), "EF-free" },
        { typeof(System.Data.Common.DbTransaction), "EF-free" },
        { typeof(System.Data.Common.DbCommand), "EF-free" },
        { typeof(System.Data.Common.DbDataReader), "EF-free" },
        { typeof(IQueryProvider), "EF-free" },
        { typeof(Expression), "EF-free" },
        { typeof(ClosedRow), "EF-free" },
        { typeof(IReadOnlyList<ClosedRow>), "argument[0]" },
        { typeof(BoundaryBaseLeak), ".base.Transaction" },
        { typeof(BoundaryInterfaceLeak), ".interface.GetTransaction.return" },
        { typeof(BoundaryIndexerLeak), ".Item.index[transaction]" },
        { typeof(BoundaryHelperLeak), ".Open.return" },
        { typeof(BoundaryConstructorLeak), ".ctor[transaction]" },
        { typeof(BoundaryConstraintLeak), ".constraint" },
        { typeof(BoundaryFieldLeak), ".Transaction" },
        { typeof(BoundaryGenericLeak<int>), ".Next.argument[0].Transaction" },
        { typeof(BoundaryOperatorLeak), ".op_Implicit.return" },
        { typeof(BoundaryEventLeak), ".Changed[e].Transaction" },
        { typeof(NamespaceLeak), ".Transaction" }
    };

    [Theory]
    [MemberData(nameof(ForbiddenShapes))]
    public void ClosedPublicGraphRejectsOrmAndQueryEscapesWithMemberPaths(Type type, string path)
    {
        var boundary = new RepositoryContractBoundary([typeof(ClosedRow)]);
        Action validate = () => boundary.Validate(type, "Search.return");
        validate.Should().Throw<InvalidOperationException>().WithMessage("RGD1502:*")
            .Which.Message.Should().Contain(path).And.Contain("Search.return");
    }

    [Fact]
    public void ExpandingGenericDtoGraphsAreBoundedAndStillCompile()
    {
        using var context = Context();
        var builder = Builder();
        builder.Entity<ClosedRow>().HasQuery("ReadTree").Select(row => new BoundaryNode<int> { Value = row.Id }).List();
        var bundle = RepositoryGenerator.Generate(context, builder.Build());
        bundle.Files.Single(static file => file.Kind == RepositoryOutputKind.Contracts).Contents.Should().Contain("BoundaryNode<global::System.Int32>");
        var report = bundle.ValidateCompilation(new RonSijm.RepoGen.Validation.RepositoryCompilationOptions(CompilationValidationTests.References()));
        report.Diagnostics.Where(static diagnostic => diagnostic.Severity == RonSijm.RepoGen.Validation.RepositoryCompilationSeverity.Error).Should().BeEmpty();
    }

    [Fact]
    public void ClosedArgumentsAreCheckedEvenAfterTheirGenericDefinitionWasSeen()
    {
        var boundary = new RepositoryContractBoundary([typeof(ClosedRow)]);
        boundary.Validate(typeof(BoundaryNode<int>), "Safe.return");
        Action leak = () => boundary.Validate(typeof(BoundaryNode<IDbContextTransaction>), "Leak.return");
        leak.Should().Throw<InvalidOperationException>().WithMessage("RGD1502:*Leak.return.argument[0]*");
    }

    [Fact]
    public void StrongIdsCanHaveOrdinarySpanAndOutParsingHelpers()
    {
        var boundary = new RepositoryContractBoundary([typeof(ClosedRow)]);
        Action validate = () => boundary.Validate(typeof(BoundaryId), "Search[id]");
        validate.Should().NotThrow();
    }

    [Fact]
    public void VisibilityChecksDoNotInvokeGettersOrConstructors()
    {
        BoundaryThrowingDto.Calls = 0;
        var boundary = new RepositoryContractBoundary([typeof(ClosedRow)]);
        boundary.Validate(typeof(BoundaryThrowingDto), "Read.return");
        BoundaryThrowingDto.Calls.Should().Be(0);
    }

    [Fact]
    public void PublicHelperParametersCannotSmuggleEfTypesThroughRefArguments()
    {
        Action validate = () => new RepositoryContractBoundary([]).Validate(typeof(BoundaryRefHelperLeak), "Read.return");
        validate.Should().Throw<InvalidOperationException>().WithMessage("RGD1502:*TryGet[transaction]*");
    }

    [Fact]
    public void ApplicationDtoWithUnmappedPocoIsAllowedAndInterfaceCyclesTerminate()
    {
        var boundary = new RepositoryContractBoundary([typeof(ClosedRow)]);
        boundary.Validate(typeof(BoundaryPocoDto), "Read.return");
        boundary.Validate(typeof(IBoundaryCycle), "Read.return");
    }

    [Fact]
    public void ClosedInputValidationRejectsRelationalEscapesBeforeAnyOutputs()
    {
        using var context = Context();
        var builder = Builder();
        builder.Entity<ClosedRow>().HasQuery("Count").Where<BoundaryBaseLeak>((row, request) => true).Count();
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD1502:*request[request].base.Transaction*");
        context.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public void DefaultBoundPortMethodsAlsoHaveAnEfFreeBoundary()
    {
        using var context = Context();
        var builder = Builder();
        builder.Entity<ClosedRow>().BindReadPort<IBoundaryUnsafeDefaultPort>().HasQuery("Count").Count();
        Action generate = () => RepositoryGenerator.Generate(context, builder.Build());
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD1502:*IBoundaryUnsafeDefaultPort.GetTransaction.return*");
    }

    [Fact]
    public void DisablingAnOperationCapabilityDoesNotValidateItsUnusedDto()
    {
        using var context = Context();
        var builder = Builder();
        var entity = builder.Entity<ClosedRow>();
        entity.ReadEnabled = false;
        entity.HasQuery("Unused").Select(row => new BoundaryBaseLeak()).List();
        RepositoryGenerator.Generate(context, builder.Build()).Files.Should().NotBeEmpty();
    }

    [Fact]
    public void CompatibilitySelectorsDoNotAcquireClosedContractRestrictions()
    {
        using var context = Context();
        var builder = new RepositoryModelBuilder();
        builder.Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities());
        builder.Entity<ClosedRow>().HasQuery("Read").List();
        RepositoryGenerator.Generate(context, builder.Build()).Files.Should().NotBeEmpty();
    }

    [Fact]
    public void OpenAndRefOnlyOperationRootsAreRejected()
    {
        foreach (var type in new[] { typeof(BoundaryNode<>), typeof(int).MakeByRefType(), typeof(ReadOnlySpan<int>), typeof(int).MakePointerType() })
        {
            Action validate = () => new RepositoryContractBoundary([]).Validate(type, "Search.return");
            validate.Should().Throw<InvalidOperationException>().WithMessage("RGD1502:*");
        }
    }

    [Fact]
    public void OrdinaryNotificationEventsDoNotMakeApplicationDtosInvalid()
    {
        new RepositoryContractBoundary([]).Validate(typeof(BoundaryNotificationDto), "Read.return");
    }

    private static RepositoryModelBuilder Builder()
    {
        var builder = new RepositoryModelBuilder();
        builder.Configure(options => options.UseSimpleDefaults().UseClosedRepositories("Boundary.Contracts").GenerateOnlyConfiguredEntities());
        builder.Entity<ClosedRow>().WriteEnabled = false;
        return builder;
    }

    private static ClosedContext Context() => new(new DbContextOptionsBuilder<ClosedContext>().UseSqlite("Data Source=:memory:").Options);
}

public class BoundaryEfBase
{
    public IDbContextTransaction? Transaction { get; }
}

public sealed class BoundaryBaseLeak : BoundaryEfBase;

public interface IBoundaryEfInterface
{
    IDbContextTransaction? GetTransaction();
}

public sealed class BoundaryInterfaceLeak : IBoundaryEfInterface
{
    IDbContextTransaction? IBoundaryEfInterface.GetTransaction() => null;
}

public sealed class BoundaryIndexerLeak
{
    public int this[IDbContextTransaction transaction] => 0;
}

public sealed class BoundaryHelperLeak
{
    public static IDbContextTransaction? Open() => null;
}

public sealed class BoundaryConstructorLeak
{
    public BoundaryConstructorLeak(IDbContextTransaction transaction) => _ = transaction;
}

public sealed class BoundaryConstraintLeak
{
    public static T? Get<T>() where T : DbContext => null;
}
#pragma warning disable CA1051 // Intentional public field: this is the leaked-field regression fixture.
public sealed class BoundaryFieldLeak
{
    public IDbContextTransaction? Transaction;
}
#pragma warning restore CA1051
public sealed class BoundaryGenericLeak<T>
{
    public BoundaryNode<BoundaryFieldLeak>? Next { get; }
}

public sealed class BoundaryOperatorLeak
{
    public static implicit operator ClosedRow(BoundaryOperatorLeak value) => new() { Name = value.ToString()! };
}

public sealed class BoundaryEfEventArgs : EventArgs
{
    public IDbContextTransaction? Transaction { get; }
}

public sealed class BoundaryEventLeak
{
    public event EventHandler<BoundaryEfEventArgs>? Changed;
    public void Raise() => Changed?.Invoke(this, new());
}

public sealed class BoundaryNotificationDto
{
    public event EventHandler<EventArgs>? Changed;
    public void Raise() => Changed?.Invoke(this, EventArgs.Empty);
}

public sealed class BoundaryRefHelperLeak
{
    public static void TryGet(out IDbContextTransaction? transaction) => transaction = null;
}

public sealed class BoundaryNode<T>
{
    public T? Value { get; init; }
    public BoundaryNode<List<T>>? Children { get; init; }
}

public sealed class BoundaryUnmappedPoco
{
    public int Id { get; init; }
}

public sealed record BoundaryPocoDto(BoundaryUnmappedPoco Value);

public interface IBoundaryCycle
{
    IBoundaryCycle Link { get; }
}

public interface IBoundaryUnsafeDefaultPort
{
    Task<int> CountAsync(CancellationToken cancellationToken = default);
    IDbContextTransaction? GetTransaction() => null;
}

public readonly record struct BoundaryId(int Value) : IParsable<BoundaryId>
{
    public static BoundaryId Parse(string s, IFormatProvider? provider) => new(int.Parse(s, provider));

    public static bool TryParse(string? s, IFormatProvider? provider, out BoundaryId result)
    {
        var success = int.TryParse(s, provider, out var value);
        result = new(value);
        return success;
    }

    public static bool TryParse(ReadOnlySpan<char> text, out BoundaryId result)
    {
        var success = int.TryParse(text, out var value);
        result = new(value);
        return success;
    }
}

public sealed class BoundaryThrowingDto
{
    public static int Calls { get; set; }

    public BoundaryThrowingDto()
    {
        Calls++;
        throw new InvalidOperationException("Must not construct DTOs during validation.");
    }

    public string Name
    {
        get
        {
            Calls += GetHashCode();
            throw new InvalidOperationException("Must not read DTO getters during validation.");
        }
    }
}

// Namespace alone must not bypass the contract graph check.
public sealed class NamespaceLeak
{
    public IDbContextTransaction? Transaction { get; }
}