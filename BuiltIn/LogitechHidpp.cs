using System.Text;
using HidSharp;

namespace Perialls;

// HID++ 2.0: o protocolo que mouses e teclados Logitech usam para informar bateria, DPI etc.
// É só um extra: quem não tem Logitech nunca passa por aqui.
//
// Cada mensagem tem 20 bytes: [0x11, aparelho, recurso, função, parâmetros...].
// "Aparelho" é o número do aparelho no receptor (1 a 6) ou 0xFF para aparelho ligado direto no cabo.
// "Recurso" (feature) é um número que o próprio aparelho informa para cada capacidade
// (ex.: o recurso de bateria pode ser o 6 num mouse e o 8 em outro). Por isso perguntamos antes.
public static class LogitechHidpp
{
    private const int LogitechVid = 0x046D;

    // Códigos dos recursos HID++ que usamos.
    private const ushort FeatureDeviceName = 0x0005;
    private const ushort FeatureDeviceInfo = 0x0003;
    private const ushort FeatureBatteryLevel = 0x1000;
    private const ushort FeatureBatteryVoltage = 0x1001;
    private const ushort FeatureUnifiedBattery = 0x1004;
    private const ushort FeatureAdjustableDpi = 0x2201;

    public sealed class Device
    {
        public byte Index { get; init; }
        public string Name { get; init; } = "";
        public int Type { get; init; } = -1;  // 0 = teclado, 3 = mouse, 8 = headset... (tabela da Logitech)
        public string? HardwareId { get; init; }
        public string Battery { get; init; } = "—";
        public int? Dpi { get; init; }
        public List<int> DpiPresets { get; init; } = [];
    }

    // Lê os aparelhos Logitech ligados a um receptor (ou o próprio aparelho, se for com fio).
    // Retorna null se esse produto não fala HID++.
    public static List<Device>? Query(int productId)
    {
        using var channel = Channel.Open(productId);
        if (channel == null)
            return null;

        var devices = new List<Device>();
        foreach (var index in DeviceIndexes(productId))
        {
            try
            {
                var device = ReadDevice(channel, index);
                if (device != null)
                    devices.Add(device);
            }
            catch
            {
                // Um aparelho que responde errado não pode atrapalhar os outros.
            }
        }
        return devices;
    }

    public static void SetDpi(int productId, byte index, int dpi)
    {
        using var channel = Channel.Open(productId)
            ?? throw new InvalidOperationException(Lang.T("error.disconnected"));
        var feature = channel.FindFeature(index, FeatureAdjustableDpi)
            ?? throw new InvalidOperationException(Lang.T("error.noDpi"));
        _ = channel.Request(index, feature, 3, 0, (byte)(dpi >> 8), (byte)dpi)
            ?? throw new InvalidOperationException(Lang.T("error.noReply"));
    }

    // Receptores (Unifying, Lightspeed, Bolt) usam códigos 0xC5xx e guardam até 6 aparelhos.
    private static IEnumerable<byte> DeviceIndexes(int productId) =>
        (productId & 0xFF00) == 0xC500 ? [1, 2, 3, 4, 5, 6] : [0xFF];

    private static Device? ReadDevice(Channel channel, byte index)
    {
        // "Ping": se não responder, não há aparelho ligado nesse número.
        if (channel.Request(index, 0, 1) == null)
            return null;

        string name = "";
        int type = -1;
        if (channel.FindFeature(index, FeatureDeviceName) is byte nameFeature)
        {
            var length = channel.Request(index, nameFeature, 0)?[0] ?? 0;
            var sb = new StringBuilder();
            while (sb.Length < length && channel.Request(index, nameFeature, 1, (byte)sb.Length) is byte[] chunk)
                sb.Append(Encoding.ASCII.GetString(chunk, 0, Math.Min(16, length - sb.Length)));
            name = sb.ToString().TrimEnd('\0');
            type = channel.Request(index, nameFeature, 2)?[0] ?? -1;
        }

        // O código USB próprio do aparelho (ex.: 4093 para o PRO X Superlight), diferente do receptor.
        string? hardwareId = null;
        if (channel.FindFeature(index, FeatureDeviceInfo) is byte infoFeature &&
            channel.Request(index, infoFeature, 0) is byte[] info && (info[7] | info[8]) != 0)
            hardwareId = $"VID_046D&PID_{info[7]:X2}{info[8]:X2}";

        var (dpi, presets) = ReadDpi(channel, index);

        return new Device
        {
            Index = index,
            Name = name,
            Type = type,
            HardwareId = hardwareId,
            Battery = ReadBattery(channel, index),
            Dpi = dpi,
            DpiPresets = presets,
        };
    }

