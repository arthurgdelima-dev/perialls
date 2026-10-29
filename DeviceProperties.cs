using System.Runtime.InteropServices;
using System.Text;

namespace Perialls;

// Lê propriedades extras de um aparelho direto do Windows (cfgmgr32.dll),
// as mesmas que aparecem na aba "Detalhes" do Gerenciador de Dispositivos.
public static class DeviceProperties
{
    // "Contêiner": o Windows dá o mesmo ID para todas as partes do mesmo aparelho físico.
    public static readonly PropertyKey ContainerId = new("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c", 2);

    // Nome que o próprio aparelho informa pelo USB (ex.: "USB Receiver", "MYSTIC LIGHT").
    public static readonly PropertyKey BusReportedName = new("540b947e-8b40-45bc-a8a2-6a0b894cbda2", 4);

    // Bateria (0 a 100) que o Windows lê de aparelhos Bluetooth que informam isso.
    public static readonly PropertyKey BluetoothBattery = new("104ea319-6ee2-4701-bd47-8ddbf425bbe5", 2);

    // Contêiner especial do Windows para coisas "de dentro do PC" (sem aparelho físico separado).
    public static readonly Guid InternalContainer = new("00000000-0000-0000-ffff-ffffffffffff");

    public static string? GetString(string deviceId, PropertyKey key)
    {
        var data = Read(deviceId, key);
        return data == null ? null : Encoding.Unicode.GetString(data).TrimEnd('\0');
    }

    public static Guid? GetGuid(string deviceId, PropertyKey key)
    {
        var data = Read(deviceId, key);
        return data is { Length: 16 } ? new Guid(data) : null;
    }

    public static byte? GetByte(string deviceId, PropertyKey key)
    {
        var data = Read(deviceId, key);
        return data is { Length: > 0 } ? data[0] : null;
    }

    private static byte[]? Read(string deviceId, PropertyKey key)
    {
        if (CM_Locate_DevNodeW(out uint devInst, deviceId, 0) != 0)
            return null;

        // Primeira chamada descobre o tamanho; a segunda lê os dados.
        uint size = 0;
        CM_Get_DevNode_PropertyW(devInst, ref key, out _, null, ref size, 0);
        if (size == 0)
            return null;

        var buffer = new byte[size];
        return CM_Get_DevNode_PropertyW(devInst, ref key, out _, buffer, ref size, 0) == 0 ? buffer : null;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PropertyKey
    {
        public Guid Category;
        public uint Id;

        public PropertyKey(string category, uint id)
        {
            Category = new Guid(category);
            Id = id;
        }
    }

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Locate_DevNodeW(out uint devInst, string deviceId, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_DevNode_PropertyW(uint devInst, ref PropertyKey key, out uint type,
        byte[]? buffer, ref uint size, uint flags);
}
