using System.Text.Json;

namespace ResetExplode.Linux;

/// <summary>Configurações do app em ~/.config/resetexplode/settings.json (a chave de API fica só legível pelo dono).</summary>
public sealed class LinuxSettings
{
    public List<string> GameFolders { get; set; } = new();
    public string? SteamGridDbKey { get; set; }

    private static string Dir => Path.Combine(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } x ? x : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"), "resetexplode");
    private static string FilePath => Path.Combine(Dir, "settings.json");

    /// <summary>A chave do SteamGridDB tem 32 caracteres hexadecimais. Tira o que vier junto ao colar ("key: ", espaços, aspas); null se não achar.</summary>
    public static string? CleanKey(string? raw)
    {
        var m = System.Text.RegularExpressions.Regex.Match(raw ?? "", "[0-9a-fA-F]{32}");
        return m.Success ? m.Value.ToLowerInvariant() : null;
    }

    public static LinuxSettings Load()
    {
        try
        {
            var s = JsonSerializer.Deserialize<LinuxSettings>(File.ReadAllText(FilePath)) ?? new();
            s.SteamGridDbKey = CleanKey(s.SteamGridDbKey);       // conserta chaves salvas com texto junto
            return s;
        }
        catch { return new(); }
    }

    public void Save()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        try { File.SetUnixFileMode(FilePath, UnixFileMode.UserRead | UnixFileMode.UserWrite); } catch { }
    }
}
