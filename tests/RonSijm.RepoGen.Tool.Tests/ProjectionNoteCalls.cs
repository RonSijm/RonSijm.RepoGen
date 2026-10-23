namespace RonSijm.RepoGen.Tool.Tests;

public static class ProjectionNoteCalls
{
    public static ProjectionNoteCard Card(int id, string title, int version, bool hidden)
        => new(id, (hidden ? "hidden:" : "") + title.Trim().ToUpperInvariant(), version, title.Length);
    public static ProjectionNoteCard Alias(int id, string title, int version, bool hidden) => Card(id, title, version, hidden);
    public static ProjectionNoteData Data(int id, string title) => new() { Id = id, Caption = title };
    public static ProjectionNoteStamp Stamp(int id) => new(id);
    public static ProjectionNoteGroup Group(bool hidden, int count, int maximum) => new(hidden, count, maximum);
    public static ProjectionNoteCard? Optional(int id, string title) => id < 0 ? null : new(id, title, 1, title.Length);
    public static ProjectionNoteCard Literal(int id, string title, string prefix) => new(id, prefix + title, 1, title.Length);
    public static ProjectionNoteCard Never(int id, string title) => throw new InvalidOperationException("Source helper must not run in a fixture.");
    public static T Echo<T>(int id) => throw new InvalidOperationException("Invalid result helper must not execute.");
}
