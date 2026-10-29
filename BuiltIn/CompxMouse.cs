using HidSharp;

namespace Perialls;

// Mouses com chip Compx (ATK, VXE, MAD R...): DPI, Hz e bateria pelo mesmo protocolo do driver
// oficial (ATK V HUB, hub.atk.pro). Os pacotes estão explicados em docs/protocolo-compx.md.
//
// O mouse guarda as configurações numa memória (EEPROM) dividida em endereços. Mudar algo é gravar
// nessa memória, então tudo continua valendo depois de desligar.
//
// Só mandamos o que o driver oficial usa para ler e para mudar DPI e Hz, e só gravamos nos endereços
// de Hz e DPI. Nada de firmware, pareamento, reset de fábrica, mapa de botões ou macros.
public static class CompxMouse
{
    // Os mouses e receptores que o driver oficial liga a este protocolo (VID 0x373B: ATK/VXE;
    // 0x3554: o código da própria Compx). Outros aparelhos desses fabricantes, como os teclados ATK,
    // usam outros protocolos e não recebem nada daqui.
    public static readonly HashSet<string> HardwareIds =
    [
        "VID_3554&PID_F2FF", "VID_3554&PID_F400", "VID_3554&PID_F401", "VID_3554&PID_F402",
        "VID_3554&PID_F403", "VID_3554&PID_F404", "VID_3554&PID_F405", "VID_3554&PID_F406",
        "VID_3554&PID_F408", "VID_3554&PID_F40A", "VID_3554&PID_F40C", "VID_3554&PID_F40D",
        "VID_3554&PID_F40E", "VID_3554&PID_F40F", "VID_3554&PID_F410", "VID_3554&PID_F502",
        "VID_3554&PID_F503", "VID_3554&PID_F505", "VID_3554&PID_F506", "VID_3554&PID_F50A",
        "VID_3554&PID_F50B", "VID_3554&PID_F58A", "VID_3554&PID_F58C", "VID_3554&PID_F58D",
        "VID_3554&PID_F58E", "VID_3554&PID_F58F", "VID_3554&PID_FB12", "VID_373B&PID_1014",
        "VID_373B&PID_1015", "VID_373B&PID_1016", "VID_373B&PID_1017", "VID_373B&PID_1018",
        "VID_373B&PID_1019", "VID_373B&PID_101A", "VID_373B&PID_101B", "VID_373B&PID_102E",
        "VID_373B&PID_102F", "VID_373B&PID_1030", "VID_373B&PID_1031", "VID_373B&PID_103C",
        "VID_373B&PID_103D", "VID_373B&PID_103E", "VID_373B&PID_103F", "VID_373B&PID_1040",
        "VID_373B&PID_1045", "VID_373B&PID_104A", "VID_373B&PID_104B", "VID_373B&PID_104C",
        "VID_373B&PID_104D", "VID_373B&PID_105B", "VID_373B&PID_1065", "VID_373B&PID_1066",
        "VID_373B&PID_1067", "VID_373B&PID_1068", "VID_373B&PID_1069", "VID_373B&PID_106A",
        "VID_373B&PID_1084", "VID_373B&PID_1085", "VID_373B&PID_1086", "VID_373B&PID_1087",
        "VID_373B&PID_10AC", "VID_373B&PID_10C7", "VID_373B&PID_10C8", "VID_373B&PID_10C9",
        "VID_373B&PID_10CA", "VID_373B&PID_10CB", "VID_373B&PID_10CC", "VID_373B&PID_10E7",
        "VID_373B&PID_10E8", "VID_373B&PID_10E9", "VID_373B&PID_10FD", "VID_373B&PID_1106",
        "VID_373B&PID_1107", "VID_373B&PID_110D", "VID_373B&PID_110E", "VID_373B&PID_1115",
        "VID_373B&PID_1116", "VID_373B&PID_1117", "VID_373B&PID_1119", "VID_373B&PID_111A",
        "VID_373B&PID_111B", "VID_373B&PID_1135", "VID_373B&PID_113E", "VID_373B&PID_113F",
        "VID_373B&PID_1149", "VID_373B&PID_114A", "VID_373B&PID_114B", "VID_373B&PID_114C",
        "VID_373B&PID_1157", "VID_373B&PID_1158", "VID_373B&PID_1159", "VID_373B&PID_115B",
        "VID_373B&PID_115C", "VID_373B&PID_115D", "VID_373B&PID_115E", "VID_373B&PID_11A0",
        "VID_373B&PID_11A6", "VID_373B&PID_11A7", "VID_373B&PID_11A9", "VID_373B&PID_11AA",
        "VID_373B&PID_11AB", "VID_373B&PID_11B3", "VID_373B&PID_11B6", "VID_373B&PID_11C8",
        "VID_373B&PID_11CC", "VID_373B&PID_11CD", "VID_373B&PID_11CE", "VID_373B&PID_11CF",
        "VID_373B&PID_11D2", "VID_373B&PID_11D8", "VID_373B&PID_11D9", "VID_373B&PID_11DD",
        "VID_373B&PID_11E3", "VID_373B&PID_11E4", "VID_373B&PID_11E5", "VID_373B&PID_11E7",
        "VID_373B&PID_11EC", "VID_373B&PID_11ED", "VID_373B&PID_11F0", "VID_373B&PID_11F2",
        "VID_373B&PID_11F3", "VID_373B&PID_11F4", "VID_373B&PID_11F5", "VID_373B&PID_11F6",
        "VID_373B&PID_11F7", "VID_373B&PID_11F9", "VID_373B&PID_11FD", "VID_373B&PID_11FE",
        "VID_373B&PID_1201", "VID_373B&PID_120A", "VID_373B&PID_120B", "VID_373B&PID_120D",
        "VID_373B&PID_120E", "VID_373B&PID_121F", "VID_373B&PID_1230", "VID_373B&PID_1245",
        "VID_373B&PID_1246", "VID_373B&PID_1248", "VID_373B&PID_1249", "VID_373B&PID_124A",
        "VID_373B&PID_124E", "VID_373B&PID_1256", "VID_373B&PID_125A", "VID_373B&PID_1266",
        "VID_373B&PID_1269", "VID_373B&PID_126C", "VID_373B&PID_126D", "VID_373B&PID_126E",
        "VID_373B&PID_126F", "VID_373B&PID_1270", "VID_373B&PID_1271", "VID_373B&PID_1272",
        "VID_373B&PID_1273", "VID_373B&PID_1274", "VID_373B&PID_1278", "VID_373B&PID_1279",
        "VID_373B&PID_127A", "VID_373B&PID_127C", "VID_373B&PID_127E", "VID_373B&PID_127F",
        "VID_373B&PID_1285", "VID_373B&PID_1286", "VID_373B&PID_1289", "VID_373B&PID_128A",
        "VID_373B&PID_128B", "VID_373B&PID_128C", "VID_373B&PID_128D", "VID_373B&PID_128E",
        "VID_373B&PID_1292", "VID_373B&PID_1293", "VID_373B&PID_1296", "VID_373B&PID_129B",
        "VID_373B&PID_129C", "VID_373B&PID_129D", "VID_373B&PID_12A2", "VID_373B&PID_12A7",
        "VID_373B&PID_12A8", "VID_373B&PID_12A9", "VID_373B&PID_12AB", "VID_373B&PID_12AF",
        "VID_373B&PID_12B0", "VID_373B&PID_12B3", "VID_373B&PID_12B4", "VID_373B&PID_12BF",
        "VID_373B&PID_12C6", "VID_373B&PID_12CC", "VID_373B&PID_12D7", "VID_373B&PID_12DA",
        "VID_373B&PID_12E9", "VID_373B&PID_12F7", "VID_373B&PID_12F9", "VID_373B&PID_12FA",
        "VID_373B&PID_12FB", "VID_373B&PID_12FF", "VID_373B&PID_1303", "VID_373B&PID_1316",
        "VID_373B&PID_131A", "VID_373B&PID_132A", "VID_373B&PID_132B", "VID_373B&PID_133B",
        "VID_373B&PID_1340", "VID_373B&PID_1341", "VID_373B&PID_1342", "VID_373B&PID_1358",
        "VID_373B&PID_1359", "VID_373B&PID_1363",
    ];

