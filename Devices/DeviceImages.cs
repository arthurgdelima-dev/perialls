using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace Perialls;

// Fotos dos aparelhos, 100% automáticas:
//   1) Cache local (por código USB): se já existe, usa, sem internet.
//   2) Senão, busca "marca modelo png" no Bing (e, se não der em nada, no DuckDuckGo), em segundo plano.
//   3) Só aceita imagem com fundo transparente, quase quadrada e de pelo menos 300 px. A primeira que
//      passar é centralizada em 512x512 e salva no cache. Se nada passar ou a busca falhar, fica o ícone
//      e tenta de novo no próximo Atualizar (uma tentativa por Atualizar).
// As imagens ficam só no PC: %LocalAppData%\Perialls\imagens (Linux: ~/.local/share/Perialls/imagens).
public static class DeviceImages
{
    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Perialls", "imagens");

    private const int Size = 512;
    private const int MinSide = 300;
    private const int MaxDownloadBytes = 8 * 1024 * 1024;
    private const int CandidatesPerSearch = 4;

    // Os buscadores respondem diferente (ou não respondem) sem um navegador "de verdade".
    private const string BrowserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:130.0) Gecko/20100101 Firefox/130.0";
    private static readonly HttpClient Http = CreateClient();

    // Nomes que o sistema dá quando não sabe o que o aparelho é: buscar isso só traria imagem errada.
    private static readonly Regex GenericName = new(
        @"^(HID|USB)\b|Input Device|Composite Device|USB Receiver|Wireless Receiver|XInput|Keyboard Device|Mouse Device",
        RegexOptions.IgnoreCase);

    // A foto já salva no cache, ou null. Só lê o disco.
    public static IImage? FromCache(DeviceInfo device)
    {
        var path = CachePath(device);
        try
        {
            return File.Exists(path) ? new Bitmap(path) : null;
        }
        catch
        {
            return null; // arquivo estragado: a busca baixa de novo
        }
    }

    // Procura uma foto na internet e salva no cache. Null se não achou (sem erro na tela).
    public static async Task<IImage?> SearchAsync(DeviceInfo device, CancellationToken cancel)
    {
        if (SearchTerm(device) is not { } term)
            return null;

        try
        {
            var png = await FindAsync(BingAsync, term, cancel) ?? await FindAsync(DuckDuckGoAsync, term, cancel);
            if (png == null)
                return null;

            Directory.CreateDirectory(Folder);
            var path = CachePath(device);
            await File.WriteAllBytesAsync(path, png, cancel);
            return new Bitmap(path);
        }
        catch
        {
            return null; // sem internet, bloqueio, página mudou...
        }
    }

    // Só vale buscar quem tem um nome de produto: aparelhos com código USB ou Bluetooth.
    // O nome usado é o mais específico que temos: o que o driver ou o protocolo informou (HID++, Akko,
    // OpenRGB) já está em OriginalName; um driver JSON pode dar o modelo exato.
    public static string? SearchTerm(DeviceInfo device)
    {
        if (device.HardwareIds.Count == 0 && !device.IsBluetooth)
            return null;

        var name = device.Driver is { } driver && device.HardwareIds.Select(id => driver.Models.GetValueOrDefault(id)).FirstOrDefault(m => m != null) is { } model
            ? $"{driver.Name} {model}"
            : device.OriginalName;
        name = Regex.Replace(name.Replace('/', ' '), @"\s+", " ").Trim();

        // Sem a marca na frente, os nomes genéricos sobram ("Logitech USB Receiver" → "USB Receiver").
        var withoutBrand = name.Contains(' ') ? name[(name.IndexOf(' ') + 1)..] : name;
        if (name.Length < 4 || GenericName.IsMatch(name) || GenericName.IsMatch(withoutBrand))
            return null;
        return name + " png";
    }

    // Tenta os primeiros resultados de um buscador, um por vez, até um passar nos filtros.
    private static async Task<byte[]?> FindAsync(Func<string, CancellationToken, Task<List<string>>> search, string term, CancellationToken cancel)
    {
        List<string> urls;
        try
        {
            urls = await search(term, cancel);
        }
        catch (Exception) when (!cancel.IsCancellationRequested)
        {
            return null;
        }

        foreach (var url in urls.Where(IsCandidate).Take(CandidatesPerSearch))
        {
            if (await DownloadAsync(url, cancel) is { } data && Prepare(data) is { } png)
                return png;
        }
        return null;
    }

    // Bing: a página de resultados de imagens já filtrada por fundo transparente e formato quadrado.
    // Cada resultado traz um JSON no atributo "m", com o endereço da imagem em "murl".
    private static async Task<List<string>> BingAsync(string term, CancellationToken cancel)
    {
        var url = "https://www.bing.com/images/async?first=0&count=20&adlt=strict&q=" + Uri.EscapeDataString(term)
            + "&qft=" + Uri.EscapeDataString("+filterui:photo-transparent+filterui:aspect-square");
        var html = await Http.GetStringAsync(url, cancel);
        return Regex.Matches(html, @"murl&quot;:&quot;(.*?)&quot;")
            .Select(m => WebUtility.HtmlDecode(m.Groups[1].Value))
            .ToList();
    }

    // DuckDuckGo: primeiro pega o "vqd" (um código da busca) na página e depois pede os resultados em JSON.
    // Ele já informa largura e altura, então dá para descartar antes de baixar.
    private static async Task<List<string>> DuckDuckGoAsync(string term, CancellationToken cancel)
    {
        var q = Uri.EscapeDataString(term);
        var page = await Http.GetStringAsync($"https://duckduckgo.com/?q={q}&iax=images&ia=images", cancel);
        var vqd = Regex.Match(page, @"vqd=""([^""]+)""").Groups[1].Value;
        if (vqd == "")
            return [];

        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"https://duckduckgo.com/i.js?l=us-en&o=json&q={q}&vqd={Uri.EscapeDataString(vqd)}&f=,,,,,&p=1");
        request.Headers.Referrer = new Uri("https://duckduckgo.com/");
        using var response = await Http.SendAsync(request, cancel);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancel));
        if (!doc.RootElement.TryGetProperty("results", out var results))
            return []; // bloqueio ("botnet_challenge") ou resposta diferente

        return results.EnumerateArray()
            .Where(r => r.TryGetProperty("width", out var w) && r.TryGetProperty("height", out var h)
                && Math.Min(w.GetInt32(), h.GetInt32()) >= MinSide && IsSquareish(w.GetInt32(), h.GetInt32()))
            .Select(r => r.GetProperty("image").GetString() ?? "")
            .ToList();
    }

    // JPG nunca tem fundo transparente: nem baixa.
    private static bool IsCandidate(string url) =>
        url.StartsWith("https://") && !Regex.IsMatch(url, @"\.(jpe?g|gif)(\?|$)", RegexOptions.IgnoreCase);

    private static async Task<byte[]?> DownloadAsync(string url, CancellationToken cancel)
    {
        try
        {
            using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancel);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxDownloadBytes)
                return null;
            var data = await response.Content.ReadAsByteArrayAsync(cancel);
            return data.Length <= MaxDownloadBytes ? data : null;
        }
        catch (Exception) when (!cancel.IsCancellationRequested)
        {
            return null;
        }
    }

    // Confere a imagem de verdade (não confia no buscador) e devolve um PNG de 512x512, ou null.
    private static byte[]? Prepare(byte[] data)
    {
        using var source = SKBitmap.Decode(data);
        if (source == null || Math.Min(source.Width, source.Height) < MinSide || !IsSquareish(source.Width, source.Height)
            || !HasTransparentBackground(source))
            return null;

        // Centraliza num quadrado transparente de 512x512, sem esticar.
        var scale = (float)Size / Math.Max(source.Width, source.Height);
        var (w, h) = (source.Width * scale, source.Height * scale);
        using var target = new SKBitmap(new SKImageInfo(Size, Size, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(target))
        {
            canvas.Clear(SKColors.Transparent);
            using var paint = new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true };
            canvas.DrawBitmap(source, SKRect.Create((Size - w) / 2, (Size - h) / 2, w, h), paint);
        }
        using var image = SKImage.FromBitmap(target);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }

    private static bool IsSquareish(int width, int height) =>
        Math.Abs(width - height) <= Math.Max(width, height) * 0.15;

    // Fundo transparente: quase toda a borda transparente e algum conteúdo de verdade no meio.
    private static bool HasTransparentBackground(SKBitmap bitmap)
    {
        int border = 0, clearBorder = 0;
        for (int i = 0; i < 64; i++)
        {
            int x = i * (bitmap.Width - 1) / 63, y = i * (bitmap.Height - 1) / 63;
            foreach (var (px, py) in new[] { (x, 0), (x, bitmap.Height - 1), (0, y), (bitmap.Width - 1, y) })
            {
                border++;
                if (bitmap.GetPixel(px, py).Alpha < 20)
                    clearBorder++;
            }
        }

        int inside = 0, solid = 0;
        for (int y = bitmap.Height / 4; y < bitmap.Height * 3 / 4; y += Math.Max(1, bitmap.Height / 40))
        {
            for (int x = bitmap.Width / 4; x < bitmap.Width * 3 / 4; x += Math.Max(1, bitmap.Width / 40))
            {
                inside++;
                if (bitmap.GetPixel(x, y).Alpha > 200)
                    solid++;
            }
        }

        return clearBorder >= border * 0.9 && solid >= inside * 0.1;
    }

    // Um arquivo por aparelho, pelo código USB (ou pelo nome, no Bluetooth sem código).
    private static string CachePath(DeviceInfo device)
    {
        var key = device.HardwareIds.FirstOrDefault() ?? device.OriginalName;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16].ToLowerInvariant();
        var readable = Regex.Replace(key, @"[^A-Za-z0-9]+", "-").Trim('-');
        return Path.Combine(Folder, $"{readable[..Math.Min(readable.Length, 40)]}-{hash}.png");
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(BrowserAgent);
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.8");
        return client;
    }
}
