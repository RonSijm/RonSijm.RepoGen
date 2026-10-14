namespace RonSijm.RepoGen.Tool.Tests;

public sealed class NavigationReadBook
{
    public int Id { get; set; }
    public string? LibraryCode { get; set; }
    public string? LibraryRegion { get; set; }
    public int AuthorId { get; set; }
    public int Price { get; set; }
    public bool Archived { get; set; }
    public NavigationReadLibrary? Library { get; set; }
    public NavigationReadAuthor Author { get; set; } = null!;
    public int? ParentId { get; set; }
    public NavigationReadBook? Parent { get; set; }
}
