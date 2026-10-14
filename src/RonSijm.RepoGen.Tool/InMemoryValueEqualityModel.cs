namespace RonSijm.RepoGen.Tool;

internal sealed record InMemoryValueEqualityModel(string PropertyName, string TypeName, string BodySource, string HelperTypeName)
{
    internal string MethodName => "Equal_" + PropertyName;
    internal string Compare(string left, string right) => HelperTypeName + "." + MethodName + "(" + left + ", " + right + ")";
}
