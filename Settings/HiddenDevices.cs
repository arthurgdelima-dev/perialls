using System.IO;

namespace Perialls;

// Lembra quais aparelhos você ocultou, num arquivo de texto simples (um aparelho por linha):
// %AppData%\Perialls\ocultos.txt
public static class HiddenDevices
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Perialls", "ocultos.txt");

    public static HashSet<string> Load()
    {
        try
        {
            return File.Exists(FilePath) ? File.ReadAllLines(FilePath).ToHashSet() : [];
        }
        catch
        {
            return [];
        }
    }

    public static void Save(HashSet<string> keys)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllLines(FilePath, keys.Order());
    }
}
