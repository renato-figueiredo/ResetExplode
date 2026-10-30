using System.Diagnostics;
using ResetExplode.Linux.Games;

namespace ResetExplode.Linux;

/// <summary>Abre um jogo pelo launcher dono dele (ou direto, se veio de uma pasta). Devolve o erro, ou null se abriu.</summary>
public static class Lancador
{
    private static bool Has(string tool) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':').Any(d => File.Exists(Path.Combine(d, tool)));

    public static string? Start(GameEntry g)
    {
        try
        {
            var args = g.LaunchKind switch
            {
                "steam" => g.FlatpakLauncher ? new[] { "flatpak", "run", "com.valvesoftware.Steam", $"steam://rungameid/{g.LaunchTarget}" }
                                             : new[] { "xdg-open", $"steam://rungameid/{g.LaunchTarget}" },
                "heroic" => g.FlatpakLauncher ? new[] { "flatpak", "run", "com.heroicgameslauncher.hgl", $"heroic://launch/{g.LaunchTarget}" }
                                              : new[] { "xdg-open", $"heroic://launch/{g.LaunchTarget}" },
                "lutris" => Has("lutris") ? new[] { "lutris", $"lutris:rungameid/{g.LaunchTarget}" }
                                          : new[] { "flatpak", "run", "net.lutris.Lutris", $"lutris:rungameid/{g.LaunchTarget}" },
                "exec" => g.LaunchTarget.EndsWith(".sh") ? new[] { "bash", g.LaunchTarget } : new[] { g.LaunchTarget },
                _ => null,
            };
            if (args == null) return g.Note ?? "Este jogo não pode ser aberto daqui.";
            if (Setup.InContainer) args = new[] { "distrobox-host-exec" }.Concat(args).ToArray();
            var psi = new ProcessStartInfo(args[0]) { UseShellExecute = false };
            foreach (var a in args.Skip(1)) psi.ArgumentList.Add(a);
            if (g.LaunchKind == "exec") psi.WorkingDirectory = Path.GetDirectoryName(g.LaunchTarget)!;
            Process.Start(psi);
            return null;
        }
        catch (Exception e) { return e.Message; }
    }
}
