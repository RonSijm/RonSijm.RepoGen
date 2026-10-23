using System.Globalization;

namespace RonSijm.RepoGen.Tool;

/// <summary>Lossless bounded fixture constants, never provider SQL or arbitrary object formatting.</summary>
internal static class InMemoryScalarConstantRenderer
{
    internal static string? RenderTyped(object? value, Type type) => Render(value, type) is { } source
        ? "(" + RepositoryModelFactory.CSharpTypeName(type) + (value is null && !type.IsValueType ? "?" : "") + ")(" + source + ")" : null;

    internal static string? Render(object? value, Type type)
    {
        var scalar = Nullable.GetUnderlyingType(type) ?? type;
        if (value is null) return !type.IsValueType || Nullable.GetUnderlyingType(type) is not null ? "null" : null;
        if (value.GetType() != scalar || !scalar.IsVisible) return null;
        if (scalar.IsEnum)
        {
            var underlying = Enum.GetUnderlyingType(scalar);
            var number = Convert.ChangeType(value, underlying, CultureInfo.InvariantCulture);
            return "(" + RepositoryModelFactory.CSharpTypeName(scalar) + ")(" + Render(number, underlying) + ")";
        }
        static string Number(long number) => number.ToString(CultureInfo.InvariantCulture) + "L";
        return value switch
        {
            Guid guid => "new global::System.Guid(\"" + guid.ToString("D", CultureInfo.InvariantCulture) + "\")",
            DateTime date => "new global::System.DateTime(" + Number(date.Ticks) + ", (global::System.DateTimeKind)" + (int)date.Kind + ")",
            DateTimeOffset date => "new global::System.DateTimeOffset(" + Number(date.Ticks) + ", new global::System.TimeSpan(" + Number(date.Offset.Ticks) + "))",
            TimeSpan time => "new global::System.TimeSpan(" + Number(time.Ticks) + ")",
            DateOnly date => "global::System.DateOnly.FromDayNumber(" + date.DayNumber.ToString(CultureInfo.InvariantCulture) + ")",
            TimeOnly time => "new global::System.TimeOnly(" + Number(time.Ticks) + ")",
            float number when !float.IsFinite(number) => "global::System.Single." + (float.IsNaN(number) ? "NaN" : number > 0 ? "PositiveInfinity" : "NegativeInfinity"),
            double number when !double.IsFinite(number) => "global::System.Double." + (double.IsNaN(number) ? "NaN" : number > 0 ? "PositiveInfinity" : "NegativeInfinity"),
            byte[] => null, // Mutable/provider binary defaults and sentinels remain explicit adapters.
            _ => Primitive(value)
        };
    }

    private static string? Primitive(object value)
    {
        if (value is string text) return Quote(text, '"');
        if (value is char character) return Quote(character.ToString(), '\'');
        if (value is not (string or char or bool or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal)) return null;
        return ProjectionExpressionRenderer.RenderBody(System.Linq.Expressions.Expression.Lambda(System.Linq.Expressions.Expression.Constant(value)), []);
    }

    private static string Quote(string value, char quote) => quote + string.Concat(value.Select(character =>
        character == quote || character == '\\' ? "\\" + character : char.IsControl(character) || character is '\u2028' or '\u2029'
            ? "\\u" + ((int)character).ToString("x4", CultureInfo.InvariantCulture) : character.ToString())) + quote;
}
