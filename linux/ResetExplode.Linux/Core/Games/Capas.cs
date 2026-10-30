using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ResetExplode.Linux.Games;

/// <summary>Uma capa oferecida pelo SteamGridDB para o jogador escolher.</summary>
public sealed record CoverCandidate(string Url, string ThumbUrl, int Width, int Height, string Author);

/// <summary>Lê largura e altura de PNG/JPEG pelo cabeçalho, sem decodificar a imagem.</summary>
public static class ImageSize
{
    public static bool TryRead(string path, out int w, out int h)
    {
        w = h = 0;
        try
        {
            using var f = File.OpenRead(path);
            Span<byte> head = stackalloc byte[24];
            if (f.Read(head) < 24) return false;
            if (head[0] == 0x89 && head[1] == 'P')                          // PNG: IHDR
            { w = head[16] << 24 | head[17] << 16 | head[18] << 8 | head[19]; h = head[20] << 24 | head[21] << 16 | head[22] << 8 | head[23]; return w > 0; }
            if (head[0] == 0xFF && head[1] == 0xD8)                         // JPEG: procura o marcador SOF
            {
                f.Position = 2;
                while (f.Position < f.Length - 9)
                {
                    int b = f.ReadByte();
                    if (b != 0xFF) continue;
                    int m = f.ReadByte();
                    if (m is 0xD8 or 0x01 or (>= 0xD0 and <= 0xD7) or 0xFF) { if (m == 0xFF) f.Position--; continue; }
                    int len = f.ReadByte() << 8 | f.ReadByte();
                    if (m is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
                    { f.ReadByte(); h = f.ReadByte() << 8 | f.ReadByte(); w = f.ReadByte() << 8 | f.ReadByte(); return w > 0; }
                    f.Position += len - 2;
                }
            }
        }
        catch { }
        return false;      // formato que não sabemos medir (ex.: WebP): quem chama decide
    }

    /// <summary>Boa para cartão vertical: pelo menos 400 px de largura e mais alta que larga. Formato desconhecido passa.</summary>
    public static bool IsGoodCover(string path) => !TryRead(path, out int w, out int h) || (w >= 400 && h > w);
}

/// <summary>
/// Resolve a capa de um jogo. Ordem: escolha do jogador → capa vertical boa do launcher → cache →
/// imagem pública do launcher → SteamGridDB (com chave) → a melhor entre as ruins que já havia → cartão com a inicial.
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
    private static string ChoiceFile(GameEntry g) => Path.Combine(CacheDir, Hash(g.Key) + ".choice");

    private static bool RecentlyFailed(string marker) =>
        File.Exists(marker) && DateTime.UtcNow - File.GetLastWriteTimeUtc(marker) < TimeSpan.FromDays(7);

    /// <summary>
    /// Cada fonte é tentada em ordem e só uma boa capa (vertical, ≥ 400 px) encerra a busca. Quem já falhou fica 7 dias
    /// sem nova tentativa, uma marca por fonte, para não bater na rede a cada abertura. Se nada for bom, vale a menos ruim.
    /// </summary>
    public static async Task<string?> GetAsync(GameEntry g, string? sgdbKey, CancellationToken ct = default)
    {
        Directory.CreateDirectory(CacheDir);
        if (File.Exists(ChoiceFile(g))) return ChoiceFile(g);                       // o jogador escolheu: vale sempre

        string cache = Path.Combine(CacheDir, Hash(g.Key) + ".img");
        string cdnMiss = cache + ".cdn.none", sgdbMiss = cache + ".sgdb.none";
        string? fallback = null;               // melhor imagem "ruim" vista até agora

        if (g.CoverPath != null && File.Exists(g.CoverPath))
        {
            if (ImageSize.IsGoodCover(g.CoverPath)) return g.CoverPath;
            fallback = g.CoverPath;
        }
        if (File.Exists(cache))
        {
            if (ImageSize.IsGoodCover(cache)) return cache;
            fallback ??= cache;
        }
        if (g.CoverUrl != null && !RecentlyFailed(cdnMiss))
        {
            string tmp = cache + ".try";
            if (await Download(g.CoverUrl, tmp, ct))
            {
                if (ImageSize.IsGoodCover(tmp)) { File.Move(tmp, cache, true); return cache; }
                File.Move(tmp, cache, true);          // baixou, mas é pequena: guarda como reserva e não tenta o CDN de novo por 7 dias
                File.WriteAllText(cdnMiss, "");
                fallback ??= cache;
            }
            else File.WriteAllText(cdnMiss, "");
        }
        if (!string.IsNullOrWhiteSpace(sgdbKey) && !RecentlyFailed(sgdbMiss))
        {
            var best = (await Candidates(g, null, sgdbKey!, ct)).FirstOrDefault();
            if (best != null && await Download(best.Url, cache, ct) && ImageSize.IsGoodCover(cache)) return cache;
            File.WriteAllText(sgdbMiss, "");
        }
        return fallback != null && File.Exists(fallback) ? fallback : null;
    }

