namespace RonSijm.RepoGen.Testing;

/// <summary>Controls duplicate keys during fixture arrangement, not repository write semantics.</summary>
public enum InMemoryDuplicateKeyBehavior
{
    Throw,
    Ignore,
    Replace
}
