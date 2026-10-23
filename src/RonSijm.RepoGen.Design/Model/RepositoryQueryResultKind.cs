namespace RonSijm.RepoGen.Design;

#pragma warning disable CA1720 // Single mirrors LINQ's established cardinality terminology.

public enum RepositoryQueryResultKind
{
    List,
    Single,
    SingleOrDefault,
    FirstOrDefault,
    Paged,
    CursorPaged,
    GroupedList,
    Exists,
    Count,
    Stream,
    Dictionary,
    Slice,
    Max,
    Min,
    Sum,
    Average,
    Lookup
}
