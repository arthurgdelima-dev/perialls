using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Avalonia.Markup.Xaml;

namespace Perialls;

// Textos da tela em português e inglês: Assets/Lang/pt-BR.json e en.json (uma chave por texto).
// No código: Lang.T("status.searching") ou Lang.T("status.count", 3). No XAML: {local:T status.searching}.
// Para traduzir para outro idioma: copie en.json, traduza e acrescente o nome em Languages.
public static class Lang
{
    public static readonly string[] Languages = ["pt-BR", "en"];
    public static string Current { get; private set; } = "pt-BR";

    // O português também é a reserva: um texto que faltar em outro idioma aparece em português.
    private static readonly Dictionary<string, string> Portuguese = Read("pt-BR");
    private static Dictionary<string, string> _texts = Portuguese;

    // Na primeira vez (sem idioma salvo), usa o do sistema: português se for português, senão inglês.
    public static void Load(string? language)
    {
        Current = language != null && Languages.Contains(language) ? language
            : CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "pt" ? "pt-BR" : "en";
        _texts = Current == "pt-BR" ? Portuguese : Read(Current);
    }

    public static string T(string key) =>
        _texts.GetValueOrDefault(key) ?? Portuguese.GetValueOrDefault(key) ?? key;

    public static string T(string key, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, T(key), args);

    private static Dictionary<string, string> Read(string language)
    {
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Lang.{language}.json");
            return stream == null ? [] : JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];
        }
        catch
        {
            return [];
        }
    }
}

// {local:T chave} no XAML. A janela é recriada quando o idioma muda, então o texto é lido uma vez só.
public sealed class TExtension(string key) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider) => Lang.T(key);
}
