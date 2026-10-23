namespace RonSijm.RepoGen.Design;

#pragma warning disable CA1040 // A shared marker lets configuration discovery recognize query and command builders.

/// <summary>Identifies a builder returned by an automatically invoked repository configuration method.</summary>
/// <remarks>The operation is registered by HasQuery or HasCommand; returning this marker does not register it again.</remarks>
public interface IRepositoryOperationBuilder
{
}
