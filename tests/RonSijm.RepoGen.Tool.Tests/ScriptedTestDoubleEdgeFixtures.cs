namespace RonSijm.RepoGen.Tool.Tests.FirstScriptedInputs
{
    public sealed record Input(int Id);
}

namespace RonSijm.RepoGen.Tool.Tests
{
    public interface IScriptedArgumentCasePort
    {
#pragma warning disable CA1708 // Deliberate legal CLR/C# input to verify generated fixture collision handling.
        Task<int> CasesAsync(int id, int Id, CancellationToken cancellationToken = default);
#pragma warning restore CA1708
    }
}

namespace RonSijm.RepoGen.Tool.Tests.SecondScriptedInputs
{
    public sealed record Input(int Id);
}
