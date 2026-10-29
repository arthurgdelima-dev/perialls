using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Perialls;

// Fotos dos aparelhos, guardadas dentro do .exe (pasta Assets/Devices).
// O arquivo devices.json liga um aparelho ao nome do PNG. A chave pode ser:
//   - o código USB, ex.: "VID_046D&PID_4093" (o jeito certo, vale só para aquele modelo);
//   - um pedaço do nome, ex.: "High Definition Audio Device" (para aparelhos sem código USB, como fones P2).
// Para adicionar um aparelho: coloque o PNG na pasta, adicione uma linha no JSON e compile de novo.
public static class DevicePhotos
{
    private const string Folder = "pack://application:,,,/Assets/Devices/";
    private static readonly Dictionary<string, string> Map = LoadMap();
    private static readonly Dictionary<string, ImageSource?> Cache = [];

    public static ImageSource? Find(DeviceInfo device)
    {
        var file = device.HardwareIds.Select(id => Map.GetValueOrDefault(id)).FirstOrDefault(f => f != null)
            ?? Map.FirstOrDefault(kv => !kv.Key.StartsWith("VID_")
                && device.Name.Contains(kv.Key, StringComparison.OrdinalIgnoreCase)).Value;

        return file == null ? null : Load(file);
    }

    private static ImageSource? Load(string file)
    {
        if (Cache.TryGetValue(file, out var cached))
            return cached;

        ImageSource? image = null;
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(Folder + file);
            bitmap.DecodePixelHeight = 240; // não precisa carregar a foto em tamanho gigante
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            image = bitmap;
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
            var info = Application.GetResourceStream(new Uri(Folder + "devices.json"));
            if (info == null)
                return [];
            using var reader = new StreamReader(info.Stream);
            var map = JsonSerializer.Deserialize<Dictionary<string, string>>(reader.ReadToEnd()) ?? [];
            return new Dictionary<string, string>(map, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return [];
        }
    }
}
