using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ResetExplode.Linux.Games;

/// <summary>
/// Resolve a capa de um jogo, nesta ordem: arquivo do próprio launcher → cache local →
/// imagem pública do launcher (Steam/Epic/GOG, sem chave) → SteamGridDB (só se houver chave).
/// Sem nenhuma, devolve null e a interface desenha um cartão com a inicial.
/// </summary>
public static class Covers
{
    private static readonly HttpClient Http = Create();
    private static HttpClient Create()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("ResetExplodeLinux/0.16 (+https://github.com/renato-figueiredo/ResetExplode)");
        return h;
    }

    public static string CacheDir { get; } = Path.Combine(
        Environment.GetEnvironmentVariable("XDG_CACHE_HOME") is { Length: > 0 } x ? x : Path.Combine(GameFolders.Home, ".cache"), "resetexplode", "covers");

    private static string Hash(string s) => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();

    public static async Task<string?> GetAsync(GameEntry g, string? sgdbKey, CancellationToken ct = default)
    {
        Directory.CreateDirectory(CacheDir);
        string file = Path.Combine(CacheDir, Hash(g.Key) + ".img"), miss = file + ".none";
        bool localOk = g.CoverPath != null && File.Exists(g.CoverPath);
        // o cache da Steam às vezes só tem o cabeçalho horizontal; a capa vertical pública fica melhor no cartão
        bool localIsVertical = localOk && Path.GetFileName(g.CoverPath!).StartsWith("library_600x900");
        if (localIsVertical) return g.CoverPath;
        if (File.Exists(file)) return file;
        if (localOk && g.CoverUrl != null && await Download(g.CoverUrl, file, ct)) return file;
        if (localOk) return g.CoverPath;
        // quem já falhou espera 7 dias antes de tentar de novo (não martelar a rede)
        if (File.Exists(miss) && DateTime.UtcNow - File.GetLastWriteTimeUtc(miss) < TimeSpan.FromDays(7) && sgdbKey == null) return null;

        if (g.CoverUrl != null && await Download(g.CoverUrl, file, ct)) return file;
        if (!string.IsNullOrWhiteSpace(sgdbKey) && await FromSteamGridDb(g.Name, sgdbKey!, file, ct)) return file;
        File.WriteAllText(miss, "");
        return null;
    }

    private static async Task<bool> Download(string url, string dest, CancellationToken ct)
    {
        try
        {
            if (!url.StartsWith("https://")) return false;
            using var r = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!r.IsSuccessStatusCode || r.Content.Headers.ContentType?.MediaType?.StartsWith("image/") != true) return false;
            if (r.Content.Headers.ContentLength is > 8_000_000) return false;
            var bytes = await r.Content.ReadAsByteArrayAsync(ct);
            if (bytes.Length is 0 or > 8_000_000) return false;
            string tmp = dest + ".part";
            await File.WriteAllBytesAsync(tmp, bytes, ct);
            File.Move(tmp, dest, true);
            return true;
        }
        catch { return false; }
    }

    /// <summary>API v2 do SteamGridDB: busca o jogo pelo nome e pega a primeira capa vertical.</summary>
    private static async Task<bool> FromSteamGridDb(string name, string key, string dest, CancellationToken ct)
    {
        try
        {
            async Task<JsonDocument> Get(string url)
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
                using var r = await Http.SendAsync(req, ct);
                r.EnsureSuccessStatusCode();
                return JsonDocument.Parse(await r.Content.ReadAsStringAsync(ct));
            }
            using var s = await Get("https://www.steamgriddb.com/api/v2/search/autocomplete/" + Uri.EscapeDataString(name));
            var data = s.RootElement.GetProperty("data");
            if (data.GetArrayLength() == 0) return false;
            long id = data[0].GetProperty("id").GetInt64();
            using var gr = await Get($"https://www.steamgriddb.com/api/v2/grids/game/{id}?dimensions=600x900&types=static&limit=1");
            var grids = gr.RootElement.GetProperty("data");
            return grids.GetArrayLength() > 0 && await Download(grids[0].GetProperty("url").GetString()!, dest, ct);
        }
        catch { return false; }
    }
}
