using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Perialls;

// Um problema na atualização, com a mensagem pronta para mostrar.
public sealed class UpdateException(string message) : Exception(message);

// Uma versão nova publicada no GitHub, pronta para baixar.
public sealed record UpdateInfo(Version Version, string Tag, string Notes, string PageUrl, string AssetName,
    string AssetUrl, string SumsUrl);

// Atualização pelo próprio app. Ao abrir, faz UMA consulta à última Release do GitHub (em segundo plano).
// Se houver versão nova, a barra lateral mostra um botão. Nada é baixado sem a pessoa clicar.
public static class Updater
{
    private const string Repo = "arthurgdelima-dev/perialls";
    public const string ReleasesPage = $"https://github.com/{Repo}/releases/latest";

    // A versão deste app (vem da tag, no CI: -p:Version=1.3.0).
    public static readonly Version Current = Normalize(Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0));

    // O pacote deste sistema. Só existe para Windows e Linux de 64 bits (x64).
    private static readonly string? AssetName =
        RuntimeInformation.ProcessArchitecture != Architecture.X64 ? null
        : OperatingSystem.IsWindows() ? "Perialls-win-x64.zip"
        : OperatingSystem.IsLinux() ? "Perialls-linux-x64.tar.gz"
        : null;

    // Só o executável publicado (um arquivo só) se atualiza. Rodando pelo código-fonte (dotnet run), não.
    public static bool Supported =>
        AssetName != null && Environment.ProcessPath != null && string.IsNullOrEmpty(typeof(Updater).Assembly.Location);

    private static readonly HttpClient Http = CreateClient();

    // A consulta é feita uma vez só, mesmo se a janela for recriada (ao trocar o idioma).
    private static Task<UpdateInfo?>? _check;
    public static Task<UpdateInfo?> CheckOnceAsync() => _check ??= Supported ? CheckAsync() : Task.FromResult<UpdateInfo?>(null);

    // Sem internet, API fora do ar, limite de consultas, release sem o pacote: devolve null, sem aviso.
    private static async Task<UpdateInfo?> CheckAsync()
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repo}/releases/latest");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await Http.SendAsync(request, timeout.Token);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(timeout.Token);
            using var doc = JsonDocument.Parse(json);
            var release = doc.RootElement;

            // Versões de teste (pre-release) e rascunhos são ignorados.
            if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean())
                return null;

            var tag = release.GetProperty("tag_name").GetString() ?? "";
            if (ParseTag(tag) is not { } version || version <= Current)
                return null;

            string? assetUrl = null, sumsUrl = null;
            foreach (var asset in release.GetProperty("assets").EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString();
                var url = asset.GetProperty("browser_download_url").GetString();
                if (name == AssetName)
                    assetUrl = url;
                else if (name == "SHA256SUMS.txt")
                    sumsUrl = url;
            }
            if (assetUrl == null || sumsUrl == null)
                return null;

            var page = release.TryGetProperty("html_url", out var html) ? html.GetString() ?? ReleasesPage : ReleasesPage;
            var notes = release.TryGetProperty("body", out var body) ? CleanNotes(body.GetString()) : "";
            return new UpdateInfo(version, tag, notes, page, AssetName!, assetUrl, sumsUrl);
        }
        catch
        {
            return null;
        }
    }

    // Baixa o pacote para a pasta de trabalho e confere o hash com o SHA256SUMS.txt da mesma Release.
    // Se não bater, apaga o arquivo e avisa. Devolve o caminho do pacote.
    public static async Task<string> DownloadAsync(UpdateInfo info, AppFolder folder, Action<double> progress,
        CancellationToken cancel)
    {
        using var sumsTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        sumsTimeout.CancelAfter(TimeSpan.FromSeconds(30));
        var sums = await Http.GetStringAsync(info.SumsUrl, sumsTimeout.Token);
        var expected = FindHash(sums, info.AssetName) ?? throw new UpdateException(Lang.T("update.noHash"));

        var archive = Path.Combine(folder.Work, info.AssetName);
        using var response = await Http.GetAsync(info.AssetUrl, HttpCompletionOption.ResponseHeadersRead, cancel);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? 0;

        string actual;
        await using (var source = await response.Content.ReadAsStreamAsync(cancel))
        await using (var file = File.Create(archive))
        {
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[81920];
            long done = 0;
            while (true)
            {
                // Se a conexão parar de mandar dados por 30 segundos, desiste (em vez de esperar para sempre).
                using var stall = CancellationTokenSource.CreateLinkedTokenSource(cancel);
                stall.CancelAfter(TimeSpan.FromSeconds(30));
                var read = await source.ReadAsync(buffer, stall.Token);
                if (read == 0)
                    break;
                await file.WriteAsync(buffer.AsMemory(0, read), cancel);
                sha.AppendData(buffer, 0, read);
                done += read;
                if (total > 0)
                    progress((double)done / total);
            }
            actual = Convert.ToHexString(sha.GetHashAndReset());
        }

        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(archive);
            throw new UpdateException(Lang.T("update.hashMismatch"));
        }
        return archive;
    }

    // Linha do sha256sum: "<hash>  <nome>" (ou "<hash> *<nome>").
    private static string? FindHash(string sums, string name) =>
        sums.Split('\n')
            .Select(line => line.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(parts => parts.Length == 2 && parts[1].TrimStart('*') == name && parts[0].Length == 64)
            .Select(parts => parts[0])
            .FirstOrDefault();

    // "v1.10.0" → 1.10.0. Compara como números: 1.10.0 é maior que 1.9.0.
    // O que vier depois de "-" ou "+" (ex.: v2.0.0-beta) é ignorado.
    public static Version? ParseTag(string tag)
    {
        var text = tag.Trim().TrimStart('v', 'V');
        var cut = text.IndexOfAny(['-', '+']);
        if (cut >= 0)
            text = text[..cut];
        if (!text.Contains('.'))
            text += ".0";
        return Version.TryParse(text, out var v) ? Normalize(v) : null;
    }

    // 1.3 e 1.3.0.0 são a mesma versão (sem isso, o Version acha 1.3.0 menor que 1.3.0.0).
    private static Version Normalize(Version v) =>
        new(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));

    public static string Display(Version v) => v.Revision > 0 ? v.ToString(4) : v.ToString(3);

    // As notas vêm em Markdown. Aqui viram texto simples, curto o bastante para o painel.
    private static string CleanNotes(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return "";
        var text = new StringBuilder();
        foreach (var raw in markdown.Replace("\r", "").Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.StartsWith('#'))
                line = line.TrimStart('#').Trim();
            else if (line.TrimStart().StartsWith("* ") || line.TrimStart().StartsWith("- "))
                line = "• " + line.TrimStart()[2..];
            line = line.Replace("**", "");
            text.AppendLine(line);
        }
        var result = text.ToString().Trim();
        return result.Length > 2000 ? result[..2000] + "…" : result;
    }

    private static HttpClient CreateClient()
    {
        // Sem limite geral: o tempo de cada pedido é controlado onde ele é feito.
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"Perialls/{Display(Current)}");
        return client;
    }
}