    private const byte ReportId = 8;
    private const byte CmdHello = 0x01;       // apresentação: modelo e tipo de conexão
    private const byte CmdMouseOnline = 0x03; // receptor: o mouse está ligado?
    private const byte CmdBattery = 0x04;
    private const byte CmdWrite = 0x07;       // grava na memória do mouse
    private const byte CmdRead = 0x08;        // lê da memória do mouse
    private const byte CmdModel = 0x10;       // CID/MID: qual modelo é

    // Endereços na memória do mouse.
    private const int AddrRateBlock = 0x0000; // 10 bytes: Hz, nº de estágios de DPI, estágio atual...
    private const int AddrDpiStages = 0x000C; // 8 estágios de 4 bytes, lidos e gravados de 2 em 2
    private const int AddrDpiStages3955 = 0x1B00; // PAW3955: 8 estágios de 6 bytes

    // Hz → número que vai no pacote.
    private static readonly Dictionary<int, byte> RateCodes = new()
    {
        [125] = 8, [250] = 4, [500] = 2, [1000] = 1, [2000] = 16, [4000] = 32, [8000] = 64,
    };

    // Tipo de conexão (resposta da apresentação) → Hz máximo e se é sem fio.
    private static readonly Dictionary<byte, (int MaxHz, bool Wireless)> Connections = new()
    {
        [0] = (1000, true),  // receptor 1K
        [1] = (4000, true),  // receptor 4K
        [2] = (1000, false), // cabo 1K
        [3] = (8000, false), // cabo 8K
        [4] = (2000, true),  // receptor 2K
        [5] = (8000, true),  // receptor 8K
        [6] = (2000, false), // cabo 2K
        [7] = (4000, false), // cabo 4K
        [8] = (1000, true),  // NearLink
    };

