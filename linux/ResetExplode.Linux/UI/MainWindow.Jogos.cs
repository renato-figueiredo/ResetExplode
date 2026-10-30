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
            var card = BuildCard(g, out var cover, out var letter, out var compat);
            _cards.Add((g, card));
            GameGrid.Children.Add(card);
            _ = LoadCover(g, cover, letter, cts.Token);
            _ = LoadCompat(g, compat, cts.Token);
        }
        var stores = games.GroupBy(g => g.Store).Select(x => $"{x.Count()} {x.Key}");
        GamesInfo.Text = games.Count == 0
            ? "Nenhum jogo encontrado. Use ADICIONAR PASTA para vincular a pasta dos seus jogos."
            : $"{games.Count} jogos · {string.Join(" · ", stores)}";
        OnGamesSearch(null, null!);
    }

    private Border BuildCard(GameEntry g, out Image cover, out TextBlock letter, out TextBlock compat)
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

        Button IconBtn(Material.Icons.MaterialIconKind kind, string tip)
        {
            var b = new Button { Classes = { "ghost" }, Padding = new Thickness(8, 6), Margin = new Thickness(6, 0, 0, 0),
                Content = new Material.Icons.Avalonia.MaterialIcon { Kind = kind, Width = 16, Height = 16 } };
            ToolTip.SetTip(b, tip);
            return b;
        }
        var btnCover = IconBtn(Material.Icons.MaterialIconKind.ImageEditOutline, "Trocar capa");
        var btnCompat = IconBtn(Material.Icons.MaterialIconKind.BookOpenPageVariantOutline, "Compatibilidade (abre o ProtonDB)");
        var img = cover; var ltr = letter;
        btnCover.Click += async (_, _) => await ChangeCover(g, img, ltr);
        btnCompat.Click += (_, _) => Abrir.Url(Compat.ProtonDbUrl(g));

        var buttons = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") };
        Grid.SetColumn(btnCover, 1); Grid.SetColumn(btnCompat, 2);
        buttons.Children.Add(play); buttons.Children.Add(btnCover); buttons.Children.Add(btnCompat);

        compat = new TextBlock { FontSize = 10.5, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, MinHeight = 14 };
        var sub = new StackPanel { Spacing = 1 };
        sub.Children.Add(new TextBlock { Text = g.Store.ToUpperInvariant(), Classes = { "label" } });
        sub.Children.Add(compat);

        var info = new StackPanel { Spacing = 2, Margin = new Thickness(2, 8, 2, 8) };
        info.Children.Add(new TextBlock { Text = g.Name, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 });
        info.Children.Add(sub);

        var stack = new StackPanel { Width = CardW };
        stack.Children.Add(artClip); stack.Children.Add(info); stack.Children.Add(buttons);
        return new Border { Margin = new Thickness(0, 0, 14, 14), Child = stack, Tag = g };
    }

    private async Task LoadCompat(GameEntry g, TextBlock label, CancellationToken ct)
    {
        var info = await Compat.GetAsync(g, ct);
        if (info == null || ct.IsCancellationRequested) return;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            string deck = info.Deck switch { "Verificado" => "DECK ✓", "Jogável" => "DECK ~", "Não suportado" => "DECK ✕", _ => "" };
            label.Text = string.Join(" · ", new[] { info.Label, deck }.Where(x => !string.IsNullOrEmpty(x)));
            label.Foreground = TierBrush(info.Tier);
        });
    }

    private async Task ChangeCover(GameEntry g, Image img, TextBlock letter)
    {
        if (string.IsNullOrWhiteSpace(_settings.SteamGridDbKey))
        {
            GamesKeyBox.IsVisible = true;
            GamesInfo.Text = "Para escolher entre várias capas, cole primeiro a chave gratuita do SteamGridDB.";
            return;
        }
        var path = await new CoverPicker(g, _settings.SteamGridDbKey!).ShowDialog<string?>(this);
        if (path == null) return;
        if (path == "") { Covers.ClearChoice(g); path = await Covers.GetAsync(g, _settings.SteamGridDbKey); }   // "voltar ao padrão"
        if (path == null) return;
        var bmp = await Task.Run(() => { using var f = File.OpenRead(path); return Bitmap.DecodeToWidth(f, (int)CardW * 2); });
        img.Source = bmp; letter.IsVisible = false;
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

    private void OnGamesKeySite(object? s, RoutedEventArgs e) => Abrir.Url("https://www.steamgriddb.com/profile/preferences/api");

    private async void OnGamesKeySave(object? s, RoutedEventArgs e)
    {
        bool empty = string.IsNullOrWhiteSpace(GamesKeyText.Text);
        var key = LinuxSettings.CleanKey(GamesKeyText.Text);
        if (!empty && key == null)
        {
            GamesInfo.Text = "Essa chave não parece válida: ela tem 32 letras e números (0-9, a-f). Copie só a chave, sem texto antes.";
            return;
        }
        _settings.SteamGridDbKey = key;
        _settings.Save();
        GamesKeyBox.IsVisible = false;
        if (Directory.Exists(Covers.CacheDir))
            foreach (var m in Directory.GetFiles(Covers.CacheDir, "*.none")) File.Delete(m);   // tenta de novo os que falharam
        await RefreshGames();
    }
}
