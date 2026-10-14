using System.Linq.Expressions;

namespace RonSijm.RepoGen.Design;

public sealed class RepositoryFieldSortDesign
{
    internal RepositoryFieldSortDesign(LambdaExpression fieldName, LambdaExpression descending,
        RepositorySortFieldDesign[] fields, bool preserveOrder, string? defaultField, bool defaultDescending)
    {
        FieldName = fieldName;
        Descending = descending;
        Fields = Array.AsReadOnly(fields);
        PreserveOrderWhenUnspecified = preserveOrder;
        DefaultField = defaultField;
        DefaultDescending = defaultDescending;
    }

    public LambdaExpression FieldName { get; internal set; }
    public LambdaExpression Descending { get; internal set; }
    public IReadOnlyList<RepositorySortFieldDesign> Fields { get; }
    public bool PreserveOrderWhenUnspecified { get; }
    public string? DefaultField { get; }
    public bool DefaultDescending { get; }
}
