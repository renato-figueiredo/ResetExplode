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

    public MainWindow()
    {
        InitializeComponent();
        SysTree.ItemsSource = Sys.Overview().Children;
        SysTree.ItemTemplate = new Avalonia.Controls.Templates.FuncTreeDataTemplate<InfoNode>(
            (n, _) => new TextBlock { Text = n.Display, Margin = new Thickness(0, 2) }, n => n.Children);
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
        Tick();
        Closing += (_, _) => { _stress.Stop(); _benchCts?.Cancel(); };
    }

    private static double? CpuTemp(List<SensorReading> all) =>
        all.FirstOrDefault(s => s.Hardware == "k10temp" && s.Name is "Tctl" or "Tdie")?.Value
        ?? all.FirstOrDefault(s => s.Hardware is "k10temp" or "coretemp" && s.Kind == "Temperatura")?.Value;

    private void Tick()
    {
        var all = Hwmon.ReadAll();
        SensorCount.Text = $"{all.Count} sensores — atualiza a cada 1 s";
        var sb = new StringBuilder();
        foreach (var g in all.GroupBy(s => s.Hardware))
        {
            sb.AppendLine($"[{g.Key}]");
            foreach (var s in g) sb.AppendLine($"  {s.Kind,-12} {s.Name,-28} {s.Value,10:F1} {s.Unit}");
            sb.AppendLine();
        }
        SensorText.Text = sb.ToString();

        GpuText.Text = _gpus.Count == 0 ? "Nenhuma GPU AMD encontrada (amdgpu)."
            : string.Join("\n──────────\n", _gpus.OrderBy(g => g.IsIntegrated).Select(g => g.Details()));

        double load = Sys.CpuLoad(), temp = CpuTemp(all) ?? 0;
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
        StressBtn.Content = "Parar";
    }

    private void StopStress() { _stress.Stop(); StressBtn.Content = "Iniciar"; }

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
