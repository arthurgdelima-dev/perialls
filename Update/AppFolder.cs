using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace Perialls;

// A pasta onde o Perialls está: o executável e a pasta "openrgb" ao lado dele.
// A atualização só mexe nesses dois e numa pasta de trabalho (.perialls-update).
// Configurações, perfis, drivers baixados e fotos ficam em outro lugar (AppData) e nunca são tocados.
//
// Como a troca funciona (igual no Windows e no Linux):
//   1. O executável atual vira "Perialls.exe.old" (o Windows deixa renomear um .exe aberto, só não sobrescrever).
//      Se a pasta openrgb mudou, ela vira "openrgb.old".
//   2. Os novos vão para o lugar. Se algo falhar no meio, os .old voltam.
//   3. A versão antiga abre a nova e espera ela avisar que abriu (arquivo "ok" na pasta de trabalho).
//      Se a nova fechar ou não avisar a tempo, a antiga encerra a nova, restaura os .old e continua aberta.
//   4. A nova, depois que a antiga fecha, apaga os .old e a pasta de trabalho.
public sealed class AppFolder(string exe)
{
    public static readonly AppFolder? Current = Updater.Supported ? new AppFolder(Environment.ProcessPath!) : null;

    // A versão antiga passa isto para a nova: "--updated-from <pid da antiga>".
    private const string UpdatedArg = "--updated-from";

    // Quanto tempo a versão antiga espera a nova abrir.
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(60);

    public string Exe { get; } = exe;
    public string Dir { get; } = Path.GetDirectoryName(exe)!;

    // Onde o pacote é baixado e extraído. Fica na mesma pasta do app para a troca ser só renomear.
    public string Work => Path.Combine(Dir, ".perialls-update");
    private string OkFile => Path.Combine(Work, "ok");
    private string OldExe => Exe + ".old";
    private string OpenRgbDir => Path.Combine(Dir, "openrgb");
    private string OldOpenRgb => OpenRgbDir + ".old";

    // Aberto pela versão antiga logo depois de uma troca: falta confirmar que abriu.
    private bool _justUpdated;
    private int? _oldPid;

    // O que contar no rodapé ao abrir (chave do texto), uma vez só.
    public string? StartupMessage { get; private set; }

    public string? TakeStartupMessage()
    {
        var message = StartupMessage;
        StartupMessage = null;
        return message;
    }

    // Cria a pasta de trabalho vazia. false = sem permissão de escrita na pasta do app.
    public bool TryPrepare()
    {
        try
        {
            DeleteWork();
            Directory.CreateDirectory(Work);
            File.WriteAllText(Path.Combine(Work, "teste"), "");
            File.Delete(Path.Combine(Work, "teste"));
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    // Extrai o pacote e troca os arquivos. Se der errado no meio, volta a versão anterior e avisa.
    public void Install(string archive)
    {
        var extracted = Path.Combine(Work, "novo");
        Extract(archive, extracted);

        var root = FindRoot(extracted);
        var newExe = Path.Combine(root, OperatingSystem.IsWindows() ? "Perialls.exe" : "Perialls");
        if (!File.Exists(newExe))
            throw new UpdateException(Lang.T("update.badPackage"));
        var newOpenRgb = Path.Combine(root, "openrgb");
        var swapOpenRgb = Directory.Exists(newOpenRgb) && !SameContent(newOpenRgb, OpenRgbDir);

        // Linux: garante a permissão de execução (o .tar.gz já traz, mas não custa).
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(newExe, File.GetUnixFileMode(newExe)
                | UnixFileMode.UserRead | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);

        // Sobras de uma atualização anterior. Se não der para apagar, nem começa.
        Retry(() => DeleteFile(OldExe));
        Retry(() => DeleteTree(OldOpenRgb));

        // O executável é um arquivo só, e o .NET reabre esse arquivo (pelo nome) para carregar cada parte
        // do programa na primeira vez que ela é usada. Depois de renomeado, isso falharia: então tudo é
        // carregado agora, enquanto o nome ainda é o original.
        LoadAllAssemblies();

        try
        {
            Retry(() => File.Move(Exe, OldExe));
            Retry(() => File.Move(newExe, Exe));
            if (swapOpenRgb)
            {
                if (Directory.Exists(OpenRgbDir))
                    Retry(() => Directory.Move(OpenRgbDir, OldOpenRgb));
                Retry(() => Directory.Move(newOpenRgb, OpenRgbDir));
            }
        }
        catch (Exception ex)
        {
            Rollback();
            throw new UpdateException(Lang.T("update.swapFailed", ex.Message));
        }
        DeleteTree(extracted);
    }

    // Abre a versão nova e espera ela confirmar. true = abriu (esta pode fechar).
    // false = não abriu: a nova foi encerrada e os .old voltaram para o lugar.
    public async Task<bool> StartNewAndWaitAsync()
    {
        DeleteFile(OkFile);
        Process? process = null;
        try
        {
            var info = new ProcessStartInfo(Exe) { UseShellExecute = false, WorkingDirectory = Dir };
            info.ArgumentList.Add(UpdatedArg);
            info.ArgumentList.Add(Environment.ProcessId.ToString());
            process = Process.Start(info);

            var clock = Stopwatch.StartNew();
            while (clock.Elapsed < StartTimeout && !File.Exists(OkFile) && process is { HasExited: false })
                await Task.Delay(250);
            if (File.Exists(OkFile))
                return true;
        }
        catch
        {
            // Não conseguiu nem abrir: volta a anterior abaixo.
        }

        try
        {
            if (process is { HasExited: false })
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token);
            }
        }
        catch
        {
            // Já tinha fechado.
        }
        await Task.Run(Rollback);
        DeleteWork();
        return false;
    }

