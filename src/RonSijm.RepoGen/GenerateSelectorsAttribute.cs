namespace RonSijm.RepoGen;

/// <summary>
/// Requests generation of single-result and collection selector types for an entity.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class GenerateSelectorsAttribute : Attribute;