    public enum Sensor { Paw3955Master, Paw3950Ultra, Paw3950Dm, Paw3950, Paw3395Ultra, Paw3395, Paw3315, Core26K, Paw3395Se, Paw3311, Paw3320 }

    // Faixa de DPI de cada sensor. Os que não estão aqui (3395 SE, 3311, 3315, 3320) usam tabelas próprias
    // de conversão que ainda não fizemos: esses mouses ficam só com Hz e bateria.
    private static readonly Dictionary<Sensor, (int Min, int Max)> DpiRange = new()
    {
        [Sensor.Paw3955Master] = (50, 50000),
        [Sensor.Paw3950Ultra] = (50, 42000),
        [Sensor.Paw3950Dm] = (50, 36000),
        [Sensor.Paw3950] = (50, 36000),
        [Sensor.Paw3395Ultra] = (100, 30000),
        [Sensor.Paw3395] = (100, 30000),
        [Sensor.Core26K] = (50, 26000),
    };

    // Modelos que o driver oficial conhece, pelo CID/MID que o mouse informa. O sensor decide como o DPI é escrito.
    private static readonly Dictionary<(byte Cid, byte Mid), (Sensor Sensor, string Name)> Models = new()
    {
        [(1, 2)] = (Sensor.Paw3950Ultra, "ATK Duckbill Ultra"),
        [(1, 3)] = (Sensor.Paw3950Ultra, "ATK A9 Ultimate"),
        [(1, 4)] = (Sensor.Paw3950Ultra, "ATK ZERO EX"),
        [(1, 5)] = (Sensor.Paw3950Ultra, "ATK Y9 Ultimate"),
        [(1, 6)] = (Sensor.Paw3395Ultra, "ATK A9 Ultra Max 2.0"),
        [(1, 7)] = (Sensor.Paw3950Ultra, "ATK F1 EXTREME 2.0"),
        [(1, 8)] = (Sensor.Paw3950Ultra, "ATK F1 Ultimate 2.0"),
        [(1, 9)] = (Sensor.Paw3950Ultra, "ATK X1 EXTREME 2.0"),
        [(1, 10)] = (Sensor.Paw3950Ultra, "ATK X1 Ultimate 2.0"),
        [(1, 11)] = (Sensor.Paw3395Ultra, "ATK X1 Ultra Max 2.0"),
        [(1, 12)] = (Sensor.Paw3395Ultra, "ATK F1 Ultra Max 2.0"),
        [(1, 13)] = (Sensor.Paw3950Ultra, "ATK F1 LEVIATAN 2.0"),
        [(1, 15)] = (Sensor.Paw3395Ultra, "ATK A9 Ultra Max 2.0 Tri-Mode"),
        [(1, 16)] = (Sensor.Paw3950Ultra, "ATK U2 Ultimate 2.0"),
        [(1, 17)] = (Sensor.Paw3950Ultra, "ATK U2 Mini 2.0"),
        [(1, 18)] = (Sensor.Paw3395Ultra, "ATK U2 Ultra Max 2.0"),
        [(1, 19)] = (Sensor.Paw3950Ultra, "ATK Z1 Ultimate 2.0"),
        [(1, 20)] = (Sensor.Paw3395Ultra, "VXE R1 Ultra"),
        [(1, 21)] = (Sensor.Paw3950Ultra, "ATK A9 Air"),
        [(1, 22)] = (Sensor.Paw3950Ultra, "ATK X1 EXTREME GT"),
        [(1, 23)] = (Sensor.Paw3950Ultra, "ATK Ghost EXTREME"),
        [(1, 24)] = (Sensor.Paw3950Ultra, "ATK Ghost Ultimate"),
        [(1, 25)] = (Sensor.Paw3950Ultra, "ATK Duckbill EXTREME"),
        [(1, 26)] = (Sensor.Paw3950Ultra, "ATK ZERO"),
        [(1, 27)] = (Sensor.Paw3950Ultra, "ATK A9 Mini"),
        [(1, 28)] = (Sensor.Paw3950Ultra, "ATK A9 Mini"),
        [(1, 30)] = (Sensor.Paw3950Ultra, "ATK X EXTREME"),
        [(1, 31)] = (Sensor.Paw3955Master, "ATK A9 Mini +"),
        [(1, 32)] = (Sensor.Paw3950Ultra, "ATK X1 Ultra Max 2.0"),
        [(1, 33)] = (Sensor.Paw3950Ultra, "ATK F1 Ultra Max 2.0"),
        [(1, 37)] = (Sensor.Paw3950Ultra, "ATK A9 Ultra Max 2.0 Tri-Mode"),
        [(1, 38)] = (Sensor.Paw3950Ultra, "ATK A9 Ultra Max 2.0"),
        [(1, 39)] = (Sensor.Paw3955Master, "ATK A9 Ultimate +"),
        [(1, 40)] = (Sensor.Paw3955Master, "ATK A9 Air +"),
        [(1, 42)] = (Sensor.Paw3955Master, "ATK X1 Air +"),
        [(1, 45)] = (Sensor.Paw3955Master, "ATK X1 EXTREME 2.0 +"),
        [(1, 46)] = (Sensor.Paw3955Master, "ATK X1 Ultimate 2.0 +"),
        [(1, 47)] = (Sensor.Paw3955Master, "ATK ZERO EX +"),
        [(1, 48)] = (Sensor.Paw3955Master, "ATK Ghost EXTREME +"),
        [(1, 49)] = (Sensor.Paw3955Master, "ATK Ghost Ultimate +"),
        [(1, 50)] = (Sensor.Paw3955Master, "ATK U2 Ultimate 2.0 +"),
        [(1, 51)] = (Sensor.Paw3955Master, "ATK U2 Mini 2.0 +"),
        [(1, 52)] = (Sensor.Paw3955Master, "ATK A9 Mini +"),
        [(1, 53)] = (Sensor.Paw3955Master, "ATK A9 Ultimate +"),
        [(1, 54)] = (Sensor.Paw3955Master, "ATK A9 Air +"),
        [(1, 55)] = (Sensor.Paw3955Master, "ATK PHANTOM ULTIMATE"),
        [(1, 58)] = (Sensor.Paw3955Master, "ATK F1 V3 EXTREME +"),
        [(1, 59)] = (Sensor.Paw3955Master, "ATK F1 Ultimate 2.0 +"),
        [(1, 60)] = (Sensor.Paw3955Master, "ATK F1 V3 LEVIATAN +"),
        [(1, 61)] = (Sensor.Paw3955Master, "ATK X1 Air +"),
        [(1, 62)] = (Sensor.Paw3955Master, "ATK F1 V3 ULTIMATE +"),
        [(1, 63)] = (Sensor.Paw3955Master, "ATK F1 V3 ULTIMATE +"),
        [(1, 64)] = (Sensor.Paw3955Master, "ATK F1 V3 ULTIMATE +"),
        [(1, 66)] = (Sensor.Paw3955Master, "ATK Y9 Ultimate +"),
        [(1, 67)] = (Sensor.Paw3955Master, "ATK Z1 Ultimate 2.0 +"),
        [(1, 71)] = (Sensor.Paw3955Master, "ATK A9 Wireless Charging"),
        [(1, 75)] = (Sensor.Paw3955Master, "ATK A9 Ultimate Silent"),
        [(1, 76)] = (Sensor.Paw3955Master, "ATK X1 Ultimate 2.0 Silent"),
        [(1, 80)] = (Sensor.Paw3955Master, "Mouse"),
        [(1, 84)] = (Sensor.Paw3955Master, "Mouse"),
        [(1, 85)] = (Sensor.Paw3950Ultra, "ATK F1 V3 ULTIMATE"),
        [(1, 86)] = (Sensor.Paw3955Master, "Mouse"),
        [(1, 87)] = (Sensor.Paw3950Dm, "ATK X1 Ultra Max 2.0"),
        [(1, 88)] = (Sensor.Paw3955Master, "Mouse"),
        [(1, 90)] = (Sensor.Paw3955Master, "ATK F1 V3 PIKACHU Edition"),
        [(1, 91)] = (Sensor.Paw3955Master, "Mouse"),
        [(1, 92)] = (Sensor.Paw3955Master, "Mouse"),
        [(2, 1)] = (Sensor.Paw3395, "ATK Dragonfly F1 PRO"),
        [(2, 2)] = (Sensor.Paw3395, "ATK Dragonfly F1 PRO"),
        [(2, 3)] = (Sensor.Paw3395, "ATK Dragonfly F1 PRO MAX"),
        [(2, 4)] = (Sensor.Paw3395, "ATK Dragonfly F1 PRO MAX"),
        [(2, 5)] = (Sensor.Paw3395, "ATK Dragonfly F1 MOBA"),
        [(2, 6)] = (Sensor.Paw3395, "ATK Dragonfly F1 MOBA"),
        [(2, 7)] = (Sensor.Paw3395, "VXE R1 PRO"),
        [(2, 8)] = (Sensor.Paw3395, "VXE R1 PRO"),
        [(2, 11)] = (Sensor.Paw3395, "VXE R1"),
        [(2, 12)] = (Sensor.Paw3395, "VXE R1"),
        [(2, 19)] = (Sensor.Paw3395, "ATK Dragonfly F1S"),
        [(2, 20)] = (Sensor.Paw3395, "ATK Dragonfly Dragonfly F1S"),
        [(2, 27)] = (Sensor.Paw3395, "VXE R1 PRO MAX"),
        [(2, 28)] = (Sensor.Paw3395, "VXE R1 PRO MAX"),
        [(2, 29)] = (Sensor.Paw3395Se, "VXE R1SE"),
        [(2, 30)] = (Sensor.Paw3395Se, "VXE R1SE"),
        [(2, 31)] = (Sensor.Paw3395Se, "VXE R1SE+"),
        [(2, 32)] = (Sensor.Paw3395Se, "VXE R1SE+"),
        [(2, 33)] = (Sensor.Paw3395, "ATK Dragonfly F1 MOBA"),
        [(2, 34)] = (Sensor.Paw3395, "VXE R1 PRO MAX"),
        [(2, 36)] = (Sensor.Paw3950, "ATK F1 PRO"),
        [(2, 37)] = (Sensor.Paw3950, "ATK F1 PRO MAX"),
        [(2, 38)] = (Sensor.Paw3950, "ATK X1 PRO"),
        [(2, 39)] = (Sensor.Paw3950, "ATK X1 PRO MAX"),
        [(2, 40)] = (Sensor.Paw3950, "ATK Z1 PRO"),
        [(2, 41)] = (Sensor.Paw3950, "ATK Z1 PRO MAX"),
        [(2, 42)] = (Sensor.Paw3950Ultra, "ATK F1 Ultimate"),
        [(2, 43)] = (Sensor.Paw3950Ultra, "ATK X1 Ultra"),
        [(2, 44)] = (Sensor.Paw3950Ultra, "ATK FIERCE X"),
        [(2, 45)] = (Sensor.Paw3950Ultra, "ATK F1 Ultra"),
        [(2, 46)] = (Sensor.Paw3950Ultra, "ATK X1 Ultimate"),
        [(2, 47)] = (Sensor.Paw3950, "MAD R MAJOR"),
        [(2, 48)] = (Sensor.Paw3395, "ATK Dragonfly F1 PRO MAX Elden"),
        [(2, 49)] = (Sensor.Paw3395, "MAD R"),
        [(2, 50)] = (Sensor.Paw3950Ultra, "ATK F1 EXTREME"),
        [(2, 52)] = (Sensor.Paw3395, "ATK Dragonfly F1 PRO MAX JOJO"),
        [(2, 53)] = (Sensor.Paw3950Ultra, "ATK Z1 Ultra"),
        [(2, 54)] = (Sensor.Paw3395, "MAD R +"),
        [(2, 55)] = (Sensor.Paw3950, "MAD R MAJOR+"),
        [(2, 56)] = (Sensor.Paw3950Ultra, "ATK F1 LEVIATAN"),
        [(2, 57)] = (Sensor.Paw3395Se, "VXE R1 NK"),
        [(2, 63)] = (Sensor.Paw3311, "VXE R1S+"),
        [(2, 64)] = (Sensor.Paw3950, "ATK U2 MINI"),
        [(2, 65)] = (Sensor.Paw3950Ultra, "ATK U2 Ultimate"),
        [(2, 66)] = (Sensor.Paw3950, "ATK U2 Ultra"),
        [(2, 67)] = (Sensor.Paw3395Se, "VXE R1 NK"),
        [(2, 70)] = (Sensor.Paw3950Ultra, "ATK X1 EXTREME"),
        [(2, 72)] = (Sensor.Paw3311, "VXE V3 NK"),
        [(2, 73)] = (Sensor.Paw3395Se, "VXE V3 PRO NK"),
        [(2, 76)] = (Sensor.Paw3395, "ATK X1 S NK"),
        [(2, 77)] = (Sensor.Paw3395Se, "ATK X1 SE NK"),
        [(2, 78)] = (Sensor.Paw3395, "ATK U2 Plus NK"),
        [(2, 79)] = (Sensor.Paw3395Se, "ATK U2 NK"),
        [(2, 83)] = (Sensor.Paw3395, "ATK A9 Plus NK"),
        [(2, 84)] = (Sensor.Paw3395Se, "ATK A9 NK"),
        [(2, 85)] = (Sensor.Paw3395Ultra, "ATK A9 Pro Max"),
        [(2, 87)] = (Sensor.Paw3950Ultra, "ATK A9 Ultra"),
        [(2, 88)] = (Sensor.Paw3311, "ATK A9 SE NK"),
        [(2, 89)] = (Sensor.Paw3395, "ATK X1 Lite"),
        [(2, 90)] = (Sensor.Paw3395Se, "ATK X1E"),
        [(2, 92)] = (Sensor.Paw3311, "ATK U2 SE NK"),
        [(2, 93)] = (Sensor.Paw3395, "DRAGONFLY F1 V2 Plus NK"),
        [(2, 94)] = (Sensor.Paw3395Se, "DRAGONFLY F1 V2 NK"),
        [(2, 95)] = (Sensor.Paw3311, "DRAGONFLY F1 V2 SE NK"),
        [(2, 96)] = (Sensor.Paw3395, "ATK Y9 Plus NK"),
        [(2, 97)] = (Sensor.Paw3395Se, "ATK Y9 NK"),
        [(2, 98)] = (Sensor.Paw3311, "ATK Y9 SE NK"),
        [(2, 99)] = (Sensor.Paw3395Se, "ATK A9"),
        [(2, 100)] = (Sensor.Paw3395Ultra, "ATK U2 Pro Max"),
        [(2, 104)] = (Sensor.Paw3395, "ATK Dragonfly F1 PRO"),
        [(2, 105)] = (Sensor.Paw3395, "ATK Dragonfly F1 PRO MAX"),
        [(2, 106)] = (Sensor.Paw3395, "ATK Dragonfly F1 MOBA"),
        [(2, 107)] = (Sensor.Paw3395Se, "ATK A9 X"),
        [(2, 108)] = (Sensor.Paw3950, "ATK Y9 PRO MAX"),
        [(2, 109)] = (Sensor.Paw3395Se, "ATK U2"),
        [(2, 110)] = (Sensor.Paw3395Ultra, "DRAGONFLY F1 V2 PRO MAX"),
        [(2, 114)] = (Sensor.Paw3395, "VXE R1 UlTRA"),
        [(2, 115)] = (Sensor.Paw3950Ultra, "ATK A9 NB ULTRA"),
        [(2, 116)] = (Sensor.Paw3395Se, "VXE R1SE+"),
        [(2, 117)] = (Sensor.Paw3311, "VXE R1 S NK"),
        [(2, 120)] = (Sensor.Paw3395Se, "ATK A9 NB"),
        [(2, 121)] = (Sensor.Paw3315, "VXE X3"),
        [(2, 122)] = (Sensor.Paw3395Se, "VXE X3 Pro"),
        [(2, 123)] = (Sensor.Paw3395Se, "VXE X3 Turbo"),
        [(2, 124)] = (Sensor.Paw3950Ultra, "ATK U2 Plus NK"),
        [(2, 125)] = (Sensor.Paw3950Ultra, "VXE R1 PRO"),
        [(2, 126)] = (Sensor.Paw3950Ultra, "VXE R1 PRO MAX"),
        [(2, 127)] = (Sensor.Paw3950Ultra, "VXE R1"),
        [(2, 128)] = (Sensor.Paw3950Ultra, "ATK U2 Pro Max"),
        [(2, 129)] = (Sensor.Paw3955Master, "ATK A9 NB ULTRA +"),
        [(2, 130)] = (Sensor.Paw3395Se, "VXE R1SE+ Silent"),
        [(2, 131)] = (Sensor.Paw3950Ultra, "Flow 7"),
        [(2, 132)] = (Sensor.Paw3950Ultra, "ATK X1 S NK"),
        [(2, 133)] = (Sensor.Paw3950Ultra, "MAD R"),
        [(2, 134)] = (Sensor.Paw3955Master, "Mouse"),
        [(2, 135)] = (Sensor.Paw3955Master, "Mouse"),
        [(2, 136)] = (Sensor.Core26K, "VXE R1SE+"),
        [(2, 137)] = (Sensor.Core26K, "VXE R1SE+ Silent"),
    };

