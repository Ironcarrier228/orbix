namespace Orbix.Core.Localization;

/// <summary>
/// Tiny localization layer (Russian / English). Every key is declared once in <see cref="Strings"/>
/// together with both translations, so the two languages can never get out of sync.
/// </summary>
public static class Loc
{
    private static string _language = "en";

    /// <summary>Current language code: "ru" or "en".</summary>
    public static string Language => _language;

    /// <summary>Sets the UI language. Anything but "ru" falls back to English.</summary>
    public static void SetLanguage(string? code) => _language = Normalize(code);

    /// <summary>Converts "auto", "ru-RU", "EN"... to "ru" / "en". "auto" is resolved by <paramref name="systemDefault"/>.</summary>
    public static string Resolve(string? setting, string systemDefault) =>
        string.IsNullOrWhiteSpace(setting) || setting.Equals("auto", StringComparison.OrdinalIgnoreCase)
            ? Normalize(systemDefault)
            : Normalize(setting);

    public static string Normalize(string? code) =>
        !string.IsNullOrWhiteSpace(code) && code.Trim().StartsWith("ru", StringComparison.OrdinalIgnoreCase) ? "ru" : "en";

    /// <summary>Translates a key to the current language (the key itself when unknown).</summary>
    public static string T(string key) => In(_language, key);

    /// <summary>Translates a key and formats it with the arguments.</summary>
    public static string T(string key, params object?[] args)
    {
        var format = In(_language, key);
        try
        {
            return string.Format(System.Globalization.CultureInfo.CurrentCulture, format, args);
        }
        catch (FormatException)
        {
            return format;
        }
    }

    /// <summary>Translates a key to a specific language without changing the current one.</summary>
    public static string In(string language, string key)
    {
        if (Strings.Table.TryGetValue(key, out var pair))
        {
            return Normalize(language) == "ru" ? pair.Ru : pair.En;
        }

        return key;
    }

    /// <summary>True when the key is declared.</summary>
    public static bool Has(string key) => Strings.Table.ContainsKey(key);

    public static IReadOnlyCollection<string> Keys => Strings.Table.Keys;
}
