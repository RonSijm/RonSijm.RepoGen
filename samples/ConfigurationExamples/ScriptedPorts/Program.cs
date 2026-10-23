using Microsoft.Extensions.DependencyInjection;
using RonSijm.RepoGen.Abstractions;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.ScriptedNotes;
using RonSijm.RepoGen.ConfigurationExamples.TestDoubles.ScriptedNotes;
using RonSijm.RepoGen.Testing;

// A service-test consumer. This executable references neither Models nor Entity Framework.
var services = new ServiceCollection().AddScriptedNotesContextRepositoryTestDoubles();
using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
using var scope = provider.CreateScope();
var fixture = scope.ServiceProvider.GetRequiredService<IScriptedNoteRepositoryTestDouble>();
var read = scope.ServiceProvider.GetRequiredService<INoteLookup>();
var write = scope.ServiceProvider.GetRequiredService<INoteWriter>();
Ensure(ReferenceEquals(fixture, read) && ReferenceEquals(fixture, write), "CQRS and application aliases share the same scoped fixture.");

fixture.OnFindAsyncByInt32.When(args => args.Id == 1, (_, _) => Task.FromResult<NoteSummary?>(new(1, "One")));
fixture.OnFindAsyncByInt32.Returns(null);
fixture.OnFindAsyncByString.Returns(new(2, "By name"));
Ensure(await read.ExistsAsync(1) && !await read.ExistsAsync(99), "Handwritten default interface methods use arranged operations.");
Ensure((await read.FindAsync("anything"))?.Id == 2, "Overloads have separate typed scripts.");

fixture.OnCreateAsync.RecordCalls(args => new(new CreateNoteRequest(args.Request.Name)));
fixture.OnCreateAsync.EnqueueException(new InvalidOperationException("simulated temporary failure"));
fixture.OnCreateAsync.EnqueueResult(new(CommandStatus.Applied, 1, new NoteSummary(3, "Created"), hasValue: true));
try { await write.CreateAsync(new("Created")); throw new InvalidOperationException("The first queued call must fail."); }
catch (InvalidOperationException error) when (error.Message == "simulated temporary failure") { }
Ensure((await write.CreateAsync(new("Created"))).Succeeded, "The second queued call returns its explicit write outcome.");
Ensure(fixture.OnCreateAsync.Calls.Count == 2 && fixture.OnCreateAsync.Calls[0].Outcome == ScriptedCallOutcome.Failed,
    "Recording captures failed and completed calls.");

fixture.OnListSelectedAsync.RecordCalls(args => new(new SelectedNotes(args.Request.Ids.ToArray())), maximumCalls: 2);
fixture.OnListSelectedAsync.Returns([new(1, "Arranged, not queried")]);
var ids = new List<int> { 1 };
await read.ListSelectedAsync(new(ids));
ids.Add(2);
Ensure(fixture.OnListSelectedAsync.Calls[0].Arguments.Request.Ids.SequenceEqual([1]), "Explicit snapshots detach mutable request members.");

fixture.ResetTestDouble();
try { await read.FindAsync(1); throw new InvalidOperationException("Reset must remove arrangements."); }
catch (UnarrangedRepositoryOperationException) { }
Console.WriteLine("ScriptedPorts: EF-free ports, overloaded scripts, queued write outcomes and detached call recording passed.");

static void Ensure(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
