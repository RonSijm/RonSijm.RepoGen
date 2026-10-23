using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Sqlite;
using Xunit;

namespace RonSijm.RepoGen.Tool.Tests;

public sealed class WriteProtectionTests
{
    [Theory]
    [InlineData("create")]
    [InlineData("patch")]
    [InlineData("stage")]
    [InlineData("update")]
    [InlineData("returning")]
    [InlineData("insert")]
    [InlineData("upsertCreate")]
    [InlineData("upsertUpdate")]
    public void EveryNamedAssignmentPathRequiresLocalPermission(string kind)
    {
        using var context = Context();
        var builder = Builder();
        var entity = builder.Entity<ProtectedNote>().ProtectWrite(note => note.Origin);
        Configure(entity, kind, permission: false);
        Action generate = () => Model(context, builder);
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD2903:*Origin*AllowWrite*");
    }

    [Theory]
    [InlineData("create")]
    [InlineData("patch")]
    [InlineData("stage")]
    [InlineData("update")]
    [InlineData("returning")]
    [InlineData("insert")]
    [InlineData("upsertCreate")]
    [InlineData("upsertUpdate")]
    public void ExplicitlyPermittedMappingsCompileStrictly(string kind)
    {
        using var context = Context();
        var builder = Builder();
        Configure(builder.Entity<ProtectedNote>().ProtectWrite(note => note.Origin), kind, permission: true);
        var model = Model(context, builder);
        ClosedRepositoryTests.Compile(model, rejectWarnings: true);
        model.Entities.Single().Commands.Single().AllowedProtectedWrites.Should().Equal("Origin");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PublicSelectorModesCannotClaimProtectedClosedWrites(bool cqrs)
    {
        using var context = Context();
        var builder = new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults().GenerateOnlyConfiguredEntities());
        if (cqrs) builder.Configure(options => options.UseCqrsSelectors());
        builder.Entity<ProtectedNote>().ProtectWrite(note => note.Origin);
        Action generate = () => Model(context, builder);
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD2901:*closed repositories*");
    }

    [Theory]
    [InlineData("ignored")]
    [InlineData("navigation")]
    [InlineData("computed")]
    public void GuardsMustNameDirectMappedScalars(string kind)
    {
        using var context = Context();
        var builder = Builder();
        Action configure = () =>
        {
            var entity = builder.Entity<ProtectedNote>();
            if (kind == "ignored") entity.ProtectWrite(note => note.Ignored);
            if (kind == "navigation") entity.ProtectWrite(note => note.Parts);
            if (kind == "computed") entity.ProtectWrite(note => note.Origin.Length);
            Model(context, builder);
        };
        configure.Should().Throw<Exception>().Where(exception => exception is InvalidOperationException || exception is ArgumentException)
            .WithMessage("RGD2902:*");
    }

    [Fact]
    public void PermissionsDoNotLeakAcrossCommandsOrDisableEfProtections()
    {
        using var context = Context();
        var builder = Builder();
        var entity = builder.Entity<ProtectedNote>().ProtectWrite(note => note.Origin);
        Configure(entity, "create", permission: true);
        entity.HasCommand("Unsafe").Where<int>((note, id) => note.Id == id)
            .Patch(setters => setters.SetProperty(note => note.Origin, "Changed")).Save();
        Action generate = () => Model(context, builder);
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD2903:*Unsafe*Origin*");
        entity.ExcludeCommand("Unsafe");
        entity.ProtectWrite(note => note.Id);
        entity.HasCommand("MoveKey").Where<int>((note, id) => note.Id == id)
            .Patch(setters => setters.SetProperty(note => note.Id, 123)).AllowWrite(note => note.Id).Save();
        generate.Should().Throw<InvalidOperationException>().WithMessage("*immutable key*");
    }