    // O que o app precisa guardar para mandar comandos depois.
    public sealed record Target(int VendorId, int ProductId, Sensor? Sensor, IReadOnlyList<int> ReportRates);

    public sealed record State(Target Target, string? Name, bool MouseOff, string Battery, int? Dpi, List<int> DpiPresets, int? ReportRateHz);

    // Lê o mouse (ou o receptor dele). Retorna null se o aparelho não fala esse protocolo.
    public static State? Query(int vendorId, int productId)
    {
        using var channel = Channel.Open(vendorId, productId);
        if (channel == null)
            return null;

        // A apresentação manda 4 bytes quaisquer; a resposta traz o tipo de conexão no byte 11.
        var hello = channel.Request(CmdHello, 0, 8, [(byte)Random.Shared.Next(256), (byte)Random.Shared.Next(256), (byte)Random.Shared.Next(256), (byte)Random.Shared.Next(256)]);
        if (hello == null || !Connections.TryGetValue(hello[11], out var connection))
            return null;
        var rates = RateCodes.Keys.Where(hz => hz <= connection.MaxHz).Order().ToList();

        if (connection.Wireless && channel.Request(CmdMouseOnline) is not [_, _, _, _, _, 1, ..])
            return new State(new Target(vendorId, productId, null, rates), null, true, "—", null, [], null);

        var model = channel.Request(CmdModel) is byte[] m && Models.TryGetValue((m[5], m[6]), out var known) ? known : ((Sensor Sensor, string Name)?)null;
        var sensor = model?.Sensor;
        var target = new Target(vendorId, productId, sensor, rates);

        var block = channel.Read(AddrRateBlock, 10);
        int? hz = block != null && Valid(block[0], block[1]) ? RateCodes.FirstOrDefault(kv => kv.Value == block[0]).Key : null;

        int? dpi = null;
        var presets = new List<int>();
        if (sensor is Sensor s && DpiRange.TryGetValue(s, out var range) && CurrentStage(block) is int stage)
        {
            dpi = ReadStageDpi(channel, s, stage);
            if (dpi != null)
            {
                int[] common = [400, 800, 1200, 1600, 2400, 3200, 6400];
                presets = common.Where(d => d >= range.Min && d <= range.Max).ToList();
            }
        }

        var battery = connection.Wireless && channel.Request(CmdBattery) is byte[] b && b[5] <= 100 ? $"{b[5]}%" : "—";
        return new State(target, model?.Name, false, battery, dpi, presets, hz == 0 ? null : hz);
    }

