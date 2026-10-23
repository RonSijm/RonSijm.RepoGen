using System.Text.Json;
using System.Text.Json.Serialization;

namespace RonSijm.RepoGen.Abstractions;

#pragma warning disable CA1716 // Optional<T> is the established presence/value terminology; VB callers can escape it.

/// <summary>Distinguishes an omitted patch field from a supplied value, including supplied null.</summary>
[JsonConverter(typeof(OptionalJsonConverterFactory))]
public readonly struct Optional<T>
{
    private readonly T? value;

    public Optional(T? value)
    {
        this.value = value;
        IsSpecified = true;
    }

    public bool IsSpecified { get; }
    public T? Value => IsSpecified ? value : throw new InvalidOperationException("The optional field was not supplied.");
}

public sealed class OptionalJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(Optional<>);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        => (JsonConverter)Activator.CreateInstance(typeof(OptionalJsonConverter<>).MakeGenericType(typeToConvert.GetGenericArguments()))!;

    private sealed class OptionalJsonConverter<T> : JsonConverter<Optional<T>>
    {
        public OptionalJsonConverter() { }
        public override bool HandleNull => true;

        public override Optional<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => new(JsonSerializer.Deserialize<T>(ref reader, options));

        public override void Write(Utf8JsonWriter writer, Optional<T> value, JsonSerializerOptions options)
        {
            if (!value.IsSpecified)
            {
                throw new JsonException("Omit unspecified optional fields using JsonIgnoreCondition.WhenWritingDefault.");
            }

            JsonSerializer.Serialize(writer, value.Value, options);
        }
    }
}