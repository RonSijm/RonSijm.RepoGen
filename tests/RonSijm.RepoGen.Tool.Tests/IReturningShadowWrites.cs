using RonSijm.RepoGen.Abstractions;

namespace RonSijm.RepoGen.Tool.Tests;

public interface IReturningShadowWrites
{
    Task<MutationResult<ReturningShadowDto>> AdjustAsync(ReturningShadowRequest input, CancellationToken cancellationToken = default);
    Task<MutationResult<ReturningShadowDto>> RemoveAsync(ReturningShadowRequest input, CancellationToken cancellationToken = default);
}
