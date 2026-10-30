using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace ResetExplode.Linux.Games;

/// <summary>Um detector por launcher. Todos só leem arquivos; nenhum abre rede nem processo.</summary>
public static class SteamSource
{
    private static readonly Regex Tools = new(@"^(Steam Linux Runtime|Proton|Steamworks Common|Steam Controller)", RegexOptions.IgnoreCase);

    public static List<string> Roots() =>
        GameFolders.Existing(".local/share/Steam", ".steam/steam", ".var/app/com.valvesoftware.Steam/.local/share/Steam")
            .GroupBy(p => Path.GetFullPath(new DirectoryInfo(p).ResolveLinkTarget(true)?.FullName ?? p)).Select(g => g.First()).ToList();

    public static List<GameEntry> Scan()
    {
        var res = new List<GameEntry>();
        var seen = new HashSet<string>();
        foreach (var root in Roots())
        {
            bool flatpak = root.Contains("/.var/app/com.valvesoftware.Steam");
            var libs = new List<string> { root };
            try
            {
                var vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
                if (File.Exists(vdf) && Vdf.Parse(File.ReadAllText(vdf)).Child("libraryfolders") is { } lf)
                    foreach (var (_, c) in lf.Children)
                        if (c.Get("path") is { } p && Directory.Exists(p) && !libs.Contains(p)) libs.Add(p);
            }
            catch { }

            foreach (var lib in libs)
            {
                string apps = Path.Combine(lib, "steamapps");
                if (!Directory.Exists(apps)) continue;
                foreach (var acf in Directory.GetFiles(apps, "appmanifest_*.acf"))
                {
                    try
                    {
                        var s = Vdf.Parse(File.ReadAllText(acf)).Child("AppState");
                        string? id = s?.Get("appid"), name = s?.Get("name"), dir = s?.Get("installdir");
                        if (id == null || name == null || Tools.IsMatch(name) || !seen.Add(id)) continue;
                        // StateFlags 4 = totalmente instalado
                        if (s!.Get("StateFlags") is { } sf && (long.Parse(sf) & 4) == 0) continue;
                        res.Add(new GameEntry
                        {
                            Store = "Steam", Id = id, Name = name,
                            InstallDir = Path.Combine(apps, "common", dir ?? ""),
                            CoverPath = LocalCover(root, id),
                            CoverUrl = $"https://cdn.cloudflare.steamstatic.com/steam/apps/{id}/library_600x900.jpg",
                            LaunchKind = "steam", LaunchTarget = id, FlatpakLauncher = flatpak,
                        });
                    }
                    catch { }
                }
            }
        }
        return res;
    }

    /// <summary>
    /// Melhor capa vertical do cache da Steam: 600x900, depois a cápsula vertical (300x450) e, só em último caso,
    /// o cabeçalho horizontal. Nunca um .jpg qualquer da pasta (há ícones de 32x32 ali).
    /// </summary>
    private static string? LocalCover(string root, string appId)
    {
        string d = Path.Combine(root, "appcache", "librarycache", appId);
        if (!Directory.Exists(d)) return null;
        foreach (var n in new[] { "library_600x900.jpg", "library_capsule.jpg" })
            if (File.Exists(Path.Combine(d, n))) return Path.Combine(d, n);
        // versões novas guardam em subpastas com hash
        var nested = Directory.GetFiles(d, "library_600x900*.jpg", SearchOption.AllDirectories).FirstOrDefault()
                  ?? Directory.GetFiles(d, "library_capsule*.jpg", SearchOption.AllDirectories).FirstOrDefault();
        if (nested != null) return nested;
        foreach (var n in new[] { "library_header.jpg", "header.jpg" })
            if (File.Exists(Path.Combine(d, n))) return Path.Combine(d, n);
        return null;
    }
}

public static class HeroicSource
{
    public static List<(string Dir, bool Flatpak)> Roots()
    {
        var l = new List<(string, bool)>();
        string h = GameFolders.Home;
        if (Directory.Exists(Path.Combine(h, ".config/heroic"))) l.Add((Path.Combine(h, ".config/heroic"), false));
        if (Directory.Exists(Path.Combine(h, ".var/app/com.heroicgameslauncher.hgl/config/heroic"))) l.Add((Path.Combine(h, ".var/app/com.heroicgameslauncher.hgl/config/heroic"), true));
        return l;
    }

