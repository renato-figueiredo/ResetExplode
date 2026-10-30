using System.Globalization;
using System.Text;

namespace ResetExplode.Linux;

/// <summary>Leitura de /proc e /sys, sem processos externos nem root.</summary>
public static class Sys
{
    public static string Read(string path)
    {
        try { return File.ReadAllText(path).Trim(); } catch { return ""; }
    }

    public static double? Num(string path) =>
        double.TryParse(Read(path), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;

    /// <summary>Dentro do distrobox o host fica em /run/host; os arquivos do sistema real são os de lá.</summary>
    public static string HostPath(string p) => File.Exists("/run/.containerenv") && File.Exists("/run/host" + p) ? "/run/host" + p : p;

    private static string OsRelease(string key)
    {
        foreach (var l in Read(HostPath("/etc/os-release")).Split('\n'))
            if (l.StartsWith(key + "=")) return l[(key.Length + 1)..].Trim('"', '\'');
        return "";
    }

    /// <summary>Bazzite (versão da imagem, variante e canal) ou, em outras distros, o PRETTY_NAME.</summary>
    public sealed record Distro(string Name, string Version, string Variant, string Channel, string Base, string Title);

    public static Distro GetDistro()
    {
        string name = OsRelease("NAME"), ver = OsRelease("VERSION");
        string variant = OsRelease("VARIANT");
        string imgJson = Read(HostPath("/usr/share/ublue-os/image-info.json"));
        string J(string k)
        {
            var m = System.Text.RegularExpressions.Regex.Match(imgJson, "\"" + k + "\"\\s*:\\s*\"([^\"]*)\"");
            return m.Success ? m.Groups[1].Value : "";
        }
        if (name.Equals("Bazzite", StringComparison.OrdinalIgnoreCase) && J("version") != "")
        {
            string v = J("version"), tag = J("image-tag");
            return new Distro("Bazzite", v, variant, tag, J("base-image-name"),
                $"Bazzite {v}" + (variant != "" ? $" ({variant})" : ""));
        }
        string pretty = OsRelease("PRETTY_NAME");
        return new Distro(name, ver, variant, "", "", pretty == "" ? "Linux" : pretty);
    }

    public static string OsName() => GetDistro().Title;

    public static string Uptime()
    {
        if (!double.TryParse(Read("/proc/uptime").Split(' ')[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var sec)) return "";
        var t = TimeSpan.FromSeconds(sec);
        return t.TotalDays >= 1 ? $"{(int)t.TotalDays} d {t.Hours} h {t.Minutes} min" : $"{t.Hours} h {t.Minutes} min";
    }

    // ---- nomes de dispositivo PCI (pci.ids do host) ----
    private static string[]? _pciIds;

    /// <summary>Procura o nome do chip e, se existir, do modelo exato (subsistema) em pci.ids.</summary>
    public static (string Chip, string? Board) PciName(string vendor, string device, string subVendor, string subDevice)
    {
        _pciIds ??= File.Exists(HostPath("/usr/share/hwdata/pci.ids")) ? File.ReadAllLines(HostPath("/usr/share/hwdata/pci.ids")) : Array.Empty<string>();
        string v = vendor.Replace("0x", ""), d = device.Replace("0x", ""), sv = subVendor.Replace("0x", ""), sd = subDevice.Replace("0x", "");
        bool inVendor = false, inDevice = false;
        string? chip = null, board = null;
        foreach (var l in _pciIds)
        {
            if (l.Length == 0 || l[0] == '#') continue;
            if (l[0] != '\t') { if (inVendor) break; inVendor = l.StartsWith(v + "  "); continue; }
            if (!inVendor) continue;
            if (l[1] != '\t') { if (inDevice) break; if (l.AsSpan(1).StartsWith(d + "  ")) { inDevice = true; chip = l[(1 + d.Length + 2)..].Trim(); } }
            else if (inDevice && l.AsSpan(2).StartsWith($"{sv} {sd}  ")) { board = l[(2 + sv.Length + 1 + sd.Length + 2)..].Trim(); break; }
        }
        return (chip ?? $"{vendor}:{device}", board);
    }

    public static string Kernel() => Read("/proc/sys/kernel/osrelease");

    public static (string Model, int Threads, double MaxMhz) Cpu()
    {
        string model = "";
        int threads = 0;
        foreach (var l in Read("/proc/cpuinfo").Split('\n'))
        {
            if (l.StartsWith("model name") && model == "") model = l[(l.IndexOf(':') + 1)..].Trim();
            if (l.StartsWith("processor")) threads++;
        }
        double max = (Num("/sys/devices/system/cpu/cpu0/cpufreq/cpuinfo_max_freq") ?? 0) / 1000;
        return (model, threads, max);
    }

    public static double CpuCurrentMhz()
    {
        var l = new List<double>();
        foreach (var d in Directory.GetDirectories("/sys/devices/system/cpu", "cpu[0-9]*"))
            if (Num(d + "/cpufreq/scaling_cur_freq") is double f) l.Add(f / 1000);
        return l.Count == 0 ? 0 : l.Average();
    }

    public static (long TotalKb, long AvailKb) Mem()
    {
        long t = 0, a = 0;
        foreach (var l in Read("/proc/meminfo").Split('\n'))
        {
            var p = l.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length < 2) continue;
            if (p[0] == "MemTotal:") t = long.Parse(p[1]);
            else if (p[0] == "MemAvailable:") a = long.Parse(p[1]);
        }
        return (t, a);
    }

    // Uso total da CPU entre duas chamadas (%).
    private static (long Idle, long Total)? _prev;
    public static double CpuLoad()
    {
        var f = Read("/proc/stat").Split('\n')[0].Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(long.Parse).ToArray();
        long idle = f[3] + f[4], total = f.Sum();
        double load = 0;
        if (_prev is { } p && total > p.Total) load = 100.0 * (1 - (double)(idle - p.Idle) / (total - p.Total));
        _prev = (idle, total);
        return load;
    }

    public static InfoNode Overview()
    {
        var root = new InfoNode { Header = "Sistema" };
        var dist = GetDistro();
        var os = root.Add("Sistema operacional");
        os.Add("Distribuição", dist.Name);
        os.Add("Versão", dist.Version);
        if (dist.Variant != "") os.Add("Variante", dist.Variant);
        if (dist.Channel != "") os.Add("Canal", dist.Channel);
        if (dist.Base != "") os.Add("Base", dist.Base);
        os.Add("Kernel", Kernel());
        os.Add("Ambiente gráfico", Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "?");
        os.Add("Sessão", Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") ?? "?");
        os.Add("Ligado há", Uptime());

        string dmi = "/sys/class/dmi/id/";
        var board = root.Add("Placa-mãe e BIOS");
        board.Add("Placa", $"{Read(dmi + "board_vendor")} {Read(dmi + "board_name")}");
        board.Add("BIOS", $"{Read(dmi + "bios_vendor")} {Read(dmi + "bios_version")}");
        board.Add("Data da BIOS", Read(dmi + "bios_date"));

        var (model, threads, max) = Cpu();
        var cpu = root.Add("Processador");
        cpu.Add("Modelo", model);
        cpu.Add("Threads", threads.ToString());
        cpu.Add("Clock máximo", $"{max:F0} MHz");
        cpu.Add("Governador", Read("/sys/devices/system/cpu/cpu0/cpufreq/scaling_governor"));
        cpu.Add("Perfil de energia", Read("/sys/devices/system/cpu/cpu0/cpufreq/energy_performance_preference"));

        var gfx = root.Add("Placas de vídeo");
        foreach (var g in AmdGpu.Find().OrderBy(g => g.IsIntegrated))
            gfx.Add(g.IsIntegrated ? "Integrada" : "Dedicada", $"{g.Name} — {g.VramTotalGb:F1} GB");

        var (t, a2) = Mem();
        var ram = root.Add("Memória");
        ram.Add("Total", $"{t / 1048576.0:F1} GB");
        ram.Add("Em uso", $"{(t - a2) / 1048576.0:F1} GB");
        ram.Add("Disponível", $"{a2 / 1048576.0:F1} GB");

        var disks = root.Add("Armazenamento");
        foreach (var d in Directory.GetDirectories("/sys/block"))
        {
            string n = Path.GetFileName(d);
            if (n.StartsWith("loop") || n.StartsWith("zram") || n.StartsWith("dm-")) continue;
            double gb = (Num(d + "/size") ?? 0) * 512 / 1e9;
            string kind = Read(d + "/queue/rotational") == "1" ? "HD" : n.StartsWith("nvme") ? "NVMe" : "SSD";
            disks.Add($"/dev/{n}", $"{Read(d + "/device/model")} — {gb:F0} GB ({kind})");
        }
        root.Children.ForEach(c => c.Children.RemoveAll(x => string.IsNullOrWhiteSpace(x.Value)));
        return root;
    }
}

/// <summary>Placas de vídeo AMD lidas do driver amdgpu (sysfs/hwmon).</summary>
public sealed class AmdGpu
{
    public string Card { get; init; } = "";
    public string Dir { get; init; } = "";
    public string Hwmon { get; init; } = "";
    public string Name { get; init; } = "";

    public static List<AmdGpu> Find()
    {
        var list = new List<AmdGpu>();
        foreach (var c in Directory.GetDirectories("/sys/class/drm", "card?"))
        {
            string dev = c + "/device";
            if (Sys.Read(dev + "/vendor") != "0x1002") continue;
            string hw = Directory.Exists(dev + "/hwmon") ? Directory.GetDirectories(dev + "/hwmon").FirstOrDefault() ?? "" : "";
            list.Add(new AmdGpu
            {
                Card = Path.GetFileName(c), Dir = dev, Hwmon = hw,
                Name = FriendlyName(dev),
            });
        }
        return list;
    }

    /// <summary>"Radeon RX 6900 XT (Navi 21)" a partir do pci.ids; sem ele, o código do chip.</summary>
    private static string FriendlyName(string dev)
    {
        var (chip, board) = Sys.PciName(Sys.Read(dev + "/vendor"), Sys.Read(dev + "/device"),
            Sys.Read(dev + "/subsystem_vendor"), Sys.Read(dev + "/subsystem_device"));
        var m = System.Text.RegularExpressions.Regex.Match(chip, @"^(.*?) \[(.*)\]$");
        string family = m.Success ? m.Groups[1].Value : "", model = m.Success ? m.Groups[2].Value : chip;
        if (board != null) model = board;
        return family == "" ? $"AMD {model}" : $"AMD {model} ({family})";
    }

    public double VramTotalGb => (Sys.Num(Dir + "/mem_info_vram_total") ?? 0) / 1073741824;
    public double VramUsedGb => (Sys.Num(Dir + "/mem_info_vram_used") ?? 0) / 1073741824;
    public bool IsIntegrated => VramTotalGb < 1.5;
    public double? Busy => Sys.Num(Dir + "/gpu_busy_percent");
    public double? TempEdge => Hw("temp1_input") / 1000;
    public double? TempJunction => Hw("temp2_input") / 1000;
    public double? TempMem => Hw("temp3_input") / 1000;
    public double? PowerW => (Hw("power1_average") ?? Hw("power1_input")) / 1e6;
    public double? PowerCapW => Hw("power1_cap") / 1e6;
    public double? FanRpm => Hw("fan1_input");
    public double? Volt => Hw("in0_input");
    public string PerfLevel => Sys.Read(Dir + "/power_dpm_force_performance_level");

    private double? Hw(string f) => Hwmon == "" ? null : Sys.Num($"{Hwmon}/{f}");

    /// <summary>Clock atual da linha marcada com * em pp_dpm_*.</summary>
    public string Clock(string file)
    {
        foreach (var l in Sys.Read($"{Dir}/{file}").Split('\n'))
            if (l.EndsWith("*")) return l[(l.IndexOf(':') + 1)..].Replace("*", "").Replace("Mhz", " MHz").Trim();
        return "—";
    }

    private static string Gen(string speed) => speed.Split(' ')[0] switch { "2.5" => "1.0", "5.0" => "2.0", "8.0" => "3.0", "16.0" => "4.0", "32.0" => "5.0", "64.0" => "6.0", _ => "?" };

    /// <summary>"PCIe 4.0 x16" (atual) e o máximo da placa, quando o link está reduzido para economizar energia.</summary>
    public string Pcie
    {
        get
        {
            string cs = Sys.Read(Dir + "/current_link_speed"), cw = Sys.Read(Dir + "/current_link_width");
            if (cs == "") return "—";
            string now = $"PCIe {Gen(cs)} x{cw}";
            string ms = Sys.Read(Dir + "/max_link_speed"), mw = Sys.Read(Dir + "/max_link_width");
            string max = ms == "" ? "" : $"PCIe {Gen(ms)} x{mw}";
            return max != "" && max != now ? $"{now}  (máx. {max})" : now;
        }
    }

    public string Details()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Placa: {Name}  ({(IsIntegrated ? "integrada" : "dedicada")}, {Card})");
        sb.AppendLine($"Driver: amdgpu, kernel {Sys.Kernel()}");
        sb.AppendLine($"VRAM: {VramUsedGb:F2} / {VramTotalGb:F1} GB");
        sb.AppendLine($"Uso: {Busy?.ToString("F0") ?? "—"} %");
        sb.AppendLine($"Clock do núcleo: {Clock("pp_dpm_sclk")}   Memória: {Clock("pp_dpm_mclk")}");
        sb.AppendLine($"PCIe: {Pcie}");
        sb.AppendLine($"Temperatura: borda {TempEdge?.ToString("F0") ?? "—"} °C  hotspot {TempJunction?.ToString("F0") ?? "—"} °C  memória {TempMem?.ToString("F0") ?? "—"} °C");
        sb.AppendLine($"Consumo: {PowerW?.ToString("F0") ?? "—"} W de {PowerCapW?.ToString("F0") ?? "—"} W");
        sb.AppendLine($"Ventoinha: {FanRpm?.ToString("F0") ?? "—"} RPM");
        sb.AppendLine($"Nível de desempenho: {PerfLevel}");
        return sb.ToString();
    }
}

public sealed record SensorReading(string Id, string Hardware, string Chip, string Name, string Kind, double Value, string Unit);

/// <summary>Todos os sensores em /sys/class/hwmon (temperatura, ventoinha, tensão, potência).</summary>
public static class Hwmon
{
    public static List<SensorReading> ReadAll()
    {
        var res = new List<SensorReading>();
        foreach (var h in Directory.GetDirectories("/sys/class/hwmon").OrderBy(x => x, StringComparer.Ordinal))
        {
            string chip = Sys.Read(h + "/name");
            if (chip is "" or "hidpp_battery_0" || chip.StartsWith("ucsi")) continue;
            foreach (var f in Directory.GetFiles(h, "*_input").OrderBy(x => x, StringComparer.Ordinal))
            {
                string b = Path.GetFileName(f)[..^6];             // ex.: temp1
                if (Sys.Num(f) is not double v) continue;
                string label = Sys.Read($"{h}/{b}_label");
                string kind, unit;
                if (b.StartsWith("temp")) { kind = "Temperatura"; unit = "°C"; v /= 1000; }
                else if (b.StartsWith("fan")) { kind = "Ventoinha"; unit = "RPM"; }
                else if (b.StartsWith("in")) { kind = "Tensão"; unit = "V"; v /= 1000; }
                else if (b.StartsWith("power")) { kind = "Potência"; unit = "W"; v /= 1e6; }
                else if (b.StartsWith("curr")) { kind = "Corrente"; unit = "A"; v /= 1000; }
                else if (b.StartsWith("energy")) continue;
                else continue;
                res.Add(new SensorReading($"{h}/{b}", chip, h, label == "" ? b : label, kind, v, unit));
            }
        }
        return res;
    }
}