    // Há três jeitos de informar bateria, conforme a idade do aparelho. Tentamos do mais novo ao mais antigo.
    private static string ReadBattery(Channel channel, byte index)
    {
        if (channel.FindFeature(index, FeatureUnifiedBattery) is byte unified &&
            channel.Request(index, unified, 1) is byte[] u)
        {
            // [porcentagem, nível, estado de carga]
            var level = u[0] > 0 ? $"{u[0]}%" : u[1] switch
            {
                >= 8 => Lang.T("battery.full"),
                >= 4 => Lang.T("battery.good"),
                >= 2 => Lang.T("battery.low"),
                _ => Lang.T("battery.critical"),
            };
            return level + ChargingText(u[2]);
        }

        if (channel.FindFeature(index, FeatureBatteryLevel) is byte simple &&
            channel.Request(index, simple, 0) is byte[] s)
        {
            // [porcentagem, próximo nível, estado de carga]
            return $"{s[0]}%" + ChargingText(s[2]);
        }

        if (channel.FindFeature(index, FeatureBatteryVoltage) is byte voltage &&
            channel.Request(index, voltage, 0) is byte[] v)
        {
            // [tensão em milivolts (2 bytes), flags]. A porcentagem é uma estimativa pela tensão.
            var millivolts = (v[0] << 8) | v[1];
            return $"~{PercentFromVoltage(millivolts)}%" + ((v[2] & 0x80) != 0 ? Lang.T("battery.charging") : "");
        }

        return "—";
    }

    private static string ChargingText(byte status) => status switch
    {
        1 or 2 => Lang.T("battery.charging"),
        3 => Lang.T("battery.charged"),
        _ => "",
    };

    // Curva aproximada de uma bateria de lítio: tensão (mV) → porcentagem.
    private static int PercentFromVoltage(int mv)
    {
        (int Mv, int Pct)[] curve =
            [(4186, 100), (4067, 90), (3989, 80), (3922, 70), (3859, 60), (3811, 50),
             (3778, 40), (3751, 30), (3717, 20), (3671, 10), (3646, 5), (3579, 2), (3500, 0)];
        if (mv >= curve[0].Mv) return 100;
        for (int i = 1; i < curve.Length; i++)
        {
            if (mv >= curve[i].Mv)
            {
                var (hiMv, hiPct) = curve[i - 1];
                var (loMv, loPct) = curve[i];
                return loPct + (mv - loMv) * (hiPct - loPct) / (hiMv - loMv);
            }
        }
        return 0;
    }

    // Lê o DPI atual e monta uma lista de valores rápidos dentro do que o sensor aceita.
    private static (int? Dpi, List<int> Presets) ReadDpi(Channel channel, byte index)
    {
        if (channel.FindFeature(index, FeatureAdjustableDpi) is not byte feature)
            return (null, []);

        var current = channel.Request(index, feature, 2, 0);
        var list = channel.Request(index, feature, 1, 0);
        if (current == null || list == null)
            return (null, []);

        // A lista vem em pares de bytes depois do número do sensor. Um valor 0xE0xx significa
        // "de X até Y, de xx em xx". Senão, são valores fixos. Termina em 0.
        var values = new List<int>();
        int? step = null;
        for (int i = 1; i + 1 < list.Length; i += 2)
        {
            var value = (list[i] << 8) | list[i + 1];
            if (value == 0) break;
            if (value >= 0xE000) step = value & 0x1FFF;
            else values.Add(value);
        }
        if (values.Count == 0)
            return (null, []);

        int[] common = [400, 800, 1200, 1600, 2400, 3200, 6400];
        var presets = step != null
            ? common.Where(d => d >= values.Min() && d <= values.Max() && (d - values.Min()) % step.Value == 0).ToList()
            : values.Count <= 7 ? values : common.Where(values.Contains).ToList();

        return ((current[1] << 8) | current[2], presets);
    }

