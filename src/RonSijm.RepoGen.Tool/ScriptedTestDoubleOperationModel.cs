namespace RonSijm.RepoGen.Tool;

internal sealed record ScriptedTestDoubleOperationModel(RepositoryApiMethod Method, string ScriptName, string ResultType, bool Stream)
{
    internal string Name => Method.Signature.Split('(')[0];
    internal string ArgumentsName => ScriptName + "Arguments";
    internal IEnumerable<RepositoryApiParameter> Inputs => Method.Parameters.SkipLast(1);
}
