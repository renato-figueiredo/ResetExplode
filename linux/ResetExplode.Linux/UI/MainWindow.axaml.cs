using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace ResetExplode.Linux;

public partial class MainWindow : Window
{
    private readonly List<AmdGpu> _gpus = AmdGpu.Find();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly StressTester _stress = new();
    private DateTime _stressStart;
    private double _stressMaxTemp;
    private CancellationTokenSource? _benchCts;
    private BenchRenderer? _bench;

    private bool _gateRunning, _ready;

    public MainWindow()
    {
        InitializeComponent();
        _ready = true;
        RefreshSetup();
        Title = $"RESET EXPLODE — {Sys.OsName()}";
        var ver = typeof(MainWindow).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion?.Split('+')[0];
        SideVersion.Text = $"v{ver} · Linux";
        // --pagina=gpu abre direto numa tela (visao, sistema, sensores, gpu, stress, bench, permissoes)
        var arg = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("--pagina="))?[9..];
        int idx = Array.FindIndex(new[] { "visao", "sistema", "sensores", "gpu", "jogos", "stress", "bench", "permissoes" }, x => x == arg);
        if (idx >= 0) Nav.SelectedIndex = idx;
        _settings = LinuxSettings.Load();
        _ = RefreshGames();
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
        Tick();
        Closing += (_, _) => { _stress.Stop(); _benchCts?.Cancel(); };
    }

    private static readonly string[] Pages = { "PageDash", "PageSystem", "PageSensors", "PageGpu", "PageGames", "PageStress", "PageBench", "PageSetup" };

    private void OnNav(object? s, SelectionChangedEventArgs e)
    {
        if (!_ready || (s as ListBox)?.SelectedItem is not ListBoxItem it) return;
        foreach (var name in Pages)
            if (this.FindControl<Control>(name) is { } page) page.IsVisible = name == (string?)it.Tag;
    }

    // ---------------------------------------------------------------- Sistema

    private readonly Dictionary<string, LiveCard> _sysCards = new();

    private void UpdateSystem()
    {
        foreach (var group in Sys.Overview().Children)
        {
            if (!_sysCards.TryGetValue(group.Header, out var card))
            {
                _sysCards[group.Header] = card = new LiveCard(group.Header);
                SysPanel.Children.Add(card.Root);
            }
            foreach (var row in group.Children) card.Set(row.Header, row.Header, row.Value);
        }
    }

    // ---------------------------------------------------------------- Placa de vídeo

    private readonly Dictionary<string, LiveCard> _gpuCards = new();

    private void UpdateGpus()
    {
        if (_gpus.Count == 0 && !_gpuCards.ContainsKey("none"))
        {
            var c = new LiveCard("Nenhuma GPU AMD encontrada", "amdgpu");
            c.Set("msg", "Driver", "O driver amdgpu não expôs nenhuma placa em /sys/class/drm.");
            _gpuCards["none"] = c; GpuPanel.Children.Add(c.Root);
        }
        foreach (var g in _gpus.OrderBy(x => x.IsIntegrated))
        {
            if (!_gpuCards.TryGetValue(g.Card, out var c))
            {
                _gpuCards[g.Card] = c = new LiveCard(g.Name, $"{(g.IsIntegrated ? "INTEGRADA" : "DEDICADA")} · {g.Card}");
                GpuPanel.Children.Add(c.Root);
            }
            c.Bar("busy", "Uso da GPU", g.Busy, g.Busy is double b ? $"{b:F0} %" : "—");
            c.Bar("vram", "Memória de vídeo", g.VramTotalGb > 0 ? 100 * g.VramUsedGb / g.VramTotalGb : null,
                $"{g.VramUsedGb:F2} de {g.VramTotalGb:F1} GB");
            c.Bar("power", "Consumo", g.PowerW is double w && g.PowerCapW is double cap && cap > 0 ? 100 * w / cap : null,
                g.PowerW is double pw ? $"{pw:F0} W" + (g.PowerCapW is double pc ? $" de {pc:F0} W" : "") : "—");
            c.Separator("Clocks");
            c.Set("sclk", "Núcleo", g.Clock("pp_dpm_sclk"));
            c.Set("mclk", "Memória", g.Clock("pp_dpm_mclk"));
            c.Set("pcie", "Barramento", g.Pcie);
            c.Separator("Temperaturas");
            c.Set("tedge", "Borda", g.TempEdge is double e ? $"{e:F0} °C" : "—");
            c.Set("tjunc", "Hotspot", g.TempJunction is double j ? $"{j:F0} °C" : "—");
            c.Set("tmem", "Memória", g.TempMem is double m ? $"{m:F0} °C" : "—");
            c.Set("fan", "Ventoinha", g.FanRpm is double f ? (f > 0 ? $"{f:F0} RPM" : "parada") : "—");
            c.Separator("Driver");
            c.Set("drv", "Driver", "amdgpu (kernel)");
            c.Set("perf", "Nível de desempenho", g.PerfLevel);
        }
    }

    // ---------------------------------------------------------------- Sensores

    private readonly Dictionary<string, LiveCard> _sensorCards = new();

    private static readonly Dictionary<string, string> SensorLabels = new()
    {
        ["edge"] = "Borda", ["junction"] = "Hotspot", ["mem"] = "Memória", ["Composite"] = "Temperatura geral",
        ["Tctl"] = "Núcleo (Tctl)", ["Tdie"] = "Núcleo (Tdie)", ["Tccd1"] = "CCD 1", ["Tccd2"] = "CCD 2",
        ["vddgfx"] = "Tensão do núcleo", ["vddnb"] = "Tensão do SoC", ["PPT"] = "Consumo (PPT)", ["fan1"] = "Ventoinha",
        ["temp1"] = "Temperatura", ["Package id 0"] = "Pacote",
    };

    /// <summary>Nome legível de cada chip do hwmon; amdgpu usa o nome real da placa e módulos repetidos ganham número.</summary>
    private string ChipTitle(string chip, string hwmonPath, Dictionary<string, int> seen)
    {
        seen[chip] = seen.GetValueOrDefault(chip) + 1;
        int n = seen[chip];
        string id = Path.GetFileName(hwmonPath);
        return chip switch
        {
            "amdgpu" => _gpus.FirstOrDefault(g => Path.GetFileName(g.Hwmon) == id)?.Name is { } nm ? nm : $"Placa de vídeo AMD {n}",
            "k10temp" => "Processador AMD (k10temp)",
            "coretemp" => "Processador Intel (coretemp)",
            "nvme" => $"SSD NVMe{(n > 1 ? " " + n : "")}",
            "spd5118" => $"Módulo de RAM DDR5 — slot {n}",
            "asus" or "asus_wmi_sensors" => "Placa-mãe ASUS",
            "zenergy" => "Consumo do processador (zenergy)",
            var c when c.StartsWith("mt79") => "Wi-Fi MediaTek " + c.Split('_')[0].ToUpperInvariant(),
            var c => c,
        };
    }

    private void UpdateSensors(List<SensorReading> all)
    {
        SensorCount.Text = $"{all.Count} sensores em {all.Select(s => s.Chip).Distinct().Count()} chips — atualiza a cada 1 s";
        var seen = new Dictionary<string, int>();
        foreach (var g in all.GroupBy(s => s.Chip))
        {
            var first = g.First();
            string title = ChipTitle(first.Hardware, g.Key, seen);
            if (!_sensorCards.TryGetValue(g.Key, out var card))
            {
                _sensorCards[g.Key] = card = new LiveCard(title, first.Hardware);
                SensorPanel.Children.Add(card.Root);
            }
            foreach (var s in g)
            {
                string label = SensorLabels.GetValueOrDefault(s.Name, s.Name);
                if (s.Name == "temp1" && s.Kind == "Temperatura" && g.Count(x => x.Kind == "Temperatura") > 1) label = "Temperatura " + s.Id[^1];
                string val = s.Unit == "RPM" ? $"{s.Value:F0} RPM" : s.Unit is "V" ? $"{s.Value:F2} V" : $"{s.Value:F1} {s.Unit}";
                card.Set(s.Id, label, val);
            }
        }
    }

    // ---------------------------------------------------------------- Permissões

    private static Control CheckRow(SetupCheck c)
    {
        var icon = new Material.Icons.Avalonia.MaterialIcon
        {
            Kind = c.Ok ? Material.Icons.MaterialIconKind.CheckCircleOutline
                 : c.Required ? Material.Icons.MaterialIconKind.AlertCircleOutline : Material.Icons.MaterialIconKind.MinusCircleOutline,
            Width = 20, Height = 20, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Foreground = c.Ok ? Avalonia.Media.Brushes.White : Avalonia.Media.Brushes.Gray,
        };
        var text = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
        text.Children.Add(new TextBlock { Text = c.Title });
        text.Children.Add(new TextBlock { Text = c.Detail, Classes = { "sub" }, FontSize = 12, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        var row = new DockPanel();
        DockPanel.SetDock(icon, Avalonia.Controls.Dock.Left);
        row.Children.Add(icon); row.Children.Add(text);
        return row;
    }

    /// <summary>Reavalia as permissões; sem elas, a tela de configuração cobre o app inteiro.</summary>
    private void RefreshSetup()
    {
        var checks = Setup.Checks();
        foreach (var list in new[] { GateList, SetupListPage })
        {
            list.Children.Clear();
            foreach (var c in checks) list.Children.Add(CheckRow(c));
        }
        Gate.IsVisible = !Setup.Ready(checks);
    }

    private async void OnGateGo(object? s, RoutedEventArgs e)
    {
        if (_gateRunning) return;
        _gateRunning = true;
        GateGo.IsEnabled = false;
        GateLogBox.IsVisible = true;
        GateLog.Text = "Aguardando a janela de senha do sistema…\n";
        int code = -1;
        try
        {
            code = await Task.Run(() => Setup.RunAsync(line => Dispatcher.UIThread.Post(() =>
            {
                GateLog.Text += line + "\n";
                GateScroll.ScrollToEnd();
            })));
        }
        catch (Exception ex) { GateLog.Text += "Erro: " + ex.Message + "\n"; }
        GateLog.Text += code == 0 ? "" : code == 126 || code == 127 ? "Permissão negada ou cancelada.\n" : $"O script terminou com código {code}.\n";
        _gateRunning = false;
        GateGo.IsEnabled = true;
        RefreshSetup();
    }

    private void OnGateExit(object? s, RoutedEventArgs e) => Close();

    private static double? CpuTemp(List<SensorReading> all) =>
        all.FirstOrDefault(s => s.Hardware == "k10temp" && s.Name is "Tctl" or "Tdie")?.Value
        ?? all.FirstOrDefault(s => s.Hardware is "k10temp" or "coretemp" && s.Kind == "Temperatura")?.Value;

    private void Tick()
    {
        var all = Hwmon.ReadAll();
        UpdateSensors(all);
        UpdateGpus();
        UpdateSystem();

        double load = Sys.CpuLoad(), temp = CpuTemp(all) ?? 0;
        var gpu = _gpus.OrderBy(g => g.IsIntegrated).FirstOrDefault();
        var (memTotal, memAvail) = Sys.Mem();
        double ramPct = memTotal == 0 ? 0 : 100.0 * (memTotal - memAvail) / memTotal;
        DashSub.Text = $"{Sys.OsName()} · kernel {Sys.Kernel()} · ligado há {Sys.Uptime()}";
        DashCpuLoad.Text = $"{load:F0} %"; DashCpuBar.Value = load;
        DashCpuLine.Text = $"{temp:F0} °C · {Sys.CpuCurrentMhz():F0} MHz";
        DashGpuLoad.Text = gpu?.Busy is double gb ? $"{gb:F0} %" : "—"; DashGpuBar.Value = gpu?.Busy ?? 0;
        DashGpuLine.Text = gpu == null ? "sem GPU AMD" : $"{gpu.TempEdge:F0} °C · {gpu.PowerW:F0} W · {gpu.VramUsedGb:F1}/{gpu.VramTotalGb:F0} GB";
        DashRam.Text = $"{ramPct:F0} %"; DashRamBar.Value = ramPct;
        DashRamLine.Text = $"{(memTotal - memAvail) / 1048576.0:F1} / {memTotal / 1048576.0:F0} GB";
        SideTemp.Text = $"{temp:F0} °C"; SideLoad.Text = $"{load:F0} %";
        SideGpuTemp.Text = gpu?.TempEdge is double gt ? $"{gt:F0} °C" : "—";
        SideGpuLoad.Text = gpu?.Busy is double gl ? $"{gl:F0} %" : "—";
        SideStress.Text = _stress.Running ? $"rodando {DateTime.Now - _stressStart:mm\\:ss}" : "parado";
        if (_stress.Running)
        {
            _stressMaxTemp = Math.Max(_stressMaxTemp, temp);
            var t = DateTime.Now - _stressStart;
            var info = new StringBuilder();
            info.AppendLine($"Tempo: {t:mm\\:ss}");
            info.AppendLine($"CPU: {load:F0} %  {Sys.CpuCurrentMhz():F0} MHz  {temp:F0} °C (máx {_stressMaxTemp:F0} °C)");
            if (StressRam.IsChecked == true)
                info.AppendLine($"RAM: {_stress.MemoryAllocatedBytes / 1073741824.0:F1} GB, {_stress.MemoryPasses} passadas, {_stress.MemoryErrors} erros");
            StressInfo.Text = info.ToString();
            if (temp >= (double)(StressLimit.Value ?? 95) || _stress.MemoryErrors > 0)
            {
                StopStress();
                StressInfo.Text += _stress.MemoryErrors > 0 ? "\nParado: erro de memória." : "\nParado: limite de temperatura.";
            }
        }
    }

    private void OnStress(object? s, RoutedEventArgs e)
    {
        if (_stress.Running) { StopStress(); return; }
        if (StressCpu.IsChecked != true && StressRam.IsChecked != true) return;
        _stressStart = DateTime.Now; _stressMaxTemp = 0;
        _stress.Start(StressCpu.IsChecked == true, StressRam.IsChecked == true);
        StressBtn.Content = "PARAR";
    }

    private void StopStress() { _stress.Stop(); StressBtn.Content = "INICIAR"; }

    // ---------------------------------------------------------------- Bench

    private void OnBenchMulti(object? s, RoutedEventArgs e) => StartBench(Environment.ProcessorCount, "Multi-core");
    private void OnBenchSingle(object? s, RoutedEventArgs e) => StartBench(1, "Single-core");
    private void OnBenchStop(object? s, RoutedEventArgs e) => _benchCts?.Cancel();

    private async void StartBench(int threads, string label)
    {
        if (_benchCts != null) return;
        _benchCts = new CancellationTokenSource();
        var ct = _benchCts.Token;
        BenchMulti.IsEnabled = BenchSingle.IsEnabled = false; BenchStop.IsEnabled = true;
        BenchInfo.Text = $"{label}: renderizando com {threads} thread(s)…";
        _bench = new BenchRenderer();
        var bmp = new WriteableBitmap(new PixelSize(BenchRenderer.Width, BenchRenderer.Height), new Vector(96, 96),
            PixelFormat.Bgra8888, AlphaFormat.Opaque);
        BenchImg.Source = bmp;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var ui = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        ui.Tick += (_, _) =>
        {
            using (var fb = bmp.Lock())
                System.Runtime.InteropServices.Marshal.Copy(_bench!.Pixels, 0, fb.Address, _bench.Pixels.Length);
            BenchImg.InvalidateVisual();
            BenchBar.Value = _bench.BucketsTotal == 0 ? 0 : (double)_bench.BucketsDone / _bench.BucketsTotal;
            BenchInfo.Text = $"{label}: {sw.Elapsed:mm\\:ss}  {_bench.SamplesDone / Math.Max(sw.Elapsed.TotalSeconds, 0.001) / 10_000:F0} pts (parcial)";
        };
        ui.Start();
        BenchResult? r = null;
        var bench = _bench;
        try { r = await Task.Run(() => bench.Run(threads, TimeSpan.Zero, ct), ct); }
        catch (OperationCanceledException) { }
        ui.Stop();
        BenchBar.Value = r != null ? 1 : 0;
        BenchInfo.Text = r != null ? $"{label}: nota {r.Score:F0}\nTempo: {r.Seconds:F1} s\nThreads: {r.Threads}" : $"{label}: cancelado.";
        _benchCts.Dispose(); _benchCts = null;
        BenchMulti.IsEnabled = BenchSingle.IsEnabled = true; BenchStop.IsEnabled = false;
    }
}