    // Põe de volta o que estiver como .old (executável e openrgb).
    private void Rollback()
    {
        try
        {
            if (File.Exists(OldExe))
            {
                Retry(() => DeleteFile(Exe));
                Retry(() => File.Move(OldExe, Exe));
            }
            if (Directory.Exists(OldOpenRgb))
            {
                Retry(() => DeleteTree(OpenRgbDir));
                Retry(() => Directory.Move(OldOpenRgb, OpenRgbDir));
            }
        }
        catch (Exception ex)
        {
            throw new UpdateException(Lang.T("update.rollbackFailed", ex.Message));
        }
    }

    // Chamado em Program.Main, antes de abrir a janela.
    public void OnStartup(string[] args)
    {
        try
        {
            // Alguém abriu o "Perialls.exe.old": a versão nova não funcionou. Volta para esta.
            if (Exe.EndsWith(".old", StringComparison.OrdinalIgnoreCase))
            {
                RestoreFromOld();
                return;
            }

            // Aberto pela versão antiga logo depois da troca: confirma quando a janela abrir.
            var i = Array.IndexOf(args, UpdatedArg);
            if (i >= 0)
            {
                _justUpdated = true;
                _oldPid = i + 1 < args.Length && int.TryParse(args[i + 1], out var pid) ? pid : null;
                return;
            }

            // Abertura normal: se uma troca foi interrompida sem a pasta openrgb, ela volta.
            if (!Directory.Exists(OpenRgbDir) && Directory.Exists(OldOpenRgb))
                Directory.Move(OldOpenRgb, OpenRgbDir);
            // E as sobras (.old e pasta de trabalho) são apagadas sem atrasar a abertura.
            Task.Run(Cleanup);
        }
        catch
        {
            // Nada aqui pode impedir o app de abrir.
        }
    }

    // A janela da versão nova abriu: avisa a antiga e, depois que ela fechar, apaga os .old.
    public void ConfirmStarted()
    {
        if (!_justUpdated)
            return;
        _justUpdated = false;
        try
        {
            Directory.CreateDirectory(Work);
            File.WriteAllText(OkFile, "");
        }
        catch
        {
            // Sem o aviso, a antiga volta para ela mesma: é o lado seguro.
        }
        StartupMessage = "update.done";

        var oldPid = _oldPid;
        Task.Run(async () =>
        {
            if (oldPid is { } pid)
            {
                try
                {
                    using var old = Process.GetProcessById(pid);
                    await old.WaitForExitAsync(new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token);
                }
                catch
                {
                    // Já fechou (ou demorou demais): segue.
                }
            }
            Cleanup();
        });
    }

    // Aberto pelo "Perialls.exe.old": a atualização não funcionou e a pessoa voltou para esta.
    public bool RunningFromOld { get; private set; }