    // Grava o DPI no estágio em uso. Retorna o DPI gravado (arredondado ao passo do sensor).
    public static int SetDpi(Target target, int dpi)
    {
        if (target.Sensor is not Sensor sensor || !DpiRange.TryGetValue(sensor, out var range))
            throw new InvalidOperationException(Lang.T("error.noDpi"));
        dpi = Normalize(sensor, Math.Clamp(dpi, range.Min, range.Max));

        using var channel = Open(target);
        var stage = CurrentStage(channel.Read(AddrRateBlock, 10))
            ?? throw new InvalidOperationException(Lang.T("error.noReply"));

        if (sensor == Sensor.Paw3955Master)
        {
            var address = AddrDpiStages3955 + stage * 6;
            channel.Write(address, Encode3955(dpi));
        }
        else
        {
            // Os estágios vão de 2 em 2 (8 bytes): lemos o par e mudamos só o nosso.
            var address = AddrDpiStages + stage / 2 * 8;
            var pair = channel.Read(address, 8) ?? throw new InvalidOperationException(Lang.T("error.noReply"));
            var (x, y, ex) = EncodeClassic(sensor, dpi);
            var at = stage % 2 * 4;
            pair[at] = x;
            pair[at + 1] = y;
            pair[at + 2] = ex;
            pair[at + 3] = Check(x + y + ex);
            channel.Write(address, pair);
        }
        return dpi;
    }

