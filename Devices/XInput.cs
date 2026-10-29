using System.Runtime.InteropServices;

namespace Perialls;

// XInput é a API do Windows para controles no padrão Xbox.
// Funciona com qualquer controle que se apresente como "controle de Xbox" (como o EasySMX).
public static class XInput
{
    public const int MaxControllers = 4;

    public static bool IsConnected(int slot) => XInputGetState(slot, out _) == 0;

    public static string GetBattery(int slot)
    {
        if (XInputGetBatteryInformation(slot, 0, out var info) != 0)
            return "—";

        return info.Type switch
        {
            0 => "—",                    // desconectado
            1 => "Com fio",              // ou um receptor que se apresenta como "com fio"
            0xFF => "Não informada",
            _ => info.Level switch
            {
                0 => "Vazia",
                1 => "Baixa",
                2 => "Média",
                _ => "Cheia",
            },
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct State
    {
        public uint PacketNumber;
        public ushort Buttons;
        public byte LeftTrigger, RightTrigger;
        public short LeftX, LeftY, RightX, RightY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BatteryInformation
    {
        public byte Type;
        public byte Level;
    }

    [DllImport("xinput1_4.dll")]
    private static extern int XInputGetState(int slot, out State state);

    [DllImport("xinput1_4.dll")]
    private static extern int XInputGetBatteryInformation(int slot, byte deviceType, out BatteryInformation info);
}
