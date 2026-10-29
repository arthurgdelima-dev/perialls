using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Perialls;

// Um driver é só um arquivo JSON que DESCREVE os comandos HID de um aparelho. Não tem código:
// quem monta e envia os pacotes é o app, depois de conferir tudo aqui.
// O formato está explicado em docs/como-adicionar-um-aparelho.md e em drivers/_modelo.json.
public sealed class Driver
{
    public string Name { get; private init; } = "";
    public bool IsLocal { get; init; }

    // Para quais aparelhos ele vale ("VID_3151&PID_502D"). Nada é enviado para outros.
    public HashSet<string> HardwareIds { get; private init; } = [];

    // A interface HID que recebe os comandos.
    public int UsagePage { get; private init; }
    public int? Usage { get; private init; }
    public byte ReportId { get; private init; }
    public int ReportLength { get; private init; }
    public bool UsesFeatureReport { get; private init; }

    public bool HasChecksum { get; private init; }
    public int DelayMs { get; private init; }

    public List<DriverControl> Controls { get; private init; } = [];

    // Comando que grava na memória do aparelho (opcional). Vai depois de cada mudança.
    public DriverControl? SaveCommand { get; private init; }

    // Valores iniciais de todas as variáveis ({r}, {brightness}...), antes de você mexer.
    public IReadOnlyDictionary<string, int> InitialValues { get; private init; } = new Dictionary<string, int>();

    // ---- Regras de segurança ----

    private const int MaxReportLength = 64;
    private const int MaxControls = 16;
    private const int MaxOptions = 64;
    private const int MaxDelayMs = 2000;
    public const int MaxFileBytes = 64 * 1024;

    // Comandos que o app NUNCA envia, por fabricante (firmware, bootloader, reset de fábrica, apagar a
    // memória, mudar o que cada tecla faz). Ficam aqui no código, e não no JSON, para um driver não
    // conseguir tirá-los. Para um fabricante novo, os comandos perigosos dele entram aqui.
    private static readonly Dictionary<int, byte[]> BlockedCommands = new()
    {
        // Akko/MonsGeek: veja "Comandos proibidos" em docs/protocolo-monsgeek.md.
        [0x3151] = [0x01, 0xAC, 0x1C, 0x1E, 0x0A, 0x10, 0x0B],
    };

    // Fabricantes que o app já controla pelo próprio código e que não aceitam driver JSON.
    // (Logitech: o HID++ tem atualização de firmware num número que muda a cada aparelho,
    // então não dá para bloquear por byte.)
    private static readonly HashSet<int> BuiltInVendors = [0x046D];

    private static readonly Regex VariableName = new(@"^[a-zA-Z][a-zA-Z0-9]{0,19}$");
    private static readonly Regex Token = new(@"^(?:[0-9a-fA-F]{2}|\{([a-zA-Z][a-zA-Z0-9]{0,19})\})$");

    // Controles com título já traduzido (Assets/Lang, "control.<nome>").
    private static readonly HashSet<string> KnownLabels = ["color", "brightness", "speed", "effect", "direction", "pollingRate", "dpi"];

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    // Lê e confere o arquivo. Se algo estiver errado, devolve null e explica em "error".
    public static Driver? Load(byte[] data, bool isLocal, out string error)
    {
        if (data.Length > MaxFileBytes)
        {
            error = Lang.T("driver.tooBig");
            return null;
        }

        DriverSpec? spec;
        try
        {
            spec = JsonSerializer.Deserialize<DriverSpec>(data, Json);
        }
        catch (JsonException ex)
        {
            error = Lang.T("driver.brokenJson", ex.Message);
            return null;
        }
        if (spec == null)
        {
            error = Lang.T("driver.empty");
            return null;
        }

        try
        {
            error = "";
            return Validate(spec, isLocal);
        }
        catch (DriverException ex)
        {
            error = ex.Message;
            return null;
        }
    }

