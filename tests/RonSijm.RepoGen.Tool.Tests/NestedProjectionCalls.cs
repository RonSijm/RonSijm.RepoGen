namespace RonSijm.RepoGen.Tool.Tests;

public static class NestedProjectionCalls
{
    public static NestedProjectionDetail Detail(string caption, int value) => new(caption.Trim().ToUpperInvariant(), value);
    public static NestedProjectionCard Card(int id, string title, int version, bool hidden) => new(id,
        hidden ? null : Detail(title, version),
        new List<NestedProjectionDetail> { new("length", title.Length), new("version", version) },
        new[] { title.Trim(), hidden ? "hidden" : "visible" });
    public static NestedProjectionCard Alias(int id, string title, int version, bool hidden) => Card(id, title, version, hidden);
    public static NestedProjectionCard Array(int id, string title) => new(id, new(title, id),
        new[] { new NestedProjectionDetail(title, title.Length) }, System.Array.Empty<string>());
    public static NestedProjectionCard Materialized(int id, string title) => new(id, null,
        Enumerable.Range(0, 2).Select(offset => new NestedProjectionDetail(title, id + offset)).OrderBy(row => row.Value).ThenBy(row => row.Caption).ToList(),
        title.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(word => word.ToUpperInvariant()).ToArray());
    public static NestedProjectionEditable Editable(int id, string title) => new()
    {
        Id = id, Detail = new(title, id), Items = new List<NestedProjectionDetail> { new(title, title.Length) }, Labels = new[] { title }
    };
    public static NestedProjectionStamp Stamp(int id) => new(id, new("stamp", id));
    public static NestedProjectionCard? Optional(int id, string title) => id < 0 ? null : Array(id, title);
    public static NestedProjectionCard Group(bool hidden, int count, int maximum) => Card(count, "group", maximum, hidden);
    public static NestedProjectionCard Never(int id, string title) => throw new InvalidOperationException("The source helper must not run in a fixture.");
}