    private static JsonDocument? Load(string path)
    {
        try { return File.Exists(path) ? JsonDocument.Parse(File.ReadAllText(path)) : null; } catch { return null; }
    }

    private static string? Str(JsonElement e, string k) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public static List<GameEntry> Scan()
    {
        var res = new List<GameEntry>();
        foreach (var (root, flatpak) in Roots())
        {
            // Epic (legendary): { "<app_name>": { title, install_path, executable } }
            using (var d = Load(Path.Combine(root, "legendaryConfig/legendary/installed.json")))
                if (d?.RootElement.ValueKind == JsonValueKind.Object)
                    foreach (var p in d.RootElement.EnumerateObject())
                        res.Add(new GameEntry
                        {
                            Store = "Epic", Id = p.Name, Name = Str(p.Value, "title") ?? p.Name,
                            InstallDir = Str(p.Value, "install_path") ?? "",
                            CoverUrl = EpicCover(root, p.Name),
                            LaunchKind = "heroic", LaunchTarget = $"legendary/{p.Name}", FlatpakLauncher = flatpak,
                        });

            // GOG: { "installed": [ { appName, install_path } ] } + títulos em store_cache/gog_library.json
            var titles = new Dictionary<string, (string Title, string? Cover)>();
            using (var lib = Load(Path.Combine(root, "store_cache/gog_library.json")))
                if (lib?.RootElement.TryGetProperty("games", out var gs) == true && gs.ValueKind == JsonValueKind.Array)
                    foreach (var g in gs.EnumerateArray())
                        if (Str(g, "app_name") is { } an) titles[an] = (Str(g, "title") ?? an, Str(g, "art_square") ?? Str(g, "art_cover"));
            using (var d = Load(Path.Combine(root, "gog_store/installed.json")))
                if (d?.RootElement.TryGetProperty("installed", out var inst) == true && inst.ValueKind == JsonValueKind.Array)
                    foreach (var g in inst.EnumerateArray())
                        if (Str(g, "appName") is { } an)
                        {
                            titles.TryGetValue(an, out var t);
                            res.Add(new GameEntry
                            {
                                Store = "GOG", Id = an, Name = t.Title ?? an, InstallDir = Str(g, "install_path") ?? "", CoverUrl = t.Cover,
                                LaunchKind = "heroic", LaunchTarget = $"gog/{an}", FlatpakLauncher = flatpak,
                            });
                        }

            // Jogos adicionados à mão no Heroic: { "games": [ { app_name, title, art_cover, folder_name } ] }
            using (var d = Load(Path.Combine(root, "sideload_apps/library.json")))
                if (d?.RootElement.TryGetProperty("games", out var gs) == true && gs.ValueKind == JsonValueKind.Array)
                    foreach (var g in gs.EnumerateArray())
                        if (Str(g, "app_name") is { } an)
                            res.Add(new GameEntry
                            {
                                Store = "Heroic", Id = an, Name = Str(g, "title") ?? an, InstallDir = Str(g, "folder_name") ?? "",
                                CoverUrl = Str(g, "art_cover") ?? Str(g, "art_square"),
                                LaunchKind = "heroic", LaunchTarget = $"sideload/{an}", FlatpakLauncher = flatpak,
                            });
        }
        return res;
    }

    /// <summary>Capa vertical da Epic (DieselGameBoxTall) dos metadados do legendary.</summary>
    private static string? EpicCover(string root, string app)
    {
        using var d = Load(Path.Combine(root, "legendaryConfig/legendary/metadata", app + ".json"));
        if (d?.RootElement.TryGetProperty("metadata", out var m) == true && m.TryGetProperty("keyImages", out var ki) && ki.ValueKind == JsonValueKind.Array)
        {
            foreach (var want in new[] { "DieselGameBoxTall", "Thumbnail" })
                foreach (var i in ki.EnumerateArray())
                    if (Str(i, "type") == want) return Str(i, "url");
        }
        return null;
    }
}