    [Fact]
    public void RepeatedDeclarationsAreIdempotentAndRemovingThemRestoresIdenticalOutput()
    {
        using var context = Context();
        var builder = Builder();
        var entity = builder.Entity<ProtectedNote>();
        Configure(entity, "create", permission: true);
        var original = Model(context, builder);
        entity.ProtectWrite(note => note.Origin).ProtectWrite(note => note.Origin);
        builder.Build().Entities.Single().ProtectedWriteProperties.Should().Equal("Origin");
        var protectedModel = Model(context, builder);
        RepositorySourceRenderer.Render(protectedModel).Should().Be(RepositorySourceRenderer.Render(original));
        ClosedRepositorySourceRenderer.RenderContracts(protectedModel).Should().Be(ClosedRepositorySourceRenderer.RenderContracts(original));
        entity.ProtectWrite(note => note.Origin, enabled: false);
        var restored = Model(context, builder);
        RepositorySourceRenderer.Render(restored).Should().Be(RepositorySourceRenderer.Render(original));
        RepositoryManifestRenderer.Render(restored).Should().Be(RepositoryManifestRenderer.Render(original));
        JsonSerializer.Serialize(restored).Should().Be(JsonSerializer.Serialize(original));
    }

    [Theory]
    [InlineData("add")]
    [InlineData("update")]
    [InlineData("matchingKey")]
    public void ChildProtectionAppliesWithoutAGeneratedChildRepositoryAndCannotInheritParentPermission(string kind)
    {
        using var context = Context();
        var builder = Builder();
        var child = builder.Entity<ProtectedNotePart>();
        child.RepositoryEnabled = false;
        child.ProtectWrite(part => part.Origin);
        if (kind == "matchingKey") child.ProtectWrite(part => part.Key);
        var command = builder.Entity<ProtectedNote>().HasCommand("Parts").Where<ProtectedPartsRequest>((note, request) => note.Id == request.Id)
            .AllowWrite(note => note.Origin);
        command.ReconcileCollection<ProtectedNotePart, ProtectedPartInput, string, ProtectedPartsRequest>(note => note.Parts, request => request.Parts, part => part.Key, item => item.Key,
            children =>
            {
                if (kind == "update") children.Update(setters => setters.SetProperty(part => part.Origin, (part, item) => item.Origin));
                else children.Add(item => new ProtectedNotePart { Key = item.Key, Origin = item.Origin });
                if (kind == "matchingKey") children.AllowWrite(part => part.Origin);
            }).Save();
        Action generate = () => Model(context, builder);
        generate.Should().Throw<InvalidOperationException>().WithMessage("RGD2903:*");
    }

