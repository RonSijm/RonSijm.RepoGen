namespace RonSijm.RepoGen.EntityFrameworkCore;

/// <summary>Infrastructure-only detached scalar values. Never attaches the entity or creates EF shadow entries.</summary>
public sealed class ReturningWriteRow<TEntity> where TEntity : class
{
    private readonly Dictionary<string, object?> propertyValues;

    public ReturningWriteRow(TEntity entity, IReadOnlyDictionary<string, object?> propertyValues)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(propertyValues);
        Entity = entity;
        this.propertyValues = new Dictionary<string, object?>(propertyValues, StringComparer.Ordinal);
    }

    public TEntity Entity { get; }

    /// <summary>Reads one exact mapped scalar property from the native returned row, in its model CLR type.</summary>
    public T GetValue<T>(string propertyName)
    {
        ArgumentException.ThrowIfNullOrEmpty(propertyName);
        if (!propertyValues.TryGetValue(propertyName, out var value))
            throw new InvalidOperationException("The returned row has no mapped property named '" + propertyName + "'.");
        if (value is T typed) return typed;
        if (value is null && default(T) is null) return default!;
        throw new InvalidOperationException("The returned property '" + propertyName + "' does not have the requested CLR type.");
    }
}
