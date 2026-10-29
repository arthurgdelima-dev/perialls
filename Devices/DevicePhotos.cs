using System.IO;
using System.Text.Json;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Perialls;

// Fotos dos aparelhos, guardadas dentro do executável (pasta Assets/Devices).
// O arquivo devices.json liga um aparelho ao nome do PNG. A chave pode ser:
//   - o código USB, ex.: "VID_046D&PID_4093" (o jeito certo, vale só para aquele modelo);
//   - um pedaço do nome, ex.: "High Definition Audio Device" (para aparelhos sem código USB, como fones P2).
// Para adicionar um aparelho: coloque o PNG na pasta, adicione uma linha no JSON e compile de novo.
public static class DevicePhotos
{
    private const string Folder = "avares://Perialls/Assets/Devices/";
    private static readonly Dictionary<string, string> Map = LoadMap();
    private static readonly Dictionary<string, IImage?> Cache = [];

    public static IImage? Find(DeviceInfo device)
    {
        var file = device.HardwareIds.Select(id => Map.GetValueOrDefault(id)).FirstOrDefault(f => f != null)
            ?? Map.FirstOrDefault(kv => !kv.Key.StartsWith("VID_")
                && device.Name.Contains(kv.Key, StringComparison.OrdinalIgnoreCase)).Value;

        return file == null ? null : Load(file);
    }

    private static IImage? Load(string file)
    {
        if (Cache.TryGetValue(file, out var cached))
            return cached;

        IImage? image = null;
        try
        {
            using var stream = AssetLoader.Open(new Uri(Folder + file));
            image = Bitmap.DecodeToHeight(stream, 240); // não precisa carregar a foto em tamanho gigante
        }
        catch
        {
            // Foto listada no JSON mas que não existe: usamos o ícone no lugar.
        }
        return Cache[file] = image;
    }

    private static Dictionary<string, string> LoadMap()
    {
        try
        {
            using var reader = new StreamReader(AssetLoader.Open(new Uri(Folder + "devices.json")));
            var map = JsonSerializer.Deserialize<Dictionary<string, string>>(reader.ReadToEnd()) ?? [];
            return new Dictionary<string, string>(map, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return [];
        }
    }
}
