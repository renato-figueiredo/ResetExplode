using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace ResetExplode.Linux;

public partial class MainWindow
{
    private static readonly (string Title, string Text, string Url, Material.Icons.MaterialIconKind Icon)[] CompatLinks =
    {
        ("ProtonDB", "Relatos de jogadores por jogo, com nota de Platina a Quebrado e dicas de configuração. É o primeiro lugar a olhar.", "https://www.protondb.com/", Material.Icons.MaterialIconKind.Database),
        ("Steam Deck: verificação", "Selo oficial da Valve (Verificado, Jogável, Não suportado). Vale para qualquer Linux com Proton, não só para o Deck.", "https://store.steampowered.com/steamdeck/", Material.Icons.MaterialIconKind.Steam),
        ("Are We Anti-Cheat Yet?", "Lista de jogos online e se o anti-cheat funciona no Linux. Jogo com anti-cheat bloqueado costuma não rodar de jeito nenhum.", "https://areweanticheatyet.com/", Material.Icons.MaterialIconKind.ShieldAlertOutline),
        ("Documentação do Bazzite", "Jogos no Bazzite: Steam, Heroic, Lutris, Proton e problemas comuns, passo a passo.", "https://docs.bazzite.gg/Gaming/", Material.Icons.MaterialIconKind.BookOpenPageVariantOutline),
        ("Lutris", "Scripts de instalação prontos para jogos de outras lojas e para jogos antigos.", "https://lutris.net/games", Material.Icons.MaterialIconKind.Controller),
        ("Heroic Games Launcher", "Epic, GOG e Amazon no Linux, com escolha de Proton/Wine por jogo.", "https://heroicgameslauncher.com/", Material.Icons.MaterialIconKind.Rocket),
        ("WineHQ AppDB", "Banco antigo de compatibilidade do Wine. Útil para programas e jogos mais velhos.", "https://appdb.winehq.org/", Material.Icons.MaterialIconKind.Wine),
        ("r/linux_gaming", "Comunidade para tirar dúvida e ver o que mudou depois de uma atualização.", "https://www.reddit.com/r/linux_gaming/", Material.Icons.MaterialIconKind.Forum),
    };

    private static readonly (string Tier, string Color, string Text)[] Tiers =
    {
        ("PLATINUM", "#9AD8FF", "roda perfeito, sem ajuste"),
        ("GOLD", "#E6C14A", "roda perfeito depois de pequenos ajustes"),
        ("SILVER", "#B8B8B8", "roda com problemas que atrapalham pouco"),
        ("BRONZE", "#C88A5A", "roda, mas com problemas sérios"),
        ("BORKED", "#E5645A", "não roda"),
    };

    internal static IBrush TierBrush(string? tier) =>
        new SolidColorBrush(Color.Parse(Tiers.FirstOrDefault(t => t.Tier.Equals(tier, StringComparison.OrdinalIgnoreCase)).Color ?? "#8C8C8C"));

    private void BuildCompat()
    {
        var howCard = new LiveCard("Como ler a compatibilidade", "GUIA");
        var howBody = (StackPanel)howCard.Root.Child!;
        foreach (var line in new[]
        {
            "1. Jogos da Steam: a biblioteca já mostra a nota do ProtonDB e o selo do Steam Deck (✓ verificado, ~ jogável, ✕ não suportado).",
            "2. Outros jogos: o botão de livro no cartão abre a busca do ProtonDB com o nome do jogo.",
            "3. Jogo online? Confira o Are We Anti-Cheat Yet? antes de comprar ou instalar.",
            "4. Não roda? Leia os relatos mais recentes: muitas vezes a solução é trocar a versão do Proton ou uma opção de lançamento.",
        })
            howBody.Children.Add(new TextBlock { Text = line, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3), Foreground = new SolidColorBrush(Color.Parse("#C8C8C8")) });
        CompatPanel.Children.Add(howCard.Root);

        var legend = new LiveCard("Notas do ProtonDB", "LEGENDA");
        foreach (var (tier, color, text) in Tiers) legend.Set(tier, tier, text);
        CompatPanel.Children.Add(legend.Root);

        foreach (var (title, text, url, icon) in CompatLinks)
        {
            var c = new LiveCard(title, "", icon);
            var desc = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.Parse("#8C8C8C")) };
            var open = new Button { Classes = { "ghost" }, Content = "ABRIR NO NAVEGADOR", Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
            open.Click += (_, _) => Abrir.Url(url);
            ((StackPanel)c.Root.Child!).Children.Add(desc);
            ((StackPanel)c.Root.Child!).Children.Add(open);
            CompatPanel.Children.Add(c.Root);
        }
    }
}