    private static Driver Validate(DriverSpec spec, bool isLocal)
    {
        Require(spec.Name.Trim().Length is > 0 and <= 60, Lang.T("driver.name"));

        // Aparelhos
        Require(spec.Devices.Count is > 0 and <= 20, Lang.T("driver.devices"));
        var vendors = new HashSet<int>();
        var ids = new HashSet<string>();
        foreach (var d in spec.Devices)
        {
            var vid = ParseNumber(d.Vid, 0xFFFF, "vid");
            var pid = ParseNumber(d.Pid, 0xFFFF, "pid");
            Require(!BuiltInVendors.Contains(vid), Lang.T("driver.builtInVendor", vid.ToString("X4")));
            vendors.Add(vid);
            ids.Add(DeviceScanner.HardwareId(vid.ToString("X4"), pid.ToString("X4")));
        }

        // Interface
        var iface = spec.Interface ?? throw new DriverException(Lang.T("driver.noInterface"));
        var type = iface.Type.ToLowerInvariant();
        Require(type is "feature" or "output", Lang.T("driver.interfaceType"));
        Require(iface.ReportLength is > 0 and <= MaxReportLength, Lang.T("driver.reportLength", MaxReportLength));
        Require(iface.ReportId is >= 0 and <= 255, Lang.T("driver.reportId"));

        // Checksum
        var checksum = spec.Checksum.ToLowerInvariant();
        Require(checksum is "none" or "ff-minus-sum", Lang.T("driver.checksum"));
        Require(spec.DelayMs is >= 0 and <= MaxDelayMs, Lang.T("driver.delay", MaxDelayMs));

        // Controles
        Require(spec.Controls.Count is > 0 and <= MaxControls, Lang.T("driver.controls", MaxControls));
        var values = new Dictionary<string, int>();
        foreach (var (name, value) in spec.Defaults)
        {
            Require(VariableName.IsMatch(name), Lang.T("driver.defaultsName", name));
            values[name] = Byte(value, $"defaults.{name}");
        }

        var controls = new List<DriverControl>();
        // Confere um comando: tamanho, checksum, e o primeiro byte (o comando) fixo e não proibido.
        DriverControl Check(DriverControl control, int? at)
        {
            var key = control.Key;
            if (checksum == "ff-minus-sum")
            {
                Require(at is { } p && p >= control.Bytes.Count && p < iface.ReportLength,
                    Lang.T("driver.checksumAt", key));
            }
            Require(control.Bytes.Count <= iface.ReportLength, Lang.T("driver.tooLong", key));

            var command = control.Bytes[0].Literal
                ?? throw new DriverException(Lang.T("driver.commandVariable", key));
            foreach (var vid in vendors)
            {
                Require(!BlockedCommands.TryGetValue(vid, out var blocked) || !blocked.Contains(command),
                    Lang.T("driver.commandBlocked", key, command.ToString("X2")));
            }
            return control with { ChecksumAt = checksum == "ff-minus-sum" ? at : null };
        }

        foreach (var (key, c) in spec.Controls)
        {
            Require(VariableName.IsMatch(key), Lang.T("driver.controlName", key));
            controls.Add(Check(ReadControl(key, c, values), c.ChecksumAt ?? spec.ChecksumAt));
        }

        // "save": comando que grava a configuração na memória do aparelho, enviado depois de cada mudança.
        DriverControl? save = null;
        if (spec.Save is { } s)
        {
            Require(s.Min == null && s.Max == null && s.Options == null, Lang.T("driver.saveFields"));
            save = Check(new DriverControl("save", "", DriverControlKind.Command, ParseBytes("save", s.Bytes)), s.ChecksumAt ?? spec.ChecksumAt);
        }

        // Toda variável usada num comando precisa ter um valor.
        foreach (var c in save == null ? controls : controls.Append(save))
        {
            foreach (var v in c.Bytes.Where(t => t.Variable != null).Select(t => t.Variable!))
                Require(values.ContainsKey(v), Lang.T("driver.missingValue", c.Key, v));
        }

        return new Driver
        {
            Name = spec.Name.Trim(),
            IsLocal = isLocal,
            HardwareIds = ids,
            UsagePage = ParseNumber(iface.UsagePage, 0xFFFF, "usagePage"),
            Usage = iface.Usage == null ? null : ParseNumber(iface.Usage, 0xFFFF, "usage"),
            ReportId = (byte)iface.ReportId,
            ReportLength = iface.ReportLength,
            UsesFeatureReport = type == "feature",
            HasChecksum = checksum == "ff-minus-sum",
            DelayMs = spec.DelayMs,
            Controls = controls,
            SaveCommand = save,
            InitialValues = values,
        };
    }

