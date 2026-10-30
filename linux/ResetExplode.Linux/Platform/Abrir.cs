using System.Diagnostics;

namespace ResetExplode.Linux;

public static class Abrir
{
    /// <summary>Abre um endereço no navegador padrão (pelo host, se estiver num distrobox).</summary>
    public static void Url(string url)
    {
        if (!url.StartsWith("https://")) return;
        var psi = new ProcessStartInfo(Setup.InContainer ? "distrobox-host-exec" : "xdg-open") { UseShellExecute = false };
        if (Setup.InContainer) psi.ArgumentList.Add("xdg-open");
        psi.ArgumentList.Add(url);
        try { Process.Start(psi); } catch { }
    }
}
