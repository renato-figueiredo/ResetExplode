using System.Text.Json;

namespace ResetExplode.Linux;

/// <summary>Configurações do app em ~/.config/resetexplode/settings.json (a chave de API fica só legível pelo dono).</summary>
public sealed class LinuxSettings
{
    public List<string> GameFolders { get; set; } = new();
    public string? SteamGridDbKey { get; set; }

    private static string Dir => Path.Combine(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } x ? x : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"), "resetexplode");
    private static string FilePath => Path.Combine(Dir, "settings.json");

    public static LinuxSettings Load()
    {
        try { return JsonSerializer.Deserialize<LinuxSettings>(File.ReadAllText(FilePath)) ?? new(); } catch { return new(); }
    }

    public void Save()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        try { File.SetUnixFileMode(FilePath, UnixFileMode.UserRead | UnixFileMode.UserWrite); } catch { }
    }
}