    // Tira a nova e põe uma cópia desta no lugar. Copia em vez de renomear, porque este processo ainda
    // lê o próprio arquivo (ver LoadAllAssemblies). O .old é apagado na próxima abertura normal.
    private void RestoreFromOld()
    {
        RunningFromOld = true;
        var target = Exe[..^".old".Length];
        Retry(() => DeleteFile(target));
        Retry(() => File.Copy(Exe, target));
        if (Directory.Exists(OldOpenRgb))
        {
            Retry(() => DeleteTree(OpenRgbDir));
            Retry(() => Directory.Move(OldOpenRgb, OpenRgbDir));
        }
        DeleteWork();
        StartupMessage = "update.restored";
    }

    private void Cleanup()
    {
        try
        {
            DeleteFile(OldExe);
            DeleteTree(OldOpenRgb);
        }
        catch
        {
            // Fica para a próxima abertura.
        }
        DeleteWork();
    }

    public void DeleteWork()
    {
        try
        {
            DeleteTree(Work);
        }
        catch
        {
            // Fica para a próxima abertura.
        }
    }

    // Carrega todas as partes do programa que ele referencia (direta ou indiretamente).
    private static void LoadAllAssemblies()
    {
        var loaded = AppDomain.CurrentDomain.GetAssemblies();
        var seen = new HashSet<string>(loaded.Select(a => a.GetName().Name ?? ""));
        var pending = new Queue<Assembly>(loaded);
        while (pending.TryDequeue(out var assembly))
        {
            foreach (var reference in assembly.GetReferencedAssemblies())
            {
                if (!seen.Add(reference.Name ?? ""))
                    continue;
                try
                {
                    pending.Enqueue(Assembly.Load(reference));
                }
                catch
                {
                    // Referência que não existe neste sistema (ex.: só do Windows): não será usada.
                }
            }
        }
    }

    private static void Extract(string archive, string destination)
    {
        Directory.CreateDirectory(destination);
        if (archive.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            ZipFile.ExtractToDirectory(archive, destination);
            return;
        }
        // .tar.gz: guarda a permissão de execução e os links do OpenRGB.
        using var file = File.OpenRead(archive);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        TarFile.ExtractToDirectory(gzip, destination, overwriteFiles: false);
    }

    // O pacote traz tudo dentro de uma pasta (Perialls-win-x64/ ou Perialls-linux-x64/).
    private static string FindRoot(string extracted)
    {
        var dirs = Directory.GetDirectories(extracted);
        return Directory.GetFiles(extracted).Length == 0 && dirs.Length == 1 ? dirs[0] : extracted;
    }

    // As duas pastas têm os mesmos arquivos, com o mesmo conteúdo?
    private static bool SameContent(string a, string b) =>
        Directory.Exists(b) && Fingerprint(a) == Fingerprint(b);

    private static string Fingerprint(string root)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Add(string text) => hash.AppendData(Encoding.UTF8.GetBytes(text + "\n"));
        void Walk(DirectoryInfo dir)
        {
            foreach (var entry in dir.EnumerateFileSystemInfos().OrderBy(e => e.Name, StringComparer.Ordinal))
            {
                var name = Path.GetRelativePath(root, entry.FullName).Replace('\\', '/');
                if (entry.LinkTarget != null)
                    Add($"L {name} {entry.LinkTarget}");
                else if (entry is DirectoryInfo sub)
                {
                    Add($"D {name}");
                    Walk(sub);
                }
                else
                {
                    using var stream = File.OpenRead(entry.FullName);
                    Add($"F {name} {Convert.ToHexString(SHA256.HashData(stream))}");
                }
            }
        }
        Walk(new DirectoryInfo(root));
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void DeleteFile(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }

    // Apaga uma pasta inteira, mesmo com arquivos ou pastas sem permissão de escrita.
    private static void DeleteTree(string dir)
    {
        if (!Directory.Exists(dir))
            return;
        var all = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
        if (OperatingSystem.IsWindows())
        {
            foreach (var file in Directory.EnumerateFiles(dir, "*", all))
                File.SetAttributes(file, FileAttributes.Normal);
        }
        else
        {
            foreach (var sub in Directory.EnumerateDirectories(dir, "*", all).Prepend(dir))
                File.SetUnixFileMode(sub, File.GetUnixFileMode(sub) | UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        Directory.Delete(dir, recursive: true);
    }

    // Antivírus e o próprio Windows às vezes seguram um arquivo por um instante: tenta algumas vezes.
    private static void Retry(Action action)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (Exception ex) when (attempt < 5 && ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(300);
            }
        }
    }
}
