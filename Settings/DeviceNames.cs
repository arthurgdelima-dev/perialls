using System.IO;

namespace Perialls;

// Nomes que você deu aos aparelhos, num arquivo de texto simples (uma linha por aparelho, "código<TAB>nome"):
// %AppData%\Perialls\nomes.txt
public static class DeviceNames
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Perialls", "nomes.txt");

    public static Dictionary<string, string> Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return [];
            return File.ReadAllLines(FilePath)
                .Select(line => line.Split('\t', 2))
                .Where(parts => parts.Length == 2 && parts[1].Trim() != "")
                .GroupBy(parts => parts[0])
                .ToDictionary(g => g.Key, g => g.Last()[1].Trim());
        }
        catch
        {
            return [];
        }
    }

    public static void Save(Dictionary<string, string> names)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllLines(FilePath, names.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}\t{kv.Value}"));
    }
}