    // A conexão com o receptor: um canal "longo" (20 bytes) para mandar e receber,
    // e o "curto" (7 bytes) só para ouvir o aviso de "não tem aparelho nesse número" mais rápido.
    // No Linux os dois chegam pelo mesmo /dev/hidraw, então pode não haver um canal curto separado.
    private sealed class Channel : IDisposable
    {
        private const byte SoftwareId = 0x0B; // identifica nossas mensagens nas respostas
        private const byte ShortReport = 0x10;
        private const byte LongReport = 0x11;
        private readonly HidStream _long;
        private readonly HidStream? _short;
        private readonly Dictionary<(byte, ushort), byte?> _features = [];

        private Channel(HidStream longStream, HidStream? shortStream)
        {
            _long = longStream;
            _short = shortStream;
            _long.ReadTimeout = 20;
            if (_short != null) _short.ReadTimeout = 20;
        }

        public static Channel? Open(int productId)
        {
            var all = DeviceList.Local.GetHidDevices(LogitechVid, productId).ToList();
            var longDevice = all.FirstOrDefault(d => Accepts(d, LongReport, 20));
            if (longDevice == null || !longDevice.TryOpen(out HidStream longStream))
                return null;

            var shortDevice = all.FirstOrDefault(d => d != longDevice && Accepts(d, ShortReport, 7));
            HidStream? shortStream = null;
            shortDevice?.TryOpen(out shortStream);
            return new Channel(longStream, shortStream);
        }

        // No Windows cada tipo de mensagem é um aparelho separado, com o seu tamanho.
        // No Linux um /dev/hidraw aceita vários tipos, então olhamos a lista de mensagens dele.
        private static bool Accepts(HidDevice d, byte reportId, int length)
        {
            try
            {
                return OperatingSystem.IsWindows()
                    ? d.GetMaxOutputReportLength() == length
                    : d.GetReportDescriptor().OutputReports.Any(r => r.ReportID == reportId);
            }
            catch
            {
                return false;
            }
        }

        // Pergunta ao aparelho em que número está um recurso (0 = não tem).
        public byte? FindFeature(byte index, ushort featureId)
        {
            if (!_features.TryGetValue((index, featureId), out var feature))
            {
                var reply = Request(index, 0, 0, (byte)(featureId >> 8), (byte)featureId);
                feature = reply is { } r && r[0] != 0 ? r[0] : null;
                _features[(index, featureId)] = feature;
            }
            return feature;
        }

        // Manda um pedido e espera a resposta certa (ignorando outras mensagens no caminho).
        // Retorna os 16 bytes de parâmetros, ou null se não houve resposta ou deu erro.
        public byte[]? Request(byte index, byte feature, byte function, params byte[] args)
        {
            var message = new byte[20];
            message[0] = LongReport;
            message[1] = index;
            message[2] = feature;
            message[3] = (byte)((function << 4) | SoftwareId);
            args.CopyTo(message, 4);
            _long.Write(message);

            // O buffer tem o tamanho da maior mensagem que o aparelho manda (no Linux pode passar de 20).
            var buffer = new byte[Math.Max(20, _long.Device.GetMaxInputReportLength())];
            var deadline = DateTime.UtcNow.AddMilliseconds(600);
            while (DateTime.UtcNow < deadline)
            {
                if (TryRead(_long, buffer) && buffer[1] == index)
                {
                    if (buffer[0] == LongReport && buffer[2] == 0xFF && buffer[3] == feature) return null; // erro HID++ 2.0
                    if (buffer[0] == LongReport && buffer[2] == feature && buffer[3] == message[3]) return buffer[4..20];
                    if (IsNoDevice(buffer)) return null;
                }
                if (_short != null && TryRead(_short, buffer) && buffer[1] == index && IsNoDevice(buffer))
                    return null;
            }
            return null;
        }

        // Mensagem curta do receptor avisando: nenhum aparelho nesse número (ou desligado).
        private static bool IsNoDevice(byte[] buffer) => buffer[0] == ShortReport && buffer[2] == 0x8F;

        private static bool TryRead(HidStream stream, byte[] buffer)
        {
            try { return stream.Read(buffer) >= 4; }
            catch (TimeoutException) { return false; }
        }

        public void Dispose()
        {
            _long.Dispose();
            _short?.Dispose();
        }
    }
}
