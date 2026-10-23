using Microsoft.EntityFrameworkCore.Metadata;

namespace RonSijm.RepoGen.Tool;

internal sealed record ValidatedLinkRelationship(IForeignKey Target, IReadOnlyList<IForeignKey> Additional);
