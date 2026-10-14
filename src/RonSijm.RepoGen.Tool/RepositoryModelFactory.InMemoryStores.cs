using RonSijm.RepoGen.Design;
using Microsoft.EntityFrameworkCore.Metadata;

namespace RonSijm.RepoGen.Tool;

internal static partial class RepositoryModelFactory
{
    private static InMemoryStoreModel? CreateInMemoryStore(IEntityType entity, RepositoryEntityDesign? design, RepositoryGenerationSettingsModel settings, RepositoryDesign? fullDesign, IModel? metadata)
    {
        if (!settings.InMemoryTestStores || design is null || design.TestDoubleEnabled == false || !design.ReadEnabled && !design.WriteEnabled) return null;
        if (design.ReadPorts.Concat(design.WritePorts).SelectMany(static port => port.GetInterfaces().Append(port))
            .SelectMany(static port => port.GetMethods()).Any(static method => method.Name == "Fixture"))
            throw new InvalidOperationException("RGD3202: Application ports cannot declare Fixture when generating in-memory test stores. This name is reserved for the shared fixture property.");
        if (design.InMemoryStore is not { } store) return null;
        if (!store.RowType.IsVisible || !store.KeyType.IsVisible || store.RowType.ContainsGenericParameters || store.KeyType.ContainsGenericParameters || store.KeyType.IsByRefLike)
            throw new InvalidOperationException("RGD3201: In-memory stores require public entity/key types or an explicit public data shape usable in a separate testing project. Provide HasInMemoryStore<TData, TKey> and explicit key/copy adapters.");
        try
        {
            var mapping = CreateInMemoryEntityMapping(entity, design, settings, fullDesign);
            return new(CSharpTypeName(store.RowType), CSharpTypeName(store.KeyType), ProjectionExpressionRenderer.Render(store.KeySelector), ProjectionExpressionRenderer.Render(store.Copy),
                mapping, design.WriteEnabled ? CreateInMemoryGeneratedValues(entity, store, settings, mapping, metadata) : null,
                CreateInMemoryWriteMapping(entity, design, settings, mapping), CreateInMemoryKeyComparer(entity, store),
                CreateInMemoryValueEqualities(entity, design, settings));
        }
        catch (InvalidOperationException exception)
        {
            if (exception.Message.StartsWith("RGD3203:", StringComparison.Ordinal) || exception.Message.StartsWith("RGD3204:", StringComparison.Ordinal) || exception.Message.StartsWith("RGD3501:", StringComparison.Ordinal) ||
                exception.Message.StartsWith("RGD3601:", StringComparison.Ordinal) || exception.Message.StartsWith("RGD4101:", StringComparison.Ordinal) ||
                exception.Message.StartsWith("RGD4102:", StringComparison.Ordinal)) throw;
            throw new InvalidOperationException("RGD3201: Invalid in-memory key/copy adapter for '" + entity.ClrType.FullName + "': " + exception.Message, exception);
        }
    }
}
