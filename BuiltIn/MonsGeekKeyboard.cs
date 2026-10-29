using HidSharp;

namespace Perialls;

// Teclados Akko/MonsGeek (chip RY5088): iluminação e Hz pelo mesmo protocolo do driver oficial.
// Os pacotes estão explicados em docs/protocolo-monsgeek.md.
//
// Só mandamos o que o driver oficial usa: ler informações, iluminação e Hz.
// Nada de firmware, reset de fábrica, calibração ou mapa de teclas.
//
// Hz: o driver oficial só mostra essa opção com firmware USB >= 0x0400. Liberamos mesmo assim
// (decisão do usuário), com o mesmo pacote que o driver manda nos firmwares novos.
public static class MonsGeekKeyboard
{
    private const int Vid = 0x3151;
    private const int Pid = 0x502D;
    public const string HardwareId = "VID_3151&PID_502D";

    // Vários teclados usam esse mesmo PID, então o modelo é confirmado pelo ID que o próprio teclado informa.
    private static readonly Dictionary<int, string> KnownModels = new()
    {
        [2304] = "FUN60 PRO",
    };

    private const byte CmdGetInfo = 0x8F;
    private const byte CmdSetLighting = 0x07;
    private const byte CmdGetLighting = 0x87;
    private const byte CmdSetReportRate = 0x03;
    private const byte CmdGetReportRate = 0x83;

    // No pacote vai a posição nesta lista (0 = 8000 Hz ... 6 = 125 Hz).
    private static readonly int[] RateTable = [8000, 4000, 2000, 1000, 500, 250, 125];
    public static readonly IReadOnlyList<int> ReportRates = [125, 250, 500, 1000, 2000, 4000, 8000];

    private const byte MaxSpeed = 4;
    // O código do site chama o 8 de "multicor", mas no teclado é o contrário:
    // o driver oficial mandou 8 quando o usuário escolheu vermelho, e com 7 a cor é ignorada.
    private const byte SingleColor = 8;
    private const byte Multicolor = 7;

    public sealed record Effect(byte Id, string Name, bool UsesColor = true, byte DefaultOption = 0);

    // Os efeitos que o driver oficial oferece para o FUN60 PRO, com o número que vai no pacote.
    public static readonly IReadOnlyList<Effect> Effects =
    [
        new(1, "Estático"),
        new(2, "Respiração"),
        new(3, "Neon", UsesColor: false),
        new(4, "Onda", DefaultOption: 2), // nesse efeito só as opções 2 e 3 (para baixo/para cima) existem
        new(5, "Ondulação"),
        new(6, "Gotas"),
        new(7, "Cobra"),
        new(8, "Ao pressionar"),
        new(9, "Convergir"),
        new(10, "Senoide"),
        new(11, "Caleidoscópio"),
        new(12, "Linhas"),
        new(14, "Laser"),
        new(15, "Onda circular"),
        new(16, "Brilhante"),
        new(17, "Chuva"),
        new(18, "Meteoro"),
        new(19, "Apaga ao pressionar"),
        new(23, "Trem"),
        new(24, "Fogos"),
    ];

    // Velocidade e brilho vão de 0 a 4. "Option" muda conforme o efeito (ex.: sentido da onda).
    public sealed record Lighting(byte Effect, byte Speed, byte Brightness, byte Option, bool Multicolor, byte R, byte G, byte B);

    public sealed record State(string Model, Lighting Lighting, int? ReportRateHz);

    // Lê o modelo, a iluminação e o Hz. Retorna null se não houver um modelo conhecido conectado.
    public static State? Query()
    {
        using var channel = Channel.Open();
        if (channel?.Model == null)
            return null;

        var lighting = channel.Request(CmdGetLighting) is byte[] l ? ParseLighting(l) : null;
        if (lighting == null)
            return null;

        return new State(channel.Model, lighting, ReadReportRate(channel));
    }

    public static void SetLighting(Lighting lighting)
    {
        using var channel = Channel.Open();
        if (channel?.Model == null)
            throw new InvalidOperationException("O teclado não está mais conectado.");

        // O driver oficial troca o branco puro por FAFFFA antes de enviar; fazemos igual.
        var (r, g, b) = (lighting.R, lighting.G, lighting.B) == (255, 255, 255)
            ? ((byte)0xFA, (byte)0xFF, (byte)0xFA)
            : (lighting.R, lighting.G, lighting.B);

        byte[] message =
        [
            CmdSetLighting,
            lighting.Effect,
            (byte)(MaxSpeed - Math.Min(lighting.Speed, MaxSpeed)), // no pacote a velocidade vai invertida
            Math.Min(lighting.Brightness, (byte)4),
            (byte)((lighting.Option << 4) | (lighting.Multicolor ? Multicolor : SingleColor)),
            r, g, b,
        ];
        channel.Write(message, checksumAt: 8);
    }

