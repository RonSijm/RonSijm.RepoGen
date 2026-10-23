namespace RonSijm.RepoGen.Tool.Tests;

public sealed record ProxyNavigationBookData(int Key, string? Code, string? Region, int AuthorKey, int Price, bool Archived = false, int? ParentKey = null);
