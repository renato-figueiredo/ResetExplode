namespace ResetExplode.Linux.Games;

/// <summary>Um jogo da biblioteca, independente de onde veio. Sem nada de interface: é o contrato entre detectores, capas e lançador.</summary>
public sealed class GameEntry
{
    public string Store { get; init; } = "";        // Steam | Epic | GOG | Heroic | Lutris | Pasta
    public string Id { get; init; } = "";           // appid, app_name, id do Lutris, caminho...
    public string Name { get; init; } = "";
    public string InstallDir { get; init; } = "";
    public string? CoverPath { get; init; }         // arquivo local já existente (cache do launcher)
    public string? CoverUrl { get; init; }          // imagem pública do próprio launcher (sem chave)
    public string LaunchKind { get; init; } = "";   // steam | heroic | lutris | exec | none
    public string LaunchTarget { get; init; } = ""; // appid, "runner/app", id, ou caminho do executável
    public bool FlatpakLauncher { get; init; }
    public string? Note { get; init; }              // por que não dá para abrir (ex.: .exe sem Proton)
    public string Key => $"{Store}:{Id}";
    public bool CanLaunch => LaunchKind is not ("" or "none");
}

public sealed class GameFolders
{
    public static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public static IEnumerable<string> Existing(params string[] rel) =>
        rel.Select(r => Path.Combine(Home, r)).Where(Directory.Exists).Select(p => Path.GetFullPath(p)).Distinct();
}
