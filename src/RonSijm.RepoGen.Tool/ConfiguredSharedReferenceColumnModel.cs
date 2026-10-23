namespace RonSijm.RepoGen.Tool;

internal sealed record ConfiguredSharedReferenceColumnModel(string PropertyName, string OwnerPropertyName,
    string TypeName, int ReferenceKeyIndex);