    public static void SetReportRate(Target target, int hz)
    {
        if (!target.ReportRates.Contains(hz) || !RateCodes.TryGetValue(hz, out var code))
            throw new ArgumentException(Lang.T("error.badRate", hz));

        // O Hz divide o bloco com outras configurações: lemos o bloco e mudamos só os 2 bytes do Hz.
        using var channel = Open(target);
        var block = channel.Read(AddrRateBlock, 10) ?? throw new InvalidOperationException(Lang.T("error.noReply"));
        block[0] = code;
        block[1] = Check(code);
        channel.Write(AddrRateBlock, block);
    }

    private static Channel Open(Target target) =>
        Channel.Open(target.VendorId, target.ProductId) ?? throw new InvalidOperationException(Lang.T("error.disconnected"));

    // Cada valor na memória vem seguido de um byte de conferência: 0x55 − valor.
    private static byte Check(int value) => (byte)(0x55 - value);
    private static bool Valid(byte value, byte check) => check == Check(value);

    // Bloco do Hz: [Hz, conf., nº de estágios, conf., estágio atual (começa em 0), conf., ...].
    private static int? CurrentStage(byte[]? block) =>
        block != null && Valid(block[2], block[3]) && Valid(block[4], block[5]) && block[2] is >= 1 and <= 8 && block[4] < block[2]
            ? block[4]
            : null;