    private static DriverControl ReadControl(string key, ControlSpec c, Dictionary<string, int> values)
    {
        var bytes = ParseBytes(key, c.Bytes);
        var label = c.Label?.Trim() is { Length: > 0 and <= 30 } l ? l : (KnownLabels.Contains(key) ? Lang.T("control." + key) : key);

        // Cor: usa {r}, {g} e {b}, que começam em branco.
        if (key == "color")
        {
            Require(c.Min == null && c.Max == null && c.Options == null, Lang.T("driver.colorFields"));
            foreach (var v in new[] { "r", "g", "b" })
                values.TryAdd(v, 255);
            return new DriverControl(key, label, DriverControlKind.Color, bytes);
        }

        // Faixa de números (ex.: brilho de 0 a 4).
        if (c.Options == null)
        {
            var (min, max) = (c.Min ?? -1, c.Max ?? -1);
            Require(min >= 0 && max <= 255 && min < max && max - min <= 20,
                Lang.T("driver.range", key));
            var initial = c.Default is { } d ? Byte(d, $"{key}.default") : min;
            Require(initial >= min && initial <= max, Lang.T("driver.rangeDefault", key));
            values[key] = initial;
            return new DriverControl(key, label, DriverControlKind.Range, bytes) { Min = min, Max = max };
        }

        // Lista de opções (ex.: efeitos). Cada opção pode ser um número (vai em {key})
        // ou um grupo de variáveis (ex.: { "effect": 4, "direction": 2 }).
        Require(c.Min == null && c.Max == null, Lang.T("driver.optionsOrRange", key));
        Require(c.Options.Count is > 0 and <= MaxOptions, Lang.T("driver.optionsCount", key, MaxOptions));
        var options = new List<DriverOption>();
        foreach (var (optionLabel, value) in c.Options)
        {
            Require(optionLabel.Trim().Length is > 0 and <= 40, Lang.T("driver.optionLabel", key));
            var sets = new Dictionary<string, int>();
            if (value.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in value.EnumerateObject())
                {
                    Require(VariableName.IsMatch(prop.Name), Lang.T("driver.optionVariable", $"{key}.{optionLabel}", prop.Name));
                    sets[prop.Name] = Byte(prop.Value, $"{key}.{optionLabel}.{prop.Name}");
                }
                Require(sets.Count > 0, Lang.T("driver.optionEmpty", $"{key}.{optionLabel}"));
            }
            else
            {
                sets[key] = Byte(value, $"{key}.{optionLabel}");
            }
            options.Add(new DriverOption(optionLabel.Trim(), sets));
        }

        var defaultLabel = c.Default is { ValueKind: JsonValueKind.String } dl ? dl.GetString() : null;
        var start = defaultLabel == null ? options[0] : options.FirstOrDefault(o => o.Label == defaultLabel)
            ?? throw new DriverException(Lang.T("driver.optionDefault", key));
        foreach (var (v, n) in start.Sets)
            values[v] = n;