    [Fact]
    public async Task SavedStageSetBasedAtomicAndNativeWritesExecuteWithExplicitPermissions()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = Context(connection);
        await context.Database.EnsureCreatedAsync();
        var builder = Builder();
        var entity = builder.Entity<ProtectedNote>().ProtectWrite(note => note.Origin).ProtectWrite(note => note.OwnerId);
        foreach (var kind in new[] { "create", "patch", "stage", "update", "returning", "insert", "upsertCreate", "upsertUpdate" }) Configure(entity, kind, permission: true);
        var operations = Operations(Model(context, builder), context);
        var request = new ProtectedNoteRequest("first", "Initial", "Author", 7);
        var created = await Invoke<CommandResult<ProtectedNoteView>>(operations, "create", request);
        created.Value!.Origin.Should().Be("Author");
        created.Value.OwnerId.Should().Be(7);
        created.Value.Id.Should().BeGreaterThan(0);
        request = request with { Id = created.Value.Id };
        (await Invoke<CommandResult<ProtectedNoteView>>(operations, "patch", request with { Origin = "Corrected" })).Value!.Origin.Should().Be("Corrected");
        (await Invoke<CommandResult<ProtectedNoteView>>(operations, "patch", request with { OwnerId = 8, Origin = "Wrong owner" })).Status.Should().Be(CommandStatus.NotFound);
        (await Invoke<CommandResult>(operations, "stage", request with { Origin = "Staged" })).Status.Should().Be(CommandStatus.Staged);
        (await context.Notes.AsNoTracking().SingleAsync()).Origin.Should().Be("Corrected");
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        (await Invoke<int>(operations, "update", request with { Origin = "Set based" })).Should().Be(1);
        var returned = await Invoke<MutationResult<ProtectedNoteView>>(operations, "returning", request with { Origin = "Native" });
        returned.Items.Single().Origin.Should().Be("Native");
        var atomic = request with { Code = "atomic", Origin = "Atomic" };
        (await Invoke<CommandResult<ProtectedNoteView>>(operations, "insert", atomic)).Value!.Origin.Should().Be("Atomic");
        (await Invoke<CommandResult<ProtectedNoteView>>(operations, "insert", atomic)).Status.Should().Be(CommandStatus.AlreadyExists);
        (await Invoke<CommandResult<ProtectedNoteView>>(operations, "upsertUpdate", atomic with { Origin = "Updated atomic" })).Value!.Origin.Should().Be("Updated atomic");
        (await context.Notes.AsNoTracking().SingleAsync(note => note.Code == "first")).Origin.Should().Be("Native");
    }

    [Fact]
    public async Task ExplicitChildPermissionsExecuteAndAreInventoriedWithoutAnEmptyChildPort()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = Context(connection);
        await context.Database.EnsureCreatedAsync();
        context.Notes.Add(new ProtectedNote { Code = "parent", Parts = [new() { Key = "A", Origin = "Before" }] });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var builder = Builder();
        var child = builder.Entity<ProtectedNotePart>().ProtectWrite(part => part.Origin).ProtectWrite(part => part.Key);
        child.RepositoryEnabled = false;
        builder.Entity<ProtectedNote>().HasCommand("Parts").Where<ProtectedPartsRequest>((note, request) => note.Id == request.Id)
            .ReconcileCollection<ProtectedNotePart, ProtectedPartInput, string, ProtectedPartsRequest>(note => note.Parts, request => request.Parts, part => part.Key, item => item.Key,
                children => children.AllowWrite(part => part.Origin).AllowWrite(part => part.Key)
                    .Add(item => new ProtectedNotePart { Key = item.Key, Origin = item.Origin })
                    .Update(setters => setters.SetProperty(part => part.Origin, (part, item) => item.Origin)))
            .Save();
        var model = Model(context, builder);
        model.Entities.Should().ContainSingle();
        (await Invoke<CommandResult>(Operations(model, context), "Parts", new ProtectedPartsRequest(1, [new("A", "After"), new("B", "Added")]))).Succeeded.Should().BeTrue();
        (await context.Parts.AsNoTracking().OrderBy(part => part.Key).Select(part => part.Origin).ToArrayAsync()).Should().Equal("After", "Added");
        var collection = model.Entities.Single().Commands.Single().Collections!.Single();
        collection.AllowedProtectedWrites.Should().Equal("Key", "Origin");
        RepositoryManifestRenderer.Render(model).Should().Contain("\"protectedWriteProperties\"").And.Contain("\"allowedProtectedWrites\"");
    }

    [Fact]
    public void ManifestIdentifiesPoliciesAndActualCommandPermissionsWithoutChangingTheApi()
    {
        using var context = Context();
        var builder = Builder();
        var entity = builder.Entity<ProtectedNote>().ProtectWrite(note => note.Origin).ProtectWrite(note => note.OwnerId);
        Configure(entity, "create", permission: true);
        entity.HasCommand("Rename").Where<int>((note, id) => note.Id == id).Patch(setters => setters.SetProperty(note => note.Title, "Renamed")).Save();
        using var manifest = JsonDocument.Parse(RepositoryManifestRenderer.Render(Model(context, builder)));
        var inventory = manifest.RootElement.GetProperty("entities")[0];
        inventory.GetProperty("protectedWriteProperties").EnumerateArray().Select(value => value.GetString()).Should().Equal("Origin", "OwnerId");
        var commands = inventory.GetProperty("commands").EnumerateArray().ToArray();
        commands.Single(command => command.GetProperty("name").GetString() == "RenameAsync").GetProperty("allowedProtectedWrites").GetArrayLength().Should().Be(0);
        commands.Single(command => command.GetProperty("name").GetString() == "createAsync").GetProperty("allowedProtectedWrites").EnumerateArray().Select(value => value.GetString()).Should().Equal("Origin", "OwnerId");
    }

    private static RepositoryModelBuilder Builder() => new RepositoryModelBuilder().Configure(options => options.UseSimpleDefaults()
        .UseClosedRepositories("Generated.ProtectedWrites").GenerateOnlyConfiguredEntities().UseAtomicWrites<SqliteAtomicWriter>().UseReturningWrites<SqliteReturningWriter>());

    private static void Configure(RepositoryEntityBuilder<ProtectedNote> entity, string kind, bool permission)
    {
        var command = entity.HasCommand(kind);
        if (kind is "create" or "insert" or "upsertCreate" or "upsertUpdate")
        {
            if (kind == "upsertUpdate") command.Create<ProtectedNoteRequest>(request => new ProtectedNote { Code = request.Code, OwnerId = request.OwnerId, Title = request.Title });
            else command.Create<ProtectedNoteRequest>(request => new ProtectedNote { Code = request.Code, OwnerId = request.OwnerId, Title = request.Title, Origin = request.Origin });
            if (kind == "insert") command.InsertIfAbsent(note => note.Code);
            if (kind is "upsertCreate" or "upsertUpdate") command.InsertOrUpdate(note => note.Code, setters => setters.SetProperty<string, ProtectedNoteRequest>(note => note.Origin, (note, request) => request.Origin));
        }
        else
        {
            command.Where<ProtectedNoteRequest>((note, request) => note.Id == request.Id && note.OwnerId == request.OwnerId);
            if (kind is "patch" or "stage") command.Patch(setters => setters.SetProperty<string, ProtectedNoteRequest>(note => note.Origin, (note, request) => request.Origin));
            else command.Update(setters => setters.SetProperty<string, ProtectedNoteRequest>(note => note.Origin, (note, request) => request.Origin));
        }
        if (permission) command.AllowWrite(note => note.Origin).AllowWrite(note => note.OwnerId);
        if (kind == "stage") command.Stage();
        else if (kind == "update") { }
        else if (kind == "returning") command.ReturnsMany(note => new ProtectedNoteView(note.Id, note.OwnerId, note.Title, note.Origin, note.Version));
        else
        {
            if (kind is "create" or "patch") command.Save();
            command.Returns(note => new ProtectedNoteView(note.Id, note.OwnerId, note.Title, note.Origin, note.Version));
        }
    }

    private static WriteProtectionContext Context(SqliteConnection? connection = null) => new(connection is null
        ? new DbContextOptionsBuilder<WriteProtectionContext>().UseSqlite("Data Source=:memory:").Options
        : new DbContextOptionsBuilder<WriteProtectionContext>().UseSqlite(connection).Options);
    private static RepositoryGenerationModel Model(WriteProtectionContext context, RepositoryModelBuilder builder)
        => RepositoryModelFactory.Create(context, typeof(WriteProtectionContext).Assembly, design: builder.Build());
    private static object Operations(RepositoryGenerationModel model, WriteProtectionContext context)
    {
        var arguments = new List<object> { context };
        if (model.Entities.Single().Commands.Any(command => command.IsAtomic)) arguments.Add(new SqliteAtomicWriter());
        if (model.Entities.Single().Commands.Any(command => command.IsReturning)) arguments.Add(new SqliteReturningWriter());
        return Activator.CreateInstance(ClosedRepositoryTests.Compile(model, rejectWarnings: true).GetType(typeof(ProtectedNote).Namespace + ".ProtectedNoteRepositoryOperations")!, arguments.ToArray())!;
    }
    private static Task<T> Invoke<T>(object operations, string method, object request)
        => (Task<T>)operations.GetType().GetMethod(method + "Async")!.Invoke(operations, [request, CancellationToken.None])!;
}
