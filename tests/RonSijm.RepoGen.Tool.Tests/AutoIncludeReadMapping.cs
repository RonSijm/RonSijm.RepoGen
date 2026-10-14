namespace RonSijm.RepoGen.Tool.Tests;

public static class AutoIncludeReadMapping
{
    // An explicit top-level entity mapper exposes whether actual EF materialization loaded the graph.
    // It deliberately has no portable fixture translation and is not used by automatic fixture tests.
    public static NavigationReadDetail Detail(NavigationReadLibrary row) => new(row.Id, row.Books.OrderBy(book => book.Id)
        .Select(book => new NavigationReadBookView { Id = book.Id, AuthorName = book.Author.Name }).ToList());
}
