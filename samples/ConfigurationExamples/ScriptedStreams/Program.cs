using RonSijm.RepoGen.ConfigurationExamples.Contracts.Generated.ScriptedFeed;
using RonSijm.RepoGen.ConfigurationExamples.Contracts.ScriptedFeed;
using RonSijm.RepoGen.ConfigurationExamples.TestDoubles.ScriptedFeed;
using RonSijm.RepoGen.Testing;

// An EF-free consumer of a generated read port. The designer lives in Configuration, not this executable.
var fixture = new FeedEntryRepositoryTestDouble();
#pragma warning disable CA1859 // Intentionally demonstrate the service-facing read port, not fixture-only controls.
IReadFeedEntryRepository read = fixture;
#pragma warning restore CA1859
fixture.OnWatchAsync.RecordCalls(args => new(args.AfterId));
fixture.OnWatchAsync.ReturnsItems([new FeedItem(2, "Two"), new FeedItem(3, "Three")]);
var stream = read.WatchAsync(1);
Ensure(fixture.OnWatchAsync.InvocationCount == 0, "Creating a stream must not invoke a handler.");
await using (var enumerator = stream.GetAsyncEnumerator())
{
    Ensure(await enumerator.MoveNextAsync() && enumerator.Current.Id == 2, "The arranged first item is emitted.");
    Ensure(fixture.OnWatchAsync.Calls[0].Outcome == ScriptedCallOutcome.InProgress, "A non-exhausted stream remains in progress.");
}
Ensure(fixture.OnWatchAsync.Calls[0].Outcome == ScriptedCallOutcome.Disposed, "Early disposal is not successful exhaustion.");

var count = 0;
await foreach (var _ in stream) count++;
Ensure(count == 2 && fixture.OnWatchAsync.Calls[1].Outcome == ScriptedCallOutcome.Completed, "Re-enumeration invokes the script again.");

using var cancellation = new CancellationTokenSource();
await cancellation.CancelAsync();
try
{
    await foreach (var _ in read.WatchAsync(1).WithCancellation(cancellation.Token)) { }
    throw new InvalidOperationException("Enumeration cancellation must be honored.");
}
catch (OperationCanceledException) { }
Ensure(fixture.OnWatchAsync.Calls[2].Outcome == ScriptedCallOutcome.Canceled, "Cancellation has a distinct recorded outcome.");
Console.WriteLine("ScriptedStreams: cold streams, completed/early-disposed outcomes and enumeration cancellation passed.");

static void Ensure(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
