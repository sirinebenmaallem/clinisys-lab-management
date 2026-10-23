namespace Clinisys.Domain.Common;

internal static class Guard
{
    public static string Required(string value, int maxLength, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        value = value.Trim();
        if (value.Length > maxLength)
            throw new ArgumentOutOfRangeException(name, $"Maximum length is {maxLength}.");
        return value;
    }

    public static string? Optional(string? value, int maxLength, string name) =>
        value is null ? null : Required(value, maxLength, name);

    public static Guid Id(Guid value, string name) => value != Guid.Empty
        ? value : throw new ArgumentException("An identifier is required.", name);

    public static T EnumValue<T>(T value, string name) where T : struct, Enum =>
        Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(name);

    public static decimal Price(decimal value, string name) => Decimal(value, 0, 9999999999999999.99m, 2, name);
    public static decimal Discount(decimal value, string name) => Decimal(value, 0, 100, 2, name);
    public static decimal Measurement(decimal value, string name) =>
        Decimal(value, -999999999999.999999m, 999999999999.999999m, 6, name);

    private static decimal Decimal(decimal value, decimal min, decimal max, int scale, string name)
    {
        if (value < min || value > max || decimal.Round(value, scale) != value)
            throw new ArgumentOutOfRangeException(name, $"Value must be between {min} and {max}, with at most {scale} decimal places.");
        return value;
    }
}
