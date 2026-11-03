namespace Perialls;

// A pasta onde o Perialls está: o executável e a pasta "openrgb" ao lado dele.
// A atualização só mexe nesses dois e numa pasta de trabalho (.perialls-update).
// Configurações, perfis, drivers baixados e fotos ficam em outro lugar (AppData) e nunca são tocados.
public sealed class AppFolder(string exe)
{
    public static readonly AppFolder? Current = Updater.Supported ? new AppFolder(Environment.ProcessPath!) : null;

    public string Exe { get; } = exe;
    public string Dir { get; } = Path.GetDirectoryName(exe)!;

    // Onde o pacote é baixado e extraído. Fica na mesma pasta do app para a troca ser só renomear.
    public string Work => Path.Combine(Dir, ".perialls-update");

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

    public void DeleteWork()
    {
        try
        {
            if (Directory.Exists(Work))
                Directory.Delete(Work, recursive: true);
        }
        catch
        {
            // Fica para a próxima abertura.
        }
    }
}
