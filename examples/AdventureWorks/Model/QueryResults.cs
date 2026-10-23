namespace RonSijm.RepoGen.AdventureWorks.Model;

public sealed record ProductSummary(int ProductId, string Name, string ProductNumber, decimal ListPrice, string? Subcategory);

public sealed record OrderSummary(int SalesOrderId, int CustomerId, DateTime OrderDate, decimal TotalDue, int LineCount);