public static class LutrisSource
{
    public static List<GameEntry> Scan()
    {
        var res = new List<GameEntry>();
        string data = Path.Combine(GameFolders.Home, ".local/share/lutris");
        string db = Path.Combine(data, "pga.db");
        if (!File.Exists(db)) return res;
        try
        {
            using var c = new SqliteConnection($"Data Source={db};Mode=ReadOnly");
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "select id, name, slug, directory from games where installed = 1";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                string id = r.GetInt64(0).ToString(), name = r.GetString(1), slug = r.IsDBNull(2) ? "" : r.GetString(2);
                string? cover = new[] { ".jpg", ".png" }.Select(e => Path.Combine(data, "coverart", slug + e)).FirstOrDefault(File.Exists);
                res.Add(new GameEntry
                {
                    Store = "Lutris", Id = id, Name = name, InstallDir = r.IsDBNull(3) ? "" : r.GetString(3),
                    CoverPath = cover, LaunchKind = "lutris", LaunchTarget = id,
                });
            }
        }
        catch { }
        return res;
    }
}

/// <summary>Pastas que o usuário vincula: cada subpasta vira um jogo; o executável é escolhido por heurística.</summary>
public static class FolderSource
{
    public static List<GameEntry> Scan(IEnumerable<string> folders)
    {
        var res = new List<GameEntry>();
        foreach (var f in folders.Where(Directory.Exists))
            foreach (var dir in Directory.GetDirectories(f).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                string name = Path.GetFileName(dir);
                if (name.StartsWith('.')) continue;
                var (exe, windows) = FindExecutable(dir, name);
                if (exe == null) continue;
                string? cover = new[] { "cover.jpg", "cover.png", "folder.jpg", "poster.jpg" }.Select(n => Path.Combine(dir, n)).FirstOrDefault(File.Exists);
                res.Add(new GameEntry
                {
                    Store = "Pasta", Id = dir, Name = name, InstallDir = dir, CoverPath = cover,
                    LaunchKind = windows ? "none" : "exec", LaunchTarget = exe,
                    Note = windows ? "Jogo de Windows: adicione ao Steam, Heroic ou Lutris para rodar com Proton." : null,
                });
            }
        return res;
    }

    private static bool IsElf(string path)
    {
        try { using var s = File.OpenRead(path); Span<byte> b = stackalloc byte[4]; return s.Read(b) == 4 && b[0] == 0x7F && b[1] == (byte)'E' && b[2] == (byte)'L' && b[3] == (byte)'F'; }
        catch { return false; }
    }

    internal static (string? Exe, bool Windows) FindExecutable(string dir, string name)
    {
        var opt = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 2, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        var files = Directory.GetFiles(dir, "*", opt);
        string norm(string s) => new string(s.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        string n = norm(name);
        var scripts = files.Where(f => Path.GetExtension(f) is ".sh" or ".x86_64" or ".AppImage").ToList();
        var pick = scripts.FirstOrDefault(f => Path.GetFileName(f).Equals("start.sh", StringComparison.OrdinalIgnoreCase))
                ?? scripts.FirstOrDefault(f => norm(Path.GetFileNameWithoutExtension(f)).Contains(n) || n.Contains(norm(Path.GetFileNameWithoutExtension(f))))
                ?? scripts.FirstOrDefault();
        if (pick != null) return (pick, false);
        var elf = files.Where(f => Path.GetExtension(f) == "" && IsElf(f)).ToList();
        pick = elf.FirstOrDefault(f => norm(Path.GetFileName(f)).Contains(n) || n.Contains(norm(Path.GetFileName(f)))) ?? elf.FirstOrDefault();
        if (pick != null) return (pick, false);
        var exes = files.Where(f => Path.GetExtension(f).Equals(".exe", StringComparison.OrdinalIgnoreCase)
                && !Regex.IsMatch(Path.GetFileName(f), "unins|setup|redist|crash|vcredist|dxsetup", RegexOptions.IgnoreCase)).ToList();
        pick = exes.FirstOrDefault(f => norm(Path.GetFileNameWithoutExtension(f)).Contains(n) || n.Contains(norm(Path.GetFileNameWithoutExtension(f)))) ?? exes.FirstOrDefault();
        return pick != null ? (pick, true) : (null, false);
    }
}

public static class Library
{
    public static List<GameEntry> Scan(LinuxSettings s)
    {
        var all = new List<GameEntry>();
        all.AddRange(SteamSource.Scan());
        all.AddRange(HeroicSource.Scan());
        all.AddRange(LutrisSource.Scan());
        all.AddRange(FolderSource.Scan(s.GameFolders));
        return all.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }
}