    private static int? ReadStageDpi(Channel channel, Sensor sensor, int stage)
    {
        if (sensor == Sensor.Paw3955Master)
        {
            // [X (2 bytes), Y (2 bytes), dobro, conf.]: DPI = (X + 1), vezes 2 se o bit do dobro estiver ligado.
            var d = channel.Read(AddrDpiStages3955 + stage * 6, 6);
            if (d == null || d[5] != Check(d[0] + d[1] + d[2] + d[3] + d[4]))
                return null;
            return ((d[0] | (d[1] << 8)) + 1) * ((d[4] & 1) != 0 ? 2 : 1);
        }

        var pair = channel.Read(AddrDpiStages + stage / 2 * 8, 8);
        if (pair == null)
            return null;
        var at = stage % 2 * 4;
        var (x, ex) = (pair[at], pair[at + 2]);
        if (pair[at + 3] != Check(x + pair[at + 1] + ex))
            return null;
        return DecodeClassic(sensor, x, ex);
    }

    // DPI no formato de 4 bytes [X, Y, extra, conf.]. O byte extra guarda os 2 bits altos de X (bits 2-3)
    // e de Y (bits 6-7), e marcas que mudam a escala (bit 1/5: faixa alta, bit 0/4: dobro).
    private static (byte X, byte Y, byte Ex) EncodeClassic(Sensor sensor, int dpi)
    {
        int value, flags = 0;
        switch (sensor)
        {
            case Sensor.Paw3950Ultra:
                // Até 10000: de 10 em 10. Acima: 10050 + 50 por passo (e em dobro acima de 30000).
                if (dpi <= 10000)
                    value = dpi / 10 - 1;
                else
                {
                    value = ((dpi > 30000 ? dpi / 2 : dpi) - 10050) / 50;
                    flags = 0x22 | (dpi > 30000 ? 0x11 : 0);
                }
                break;
            case Sensor.Core26K:
                value = dpi / 50 - 1;
                break;
            default: // PAW3395, 3395 Ultra, 3950, 3950 DM: de 50 em 50, e de 100 em 100 acima de 30000
                value = dpi / (dpi <= 30000 ? 50 : 100) - 1;
                flags = dpi > 30000 ? 0x11 : 0;
                break;
        }
        var high = (value >> 8) & 3;
        return ((byte)value, (byte)value, (byte)((high << 6) | (high << 2) | flags));
    }

