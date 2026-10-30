using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ResetExplode.Linux.Games;

namespace ResetExplode.Linux;

/// <summary>Janela para escolher entre as capas do SteamGridDB. Fecha devolvendo o arquivo escolhido, "" (voltar ao padrão) ou null (cancelar).</summary>
public sealed class CoverPicker : Window
{
    private readonly GameEntry _game;
    private readonly string _key;
    private readonly WrapPanel _grid = new();
    private readonly TextBlock _status = new() { Foreground = new SolidColorBrush(Color.Parse("#8C8C8C")) };
    private readonly TextBox _term = new() { Width = 300, Watermark = "Buscar por outro nome…" };
    private CancellationTokenSource _cts = new();

    public CoverPicker(GameEntry game, string key)
    {
        _game = game; _key = key;
        Title = $"Trocar capa — {game.Name}";
        Width = 900; Height = 680; MinWidth = 600; MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var search = new Button { Classes = { "primary" }, Content = "BUSCAR" };
        search.Click += async (_, _) => await Load(_term.Text);
        var reset = new Button { Classes = { "ghost" }, Content = "VOLTAR AO PADRÃO" };
        reset.Click += (_, _) => Close("");
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 8) };
        bar.Children.Add(_term); bar.Children.Add(search); bar.Children.Add(reset);

        var top = new StackPanel { Margin = new Thickness(18, 16, 18, 8) };
        top.Children.Add(new TextBlock { Text = "Escolha a capa", Classes = { "h1" }, FontSize = 20 });
        top.Children.Add(new TextBlock { Text = game.Store == "Steam" ? "Capas deste jogo (pelo número da Steam). Se vier jogo errado, busque pelo nome." : "Buscado pelo nome do jogo. Se vier jogo errado, busque por outro nome.", Classes = { "sub" }, Margin = new Thickness(0, 4, 0, 10) });
        top.Children.Add(bar);
        top.Children.Add(_status);

        var dock = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        dock.Children.Add(top);
        dock.Children.Add(new ScrollViewer { Content = _grid, Margin = new Thickness(18, 0, 10, 14) });
        Content = dock;
        Closed += (_, _) => _cts.Cancel();
        Opened += async (_, _) => await Load(null);
    }

    private async Task Load(string? term)
    {
        _cts.Cancel(); _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _grid.Children.Clear();
        _status.Text = "Buscando…";
        var list = await Covers.Candidates(_game, string.IsNullOrWhiteSpace(term) ? null : term.Trim(), _key, ct);
        if (ct.IsCancellationRequested) return;
        _status.Text = list.Count == 0 ? "Nenhuma capa encontrada (confira a chave ou tente outro nome)." : $"{list.Count} capas, da maior para a menor resolução.";
        foreach (var c in list.Take(60)) _ = AddThumb(c, ct);
    }

    private async Task AddThumb(CoverCandidate c, CancellationToken ct)
    {
        var bytes = await Covers.FetchBytes(c.ThumbUrl, ct);
        if (bytes == null || ct.IsCancellationRequested) return;
        Bitmap bmp;
        try { bmp = Bitmap.DecodeToWidth(new MemoryStream(bytes), 280); } catch { return; }

        var btn = new Button { Padding = new Thickness(0), Margin = new Thickness(0, 0, 12, 12), Background = Brushes.Transparent, Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand) };
        var st = new StackPanel { Width = 140 };
        st.Children.Add(new Border { CornerRadius = new CornerRadius(8), ClipToBounds = true, Child = new Image { Source = bmp, Width = 140, Height = 210, Stretch = Stretch.UniformToFill } });
        st.Children.Add(new TextBlock { Text = $"{c.Width}×{c.Height}", FontSize = 11, Margin = new Thickness(0, 4, 0, 0) });
        st.Children.Add(new TextBlock { Text = c.Author, FontSize = 10, Classes = { "label" }, TextTrimming = TextTrimming.CharacterEllipsis });
        btn.Content = st;
        btn.Click += async (_, _) =>
        {
            _status.Text = "Baixando a capa escolhida…";
            var path = await Covers.Choose(_game, c);
            if (path != null) Close(path); else _status.Text = "Não consegui baixar essa capa. Tente outra.";
        };
        _grid.Children.Add(btn);
    }
}
