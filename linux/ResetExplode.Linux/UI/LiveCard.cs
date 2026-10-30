using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace ResetExplode.Linux;

/// <summary>Cartões "rótulo ... valor" que se atualizam no lugar, sem recriar a tela.</summary>
public sealed class LiveCard
{
    private readonly Dictionary<string, (Grid Row, TextBlock Value)> _rows = new();
    private readonly Dictionary<string, ProgressBar> _bars = new();
    public Border Root { get; }
    public TextBlock Title { get; }
    public TextBlock Badge { get; }
    private readonly StackPanel _body = new() { Spacing = 2 };

    public LiveCard(string title, string badge = "")
    {
        Title = new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
        Badge = new TextBlock { Text = badge, FontSize = 10.5, FontWeight = FontWeight.SemiBold, Foreground = new SolidColorBrush(Color.Parse("#8C8C8C")), VerticalAlignment = VerticalAlignment.Center };
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(Badge, Dock.Right);
        head.Children.Add(Badge); head.Children.Add(Title);
        var stack = new StackPanel();
        stack.Children.Add(head); stack.Children.Add(_body);
        Root = new Border { Classes = { "card" }, Child = stack };
    }

    /// <summary>Linha de texto; valor vazio ou "—" esconde a linha.</summary>
    public void Set(string key, string label, string? value)
    {
        if (!_rows.TryGetValue(key, out var r))
        {
            var lab = new TextBlock { Text = label, Foreground = new SolidColorBrush(Color.Parse("#8C8C8C")), VerticalAlignment = VerticalAlignment.Center };
            var val = new TextBlock { FontFamily = (FontFamily)Application.Current!.FindResource("Mono")!, HorizontalAlignment = HorizontalAlignment.Right, TextAlignment = TextAlignment.Right, TextWrapping = TextWrapping.Wrap };
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("220,*"), Margin = new Thickness(0, 4) };
            Grid.SetColumn(val, 1);
            row.Children.Add(lab); row.Children.Add(val);
            _body.Children.Add(row);
            _rows[key] = r = (row, val);
        }
        bool show = !string.IsNullOrEmpty(value) && value != "—";
        r.Row.IsVisible = show;
        if (show) r.Value.Text = value;
    }

    /// <summary>Linha com barra de progresso (0–100) e o valor ao lado.</summary>
    public void Bar(string key, string label, double? pct, string text)
    {
        Set(key, label, text);                    // a linha vem primeiro; a barra fica logo abaixo dela
        if (!_bars.TryGetValue(key, out var bar))
        {
            bar = new ProgressBar { Maximum = 100, Height = 4, Margin = new Thickness(0, 0, 0, 6) };
            _bars[key] = bar;
            _body.Children.Add(bar);
        }
        bar.IsVisible = pct != null;
        if (pct != null) bar.Value = Math.Clamp(pct.Value, 0, 100);
    }

    private readonly HashSet<string> _seps = new();

    /// <summary>Título de seção, criado uma única vez mesmo se for chamado a cada atualização.</summary>
    public void Separator(string text)
    {
        if (!_seps.Add(text)) return;
        _body.Children.Add(new TextBlock { Text = text.ToUpperInvariant(), Classes = { "label" }, Margin = new Thickness(0, 12, 0, 2) });
    }
}