    /// <summary>Baixa a capa escolhida e passa a usá-la no lugar de qualquer outra.</summary>
    public static async Task<string?> Choose(GameEntry g, CoverCandidate c, CancellationToken ct = default)
    {
        Directory.CreateDirectory(CacheDir);
        return await Download(c.Url, ChoiceFile(g), ct) ? ChoiceFile(g) : null;
    }

    public static void ClearChoice(GameEntry g) { try { File.Delete(ChoiceFile(g)); } catch { } }

    public static async Task<byte[]?> FetchBytes(string url, CancellationToken ct)
    {
        try { return url.StartsWith("https://") ? await Http.GetByteArrayAsync(url, ct) : null; } catch { return null; }
    }

    private static async Task<bool> Download(string url, string dest, CancellationToken ct)
    {
        try
        {
            if (!url.StartsWith("https://")) return false;
            using var r = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!r.IsSuccessStatusCode || r.Content.Headers.ContentType?.MediaType?.StartsWith("image/") != true) return false;
            if (r.Content.Headers.ContentLength is > 12_000_000) return false;
            var bytes = await r.Content.ReadAsByteArrayAsync(ct);
            if (bytes.Length is 0 or > 12_000_000) return false;
            string tmp = dest + ".part";
            await File.WriteAllBytesAsync(tmp, bytes, ct);
            File.Move(tmp, dest, true);
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// Capas verticais do SteamGridDB, as de maior resolução primeiro. Jogo da Steam sem busca digitada usa o appid
    /// (exato); os demais buscam pelo nome (ou pelo termo que o jogador digitar, se a busca errar de jogo).
    /// </summary>
    public static async Task<List<CoverCandidate>> Candidates(GameEntry g, string? term, string key, CancellationToken ct)
    {
        var res = new List<CoverCandidate>();
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
            const string q = "dimensions=600x900,660x930,342x482&types=static";
            string url;
            if (term == null && g.Store == "Steam") url = $"https://www.steamgriddb.com/api/v2/grids/steam/{g.Id}?{q}";
            else
            {
                using var s = await Get("https://www.steamgriddb.com/api/v2/search/autocomplete/" + Uri.EscapeDataString(term ?? g.Name));
                var data = s.RootElement.GetProperty("data");
                if (data.GetArrayLength() == 0) return res;
                url = $"https://www.steamgriddb.com/api/v2/grids/game/{data[0].GetProperty("id").GetInt64()}?{q}";
            }
            using var gr = await Get(url);
            foreach (var e in gr.RootElement.GetProperty("data").EnumerateArray())
            {
                string? u = e.TryGetProperty("url", out var uu) ? uu.GetString() : null;
                if (u == null) continue;
                res.Add(new CoverCandidate(u, e.TryGetProperty("thumb", out var t) ? t.GetString() ?? u : u,
                    e.TryGetProperty("width", out var w) ? w.GetInt32() : 0, e.TryGetProperty("height", out var h) ? h.GetInt32() : 0,
                    e.TryGetProperty("author", out var a) && a.TryGetProperty("name", out var n) ? n.GetString() ?? "" : ""));
            }
        }
        catch { }
        return res.OrderByDescending(c => c.Width).ToList();
    }
}
