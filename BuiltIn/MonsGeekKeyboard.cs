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

    // Key: a chave do nome na tela (Assets/Lang, "effect.<Key>").
    public sealed record Effect(byte Id, string Key, bool UsesColor = true, byte DefaultOption = 0)
    {
        public string Name => Lang.T("effect." + Key);
    }

    // Os efeitos que o driver oficial oferece para o FUN60 PRO, com o número que vai no pacote.
    public static readonly IReadOnlyList<Effect> Effects =
    [
        new(1, "static"),
        new(2, "breath"),
        new(3, "neon", UsesColor: false),
        new(4, "wave", DefaultOption: 2), // nesse efeito só as opções 2 e 3 (para baixo/para cima) existem
        new(5, "ripple"),
        new(6, "raindrop"),
        new(7, "snake"),
        new(8, "press"),
        new(9, "converge"),
        new(10, "sine"),
        new(11, "kaleidoscope"),
        new(12, "lines"),
        new(14, "laser"),
        new(15, "circle"),
        new(16, "dazzle"),
        new(17, "rain"),
        new(18, "meteor"),
        new(19, "pressOff"),
        new(23, "train"),
        new(24, "fireworks"),
    ];

    // Velocidade e brilho vão de 0 a 4. "Option" muda conforme o efeito (ex.: sentido da onda).
    public sealed record Lighting(byte Effect, byte Speed, byte Brightness, byte Option, bool Multicolor, byte R, byte G, byte B);

    public sealed record State(string Model, Lighting Lighting, int? ReportRateHz);

    // O que aconteceu na última busca pelo teclado (vai para o rodapé e para o diagnóstico).
    // Null se nenhuma busca foi feita ainda.
    public static string? LastProbe { get; private set; }

    // Lê o modelo, a iluminação e o Hz. Retorna null se não houver um modelo conhecido conectado
    // (o motivo fica em LastProbe).
    public static State? Query()
    {
        try
        {
            using var channel = Channel.Open(out var problem);
            if (channel == null)
            {
                LastProbe = problem;
                return null;
            }
            if (channel.Model == null)
            {
                LastProbe = channel.ModelId is int id ? Lang.T("akko.unknownModel", id) : Lang.T("akko.noInfo");
                return null;
            }

            var lighting = channel.Request(CmdGetLighting) is byte[] l ? ParseLighting(l) : null;
            if (lighting == null)
            {
                LastProbe = Lang.T("akko.noLighting");
                return null;
            }

            LastProbe = Lang.T("akko.ok", channel.Model, channel.ModelId);
            return new State(channel.Model, lighting, ReadReportRate(channel));
        }
        catch (Exception ex)
        {
            LastProbe = Lang.T("akko.error", ex.Message);
            throw;
        }
    }

    // Envia e depois pergunta ao teclado a iluminação atual, para ter certeza de que ele aplicou.
    // Se ele não confirmar, dá erro com o motivo (nunca "deu certo" sem saber).
    public static void SetLighting(Lighting lighting)
    {
        using var channel = Channel.Open(out var problem);
        if (channel == null)
            throw new InvalidOperationException(Lang.T("error.keyboardDisconnected") + " (" + problem + ")");
        if (channel.Model == null)
            throw new InvalidOperationException(Lang.T("error.keyboardDisconnected"));

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
        Confirm(channel, lighting);
    }

    // Lê a iluminação de volta (algumas tentativas: o teclado leva um instante para trocar).
    private static void Confirm(Channel channel, Lighting sent)
    {
        string? mismatch = null;
        for (int attempt = 0; attempt < 4; attempt++)
        {
            Thread.Sleep(attempt == 0 ? 80 : 200);
            if (channel.Request(CmdGetLighting) is not byte[] reply)
                continue;
            mismatch = Mismatch(sent, ParseLighting(reply));
            if (mismatch == null)
                return;
        }
        throw new InvalidOperationException(mismatch == null
            ? Lang.T("error.keyboardNoConfirm")
            : Lang.T("error.keyboardIgnored", mismatch));
    }

    // Compara o que foi enviado com o que o teclado diz que está usando. Só o que importa para a cor:
    // efeito, brilho e (em cor única, com os LEDs acesos) a cor. Devolve null se bate, ou o que mudou.
    internal static string? Mismatch(Lighting sent, Lighting read)
    {
        var problems = new List<string>();
        if (read.Effect != sent.Effect)
            problems.Add($"{Lang.T("control.effect")} {sent.Effect} → {read.Effect}");
        if (read.Brightness != Math.Min(sent.Brightness, (byte)4))
            problems.Add($"{Lang.T("control.brightness")} {sent.Brightness} → {read.Brightness}");
        if (!sent.Multicolor && sent.Brightness > 0)
        {
            if (read.Multicolor)
                problems.Add(Lang.T("akko.stillMulticolor"));
            else if ((read.R, read.G, read.B) != (sent.R, sent.G, sent.B))
                problems.Add($"{Lang.T("control.color")} #{sent.R:X2}{sent.G:X2}{sent.B:X2} → #{read.R:X2}{read.G:X2}{read.B:X2}");
        }
        return problems.Count == 0 ? null : string.Join(", ", problems);
    }

    public static void SetReportRate(int hz)
    {
        var index = Array.IndexOf(RateTable, hz);
        if (index < 0)
            throw new ArgumentException(Lang.T("error.badRate", hz));

        using var channel = Channel.Open(out _);
        if (channel?.Model == null)
            throw new InvalidOperationException(Lang.T("error.keyboardDisconnected"));

        channel.Write([CmdSetReportRate, 0, (byte)index], checksumAt: 7);
    }

    // Resposta do 0x87: [cmd, efeito, 4 - velocidade, brilho, opção << 4 | modo de cor, R, G, B].
    private static Lighting ParseLighting(byte[] r)
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
        public int? ModelId { get; } // o ID que o teclado informa (2304 = FUN60 PRO)

        private Channel(HidStream stream)
        {
            _stream = stream;
            if (Request(CmdGetInfo) is byte[] info)
            {
                ModelId = (info[2] << 8) | info[1];
                Model = KnownModels.GetValueOrDefault(ModelId.Value);
            }
        }

        // Abre a interface de configuração. Se não der, "problem" diz o porquê.
        public static Channel? Open(out string problem)
        {
            // A configuração fica na interface 2 (MI_02); as outras são as teclas normais, mídia etc.
            var all = DeviceList.Local.GetHidDevices(Vid, Pid).ToList();
            var device = all.FirstOrDefault(d => IsConfigInterface(d) && FeatureLength(d) == ReportLength);
            if (device == null)
            {
                problem = all.Count == 0 ? Lang.T("akko.notFound")
                    : Lang.T("akko.noConfigInterface", all.Count,
                        string.Join("; ", all.Select(d => $"{ShortPath(d)} feature={FeatureLength(d)}")));
                return null;
            }
            if (!device.TryOpen(out HidStream stream))
            {
                problem = OperatingSystem.IsLinux() ? Lang.T("error.noPermissionLinux") : Lang.T("error.busy");
                return null;
            }
            problem = "";

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

        // O fim do caminho do aparelho já diz a interface (ex.: "...&mi_02#...") sem expor o resto.
        private static string ShortPath(HidDevice d)
        {
            var path = d.DevicePath;
            var mi = path.IndexOf("mi_", StringComparison.OrdinalIgnoreCase);
            return mi >= 0 ? path.Substring(mi, Math.Min(5, path.Length - mi)) : Path.GetFileName(path);
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
