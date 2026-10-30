using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using ResetExplode.Linux.Games;

namespace ResetExplode.Linux;

public partial class MainWindow
{
    private LinuxSettings _settings = new();
    private readonly List<(GameEntry Game, Border Card)> _cards = new();
    private readonly SemaphoreSlim _coverGate = new(4);
    private CancellationTokenSource? _gamesCts;
    private const double CardW = 170, CoverH = 255;

    private async Task RefreshGames()
    {
        _gamesCts?.Cancel();
        var cts = _gamesCts = new CancellationTokenSource();
        GamesInfo.Text = "Procurando jogos…";
        var games = await Task.Run(() => Library.Scan(_settings));
        if (cts.IsCancellationRequested) return;

        GameGrid.Children.Clear(); _cards.Clear();
        foreach (var g in games)
        {
            var card = BuildCard(g, out var cover, out var letter);
            _cards.Add((g, card));
            GameGrid.Children.Add(card);
            _ = LoadCover(g, cover, letter, cts.Token);
        }
        var stores = games.GroupBy(g => g.Store).Select(x => $"{x.Count()} {x.Key}");
        GamesInfo.Text = games.Count == 0
            ? "Nenhum jogo encontrado. Use ADICIONAR PASTA para vincular a pasta dos seus jogos."
            : $"{games.Count} jogos · {string.Join(" · ", stores)}";
        OnGamesSearch(null, null!);
    }

    private Border BuildCard(GameEntry g, out Image cover, out TextBlock letter)
    {
        cover = new Image { Stretch = Stretch.UniformToFill, Width = CardW, Height = CoverH };
        letter = new TextBlock { Text = g.Name.Length > 0 ? g.Name[..1].ToUpperInvariant() : "?", FontSize = 56, FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.Parse("#2E2E2E")), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var art = new Grid { Width = CardW, Height = CoverH, Background = new SolidColorBrush(Color.Parse("#141414")) };
        art.Children.Add(letter); art.Children.Add(cover);
        var artClip = new Border { CornerRadius = new CornerRadius(12), ClipToBounds = true, Child = art };

        var play = new Button { Classes = { "primary" }, Content = "JOGAR", Padding = new Thickness(0, 7), FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, IsEnabled = g.CanLaunch };
        if (!g.CanLaunch && g.Note != null) ToolTip.SetTip(play, g.Note);
        play.Click += (_, _) =>
        {
            string? err = Lancador.Start(g);
            GamesInfo.Text = err == null ? $"Abrindo {g.Name}…" : $"Não consegui abrir {g.Name}: {err}";
        };

        var info = new StackPanel { Spacing = 2, Margin = new Thickness(2, 8, 2, 8) };
        info.Children.Add(new TextBlock { Text = g.Name, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 });
        info.Children.Add(new TextBlock { Text = g.Store.ToUpperInvariant(), Classes = { "label" } });

        var stack = new StackPanel { Width = CardW };
        stack.Children.Add(artClip); stack.Children.Add(info); stack.Children.Add(play);
        return new Border { Margin = new Thickness(0, 0, 14, 14), Child = stack, Tag = g };
    }

    private async Task LoadCover(GameEntry g, Image img, TextBlock letter, CancellationToken ct)
    {
        await _coverGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            string? path = await Covers.GetAsync(g, _settings.SteamGridDbKey, ct);
            if (path == null || ct.IsCancellationRequested) return;
            // decodifica já no tamanho do cartão: cada capa de 600x900 não fica inteira na memória
            var bmp = await Task.Run(() => { using var f = File.OpenRead(path); return Bitmap.DecodeToWidth(f, (int)CardW * 2); });
            Avalonia.Threading.Dispatcher.UIThread.Post(() => { img.Source = bmp; letter.IsVisible = false; });
        }
        catch { }
        finally { _coverGate.Release(); }
    }

    private void OnGamesSearch(object? s, TextChangedEventArgs e)
    {
        string q = GamesSearch.Text?.Trim() ?? "";
        foreach (var (g, card) in _cards) card.IsVisible = q == "" || g.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase);
    }

    private async void OnGamesRefresh(object? s, RoutedEventArgs e) => await RefreshGames();

    private async void OnGamesAddFolder(object? s, RoutedEventArgs e)
    {
        var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Pasta com os jogos (uma subpasta por jogo)", AllowMultiple = true });
        foreach (var f in picked)
            if (f.TryGetLocalPath() is { } p && !_settings.GameFolders.Contains(p)) _settings.GameFolders.Add(p);
        if (picked.Count == 0) return;
        _settings.Save();
        await RefreshGames();
    }

    private void OnGamesKey(object? s, RoutedEventArgs e)
    {
        GamesKeyBox.IsVisible = !GamesKeyBox.IsVisible;
        GamesKeyText.Text = _settings.SteamGridDbKey;
    }

    private void OnGamesKeySite(object? s, RoutedEventArgs e)
    {
        var psi = new System.Diagnostics.ProcessStartInfo(Setup.InContainer ? "distrobox-host-exec" : "xdg-open") { UseShellExecute = false };
        if (Setup.InContainer) psi.ArgumentList.Add("xdg-open");
        psi.ArgumentList.Add("https://www.steamgriddb.com/profile/preferences/api");
        try { System.Diagnostics.Process.Start(psi); } catch { }
    }

    private async void OnGamesKeySave(object? s, RoutedEventArgs e)
    {
        _settings.SteamGridDbKey = string.IsNullOrWhiteSpace(GamesKeyText.Text) ? null : GamesKeyText.Text.Trim();
        _settings.Save();
        GamesKeyBox.IsVisible = false;
        if (Directory.Exists(Covers.CacheDir))
            foreach (var m in Directory.GetFiles(Covers.CacheDir, "*.none")) File.Delete(m);   // tenta de novo os que falharam
        await RefreshGames();
    }
}