    public static void SetReportRate(int hz)
    {
        var index = Array.IndexOf(RateTable, hz);
        if (index < 0)
            throw new ArgumentException($"{hz} Hz não é uma opção do teclado.");

        using var channel = Channel.Open();
        if (channel?.Model == null)
            throw new InvalidOperationException("O teclado não está mais conectado.");

        channel.Write([CmdSetReportRate, 0, (byte)index], checksumAt: 7);
    }

    // Resposta do 0x87: [cmd, efeito, 4 - velocidade, brilho, opção << 4 | modo de cor, R, G, B].
    private static Lighting? ParseLighting(byte[] r)
    {
        var (red, green, blue) = (r[5], r[6], r[7]) == (0xFA, 0xFF, 0xFA) ? ((byte)255, (byte)255, (byte)255) : (r[5], r[6], r[7]);
        return new Lighting(
            Effect: r[1],
            Speed: (byte)(MaxSpeed - Math.Min(r[2], MaxSpeed)),
            Brightness: r[3],
            Option: (byte)(r[4] >> 4),
            Multicolor: (r[4] & 0x0F) == Multicolor,
            R: red, G: green, B: blue);
    }

    // Resposta do 0x83: o byte 2 é a posição na RateTable.
    private static int? ReadReportRate(Channel channel) =>
        channel.Request(CmdGetReportRate) is byte[] r && r[2] < RateTable.Length ? RateTable[r[2]] : null;

    // A interface de configuração do teclado: feature reports de 64 bytes (65 com o report ID 0 na frente).
    private sealed class Channel : IDisposable
    {
        private const int ReportLength = 65;

        // O driver oficial espera 1 s depois de cada mudança antes de mandar outra.
        private static readonly object WriteLock = new();
        private static DateTime _lastWrite = DateTime.MinValue;

        private readonly HidStream _stream;
        public string? Model { get; }

        private Channel(HidStream stream)
        {
            _stream = stream;
            if (Request(CmdGetInfo) is byte[] info && KnownModels.TryGetValue((info[2] << 8) | info[1], out var model))
                Model = model;
        }

        public static Channel? Open()
        {
            // A configuração fica na interface 2 (MI_02); as outras são as teclas normais, mídia etc.
            var device = DeviceList.Local.GetHidDevices(Vid, Pid).FirstOrDefault(d =>
                IsConfigInterface(d) && FeatureLength(d) == ReportLength);
            if (device == null || !device.TryOpen(out HidStream stream))
                return null;

            try
            {
                return new Channel(stream);
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }

        // No Windows o caminho do aparelho diz a interface ("mi_02"). No Linux o caminho é só
        // /dev/hidrawN, então reconhecemos pela "usage" da interface de configuração (0xFFFF / 0x02).
        private static bool IsConfigInterface(HidDevice d)
        {
            if (OperatingSystem.IsWindows())
                return d.DevicePath.Contains("mi_02", StringComparison.OrdinalIgnoreCase);
            try
            {
                return d.GetReportDescriptor().DeviceItems.Any(item => item.Usages.GetAllValues().Contains(0xFFFF0002u));
            }
            catch
            {
                return false;
            }
        }

        private static int FeatureLength(HidDevice d)
        {
            try { return d.GetMaxFeatureReportLength(); }
            catch { return 0; }
        }

        // Pede uma informação e lê a resposta. Retorna os 64 bytes, ou null se a resposta não for desse pedido.
        public byte[]? Request(byte command)
        {
            Send([command], checksumAt: 7);
            Thread.Sleep(10);

            var buffer = new byte[ReportLength];
            _stream.GetFeature(buffer);
            return buffer[1] == command ? buffer[1..] : null;
        }

        public void Write(byte[] message, int checksumAt)
        {
            lock (WriteLock)
            {
                var wait = _lastWrite.AddSeconds(1) - DateTime.UtcNow;
                if (wait > TimeSpan.Zero)
                    Thread.Sleep(wait);

                Send(message, checksumAt);
                _lastWrite = DateTime.UtcNow;
            }
        }

        // checksum = 0xFF - (soma dos bytes anteriores). Fica no byte 7 na maioria dos comandos e no 8 na iluminação.
        private void Send(byte[] message, int checksumAt)
        {
            var data = new byte[ReportLength - 1];
            message.CopyTo(data, 0);
            data[checksumAt] = (byte)(0xFF - data.Take(checksumAt).Sum(b => b));

            var buffer = new byte[ReportLength]; // [0] = report ID 0
            data.CopyTo(buffer, 1);
            _stream.SetFeature(buffer);
        }

        public void Dispose() => _stream.Dispose();
    }
}
