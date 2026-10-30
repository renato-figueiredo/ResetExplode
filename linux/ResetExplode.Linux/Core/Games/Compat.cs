using System.Text.Json;

namespace ResetExplode.Linux.Games;

/// <summary>Compatibilidade de um jogo da Steam no Linux: nota da comunidade (ProtonDB) e selo oficial (Steam Deck).</summary>
public sealed record CompatInfo(string? Tier, int Reports, string? Deck)
{
    public string Label => Tier == null ? "" : Tier.ToUpperInvariant();
}

/// <summary>
/// Usa dois endpoints públicos, sem chave: ProtonDB (não é uma API oficial e pode mudar sem aviso) e o relatório
/// de compatibilidade da loja da Steam. Respostas ficam em cache por 3 dias; qualquer falha devolve null sem incomodar.
/// </summary>
public static class Compat
{
    private static readonly HttpClient Http = Create();
    private static HttpClient Create()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("ResetExplodeLinux/0.16 (+https://github.com/renato-figueiredo/ResetExplode)");
        return h;
    }

    private static string CacheFile(string appId) => Path.Combine(Path.GetDirectoryName(Covers.CacheDir)!, "compat", appId + ".json");

    public static string ProtonDbUrl(GameEntry g) =>
        g.Store == "Steam" ? $"https://www.protondb.com/app/{g.Id}" : "https://www.protondb.com/search?q=" + Uri.EscapeDataString(g.Name);

    public static async Task<CompatInfo?> GetAsync(GameEntry g, CancellationToken ct = default)
    {
        if (g.Store != "Steam") return null;
        string cache = CacheFile(g.Id);
        try
        {
            if (File.Exists(cache) && DateTime.UtcNow - File.GetLastWriteTimeUtc(cache) < TimeSpan.FromDays(3))
                return JsonSerializer.Deserialize<CompatInfo>(File.ReadAllText(cache));

            string? tier = null; int total = 0; string? deck = null;
            try
            {
                using var d = JsonDocument.Parse(await Http.GetStringAsync($"https://www.protondb.com/api/v1/reports/summaries/{g.Id}.json", ct));
                tier = d.RootElement.GetProperty("tier").GetString();
                total = d.RootElement.TryGetProperty("total", out var t) ? t.GetInt32() : 0;
            }
            catch { }
            try
            {
                using var d = JsonDocument.Parse(await Http.GetStringAsync($"https://store.steampowered.com/saleaction/ajaxgetdeckappcompatibilityreport?nAppID={g.Id}", ct));
                deck = d.RootElement.GetProperty("results").GetProperty("resolved_category").GetInt32() switch
                { 3 => "Verificado", 2 => "Jogável", 1 => "Não suportado", _ => null };
            }
            catch { }
            if (tier == null && deck == null) return null;
            var info = new CompatInfo(tier, total, deck);
            Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
            File.WriteAllText(cache, JsonSerializer.Serialize(info));
            return info;
        }
        catch { return null; }
    }
}