    private static int DecodeClassic(Sensor sensor, byte x, byte ex)
    {
        var value = x | (((ex >> 2) & 3) << 8);
        var dpi = sensor switch
        {
            Sensor.Paw3950Ultra => (ex & 2) != 0 ? value * 50 + 10050 : (value + 1) * 10,
            Sensor.Core26K => (value + 1) * ((ex & 2) != 0 ? 100 : 50),
            _ => (value + 1) * 50,
        };
        return (ex & 1) != 0 ? dpi * 2 : dpi;
    }

    // PAW3955: [X (2 bytes), Y (2 bytes), dobro, conf.]. Acima de 40000 vai a metade com a marca de dobro.
    private static byte[] Encode3955(int dpi)
    {
        var doubled = dpi >= 40000;
        var value = (doubled ? dpi / 2 : dpi) - 1;
        byte lo = (byte)value, hi = (byte)(value >> 8), flags = (byte)(doubled ? 0x11 : 0);
        return [lo, hi, lo, hi, flags, Check(lo + hi + lo + hi + flags)];
    }

    // Arredonda para um valor que o sensor aceita.
    private static int Normalize(Sensor sensor, int dpi)
    {
        int step = sensor switch
        {
            Sensor.Paw3955Master => dpi >= 40000 ? 2 : 1,
            Sensor.Paw3950Ultra => dpi <= 10000 ? 10 : dpi <= 30000 ? 50 : 100,
            Sensor.Core26K => 50,
            _ => dpi <= 30000 ? 50 : 100,
        };
        return Math.Max(step, (int)Math.Round(dpi / (double)step) * step);
    }

    // A interface de configuração: pacotes de 16 bytes com o report ID 8, nos dois sentidos.
    private sealed class Channel : IDisposable
    {
        private readonly HidStream _stream;
        private readonly int _outputLength;

        private Channel(HidStream stream, int outputLength)
        {
            _stream = stream;
            _outputLength = outputLength;
            _stream.ReadTimeout = 100;
        }

        public static Channel? Open(int vendorId, int productId)
        {
            foreach (var device in DeviceList.Local.GetHidDevices(vendorId, productId))
            {
                if (!IsConfigInterface(device) || !device.TryOpen(out HidStream stream))
                    continue;
                return new Channel(stream, Math.Max(17, device.GetMaxOutputReportLength()));
            }
            return null;
        }

        // A interface de configuração tem usage page 0xFF04 (ou 0xFF02 em alguns modelos), usage 2,
        // e aceita mensagens com o report ID 8.
        private static bool IsConfigInterface(HidDevice d)
        {
            try
            {
                var descriptor = d.GetReportDescriptor();
                return descriptor.OutputReports.Any(r => r.ReportID == ReportId) &&
                       descriptor.DeviceItems.Any(item => item.Usages.GetAllValues().Any(u => u is 0xFF040002u or 0xFF020002u));
            }
            catch
            {
                return false;
            }
        }

        public byte[]? Read(int address, byte length) => Request(CmdRead, address, length);

        public void Write(int address, byte[] data)
        {
            _ = Request(CmdWrite, address, (byte)data.Length, data) ?? throw new InvalidOperationException(Lang.T("error.noReply"));
        }

        // Para ler da memória, devolve só os dados; nos outros comandos, a resposta inteira (16 bytes).
        public byte[]? Request(byte command, int address = 0, byte length = 0, byte[]? data = null)
        {
            // [comando, status, endereço (2), tamanho, 10 bytes de dados, checksum]
            var packet = new byte[16];
            packet[0] = command;
            packet[2] = (byte)(address >> 8);
            packet[3] = (byte)address;
            packet[4] = length;
            data?.CopyTo(packet, 5);
            // checksum = 0x55 − (report ID + soma dos 15 bytes)
            packet[15] = (byte)(0x55 - (ReportId + packet.Take(15).Sum(b => b)));

            var buffer = new byte[_outputLength];
            buffer[0] = ReportId;
            packet.CopyTo(buffer, 1);

            // O driver oficial tenta até 3 vezes, esperando meio segundo pela resposta.
            for (int attempt = 0; attempt < 3; attempt++)
            {
                _stream.Write(buffer);
                if (WaitReply(command) is byte[] reply)
                    return command == CmdRead ? reply[5..(5 + length)] : reply;
            }
            return null;
        }

        private byte[]? WaitReply(byte command)
        {
            var input = new byte[Math.Max(17, _stream.Device.GetMaxInputReportLength())];
            var deadline = DateTime.UtcNow.AddMilliseconds(500);
            while (DateTime.UtcNow < deadline)
            {
                int read;
                try { read = _stream.Read(input); }
                catch (TimeoutException) { continue; }
                if (read < 17)
                    continue;
                // [0] é o report ID. Como o driver oficial, reconhecemos a resposta pelo comando.
                var reply = input[1..17];
                if (reply[0] == command)
                    return reply;
            }
            return null;
        }

        public void Dispose() => _stream.Dispose();
    }
}
