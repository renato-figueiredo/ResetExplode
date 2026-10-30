using System.Diagnostics;
using System.Reflection;

namespace ResetExplode.Linux;

public sealed record SetupCheck(string Title, string Detail, bool Ok, bool Required);

/// <summary>Verifica o que o app precisa e roda o script de configuração via pkexec (diálogo gráfico de senha).</summary>
public static class Setup
{
    private static bool CanRead(string p) { try { using var f = File.OpenRead(p); f.ReadByte(); return true; } catch { return false; } }
    private static bool CanWrite(string p) { try { using var f = new FileStream(p, FileMode.Open, FileAccess.Write, FileShare.ReadWrite); return true; } catch { return false; } }
    // Dentro de um distrobox, ferramentas e pkexec precisam rodar no host para valerem de verdade.
    private static readonly bool InContainer = File.Exists("/run/.containerenv");

    private static bool Has(string tool)
    {
        if (!InContainer)
            return (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':').Any(d => File.Exists(Path.Combine(d, tool)));
        try
        {
            var psi = new ProcessStartInfo("distrobox-host-exec") { RedirectStandardOutput = true, UseShellExecute = false };
            foreach (var a in new[] { "sh", "-c", $"command -v {tool}" }) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi)!;
            p.WaitForExit(5000);
            return p.HasExited && p.ExitCode == 0;
        }
        catch { return false; }
    }

    public static List<SetupCheck> Checks()
    {
        var res = new List<SetupCheck>();

        // sysfs tem laços de symlink: sem pular links a busca recursiva nunca termina
        var skipLinks = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint, MaxRecursionDepth = 4 };
        var rapl = Directory.Exists("/sys/devices/virtual/powercap")
            ? Directory.GetFiles("/sys/devices/virtual/powercap", "energy_uj", skipLinks).ToList() : new();
        res.Add(new("Consumo da CPU (RAPL)", rapl.Count == 0 ? "Não disponível neste hardware — nada a fazer."
            : "Leitura de energy_uj para mostrar a potência da CPU.", rapl.Count == 0 || rapl.All(CanRead), true));

        var gpus = AmdGpu.Find().Where(g => !g.IsIntegrated).ToList();
        res.Add(new("Ajustes da GPU AMD", gpus.Count == 0 ? "Nenhuma GPU AMD dedicada — nada a fazer."
            : "Escrita em nível de desempenho, clocks e limite de energia (RESET OC).",
            gpus.Count == 0 || gpus.All(g => CanWrite(g.Dir + "/power_dpm_force_performance_level")), true));

        res.Add(new("smartmontools (saúde dos discos)", "Usado sob demanda, com senha.", Has("smartctl"), false));
        res.Add(new("dmidecode (módulos de RAM)", "Usado sob demanda, com senha.", Has("dmidecode"), false));
        res.Add(new("pkexec (diálogo de permissão)", "Necessário para este instalador.", Has("pkexec"), true));
        return res;
    }

    public static bool Ready(List<SetupCheck> c) => c.All(x => x.Ok || !x.Required);

    private static string Script()
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("resetexplode-setup.sh")!;
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    /// <summary>Executa o script embutido como root. Cada linha de saída chega em <paramref name="log"/>.</summary>
    public static async Task<int> RunAsync(Action<string> log)
    {
        var psi = new ProcessStartInfo(InContainer ? "distrobox-host-exec" : "pkexec") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        if (InContainer) psi.ArgumentList.Add("pkexec");
        psi.ArgumentList.Add("/usr/bin/bash");
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add(Script());
        psi.ArgumentList.Add("resetexplode-setup");
        psi.ArgumentList.Add(Environment.UserName);
        using var p = Process.Start(psi)!;
        void Pump(StreamReader r) { string? l; while ((l = r.ReadLine()) != null) log(l); }
        await Task.WhenAll(Task.Run(() => Pump(p.StandardOutput)), Task.Run(() => Pump(p.StandardError)));
        await p.WaitForExitAsync();
        return p.ExitCode;
    }
}