        return new DriverControl(key, label, DriverControlKind.Options, bytes) { Options = options };
    }

    // "07 {effect} 00" → [07, variável effect, 00]
    private static List<DriverToken> ParseBytes(string key, string? text)
    {
        var parts = (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Require(parts.Length > 0, Lang.T("driver.noBytes", key));
        var tokens = new List<DriverToken>();
        foreach (var part in parts)
        {
            var match = Token.Match(part);
            Require(match.Success, Lang.T("driver.badToken", key, part));
            tokens.Add(match.Groups[1].Success
                ? new DriverToken(null, match.Groups[1].Value)
                : new DriverToken(byte.Parse(part, NumberStyles.HexNumber), null));
        }
        return tokens;
    }

    // Monta o pacote com os valores atuais. Confere de novo os limites e os comandos proibidos.
    public byte[] Build(DriverControl control, IReadOnlyDictionary<string, int> values)
    {
        var data = new byte[ReportLength];
        for (int i = 0; i < control.Bytes.Count; i++)
        {
            var token = control.Bytes[i];
            var value = token.Literal ?? (values.TryGetValue(token.Variable!, out var v) ? v
                : throw new DriverException(Lang.T("driver.valueMissing", token.Variable)));
            if (value is < 0 or > 255)
                throw new DriverException(Lang.T("driver.valueRange", token.Variable));
            data[i] = (byte)value;
        }

        foreach (var id in HardwareIds)
        {
            var vid = Convert.ToInt32(id[4..8], 16);
            if (BlockedCommands.TryGetValue(vid, out var blocked) && blocked.Contains(data[0]))
                throw new DriverException(Lang.T("driver.blocked", data[0].ToString("X2")));
        }

        // checksum = 0xFF - (soma dos bytes anteriores)
        if (HasChecksum && control.ChecksumAt is { } at)
            data[at] = (byte)(0xFF - data.Take(at).Sum(b => b));
        return data;
    }

    private static int ParseNumber(string? text, int max, string field)
    {
        text = text?.Trim() ?? "";
        var ok = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? int.TryParse(text[2..], NumberStyles.HexNumber, null, out var n)
            : int.TryParse(text, out n);
        Require(ok && n >= 0 && n <= max, Lang.T("driver.badNumber", field, text));
        return n;
    }

    private static int Byte(JsonElement value, string field)
    {
        Require(value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var n) && n is >= 0 and <= 255,
            Lang.T("driver.byte", field));
        return value.GetInt32();
    }

    private static int Byte(int value, string field)
    {
        Require(value is >= 0 and <= 255, Lang.T("driver.byte", field));
        return value;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new DriverException(message);
    }
}

public sealed class DriverException(string message) : Exception(message);

// Command: um comando sem controle na tela (o "save").
public enum DriverControlKind { Color, Range, Options, Command }

// Um controle pronto para usar: o comando já separado em bytes fixos e variáveis.
public sealed record DriverControl(string Key, string Label, DriverControlKind Kind, List<DriverToken> Bytes)
{
    public int Min { get; init; }
    public int Max { get; init; }
    public List<DriverOption> Options { get; init; } = [];
    public int? ChecksumAt { get; init; }
}

// Um byte do comando: fixo (Literal) ou o valor de uma variável (Variable).
public sealed record DriverToken(byte? Literal, string? Variable);

// Uma opção da lista: o nome na tela e as variáveis que ela muda.
public sealed record DriverOption(string Label, Dictionary<string, int> Sets);

// ---- O arquivo JSON, como está escrito ----

public sealed class DriverSpec
{
    public string Name { get; set; } = "";
    public List<DeviceSpec> Devices { get; set; } = [];
    public InterfaceSpec? Interface { get; set; }
    public string Checksum { get; set; } = "none";
    public int? ChecksumAt { get; set; }
    public int DelayMs { get; set; }
    public Dictionary<string, int> Defaults { get; set; } = [];
    public Dictionary<string, ControlSpec> Controls { get; set; } = [];
    public ControlSpec? Save { get; set; }
}

public sealed class DeviceSpec
{
    public string Vid { get; set; } = "";
    public string Pid { get; set; } = "";
    public string? Model { get; set; }
}

public sealed class InterfaceSpec
{
    public string UsagePage { get; set; } = "";
    public string? Usage { get; set; }
    public int ReportId { get; set; }
    public int ReportLength { get; set; }
    public string Type { get; set; } = "";
}

public sealed class ControlSpec
{
    public string? Label { get; set; }
    public string? Bytes { get; set; }
    public int? Min { get; set; }
    public int? Max { get; set; }
    public Dictionary<string, JsonElement>? Options { get; set; }
    public JsonElement? Default { get; set; }
    public int? ChecksumAt { get; set; }
}
