using Avalonia;
using System;

namespace ResetExplode.Linux;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static int Main(string[] args)
    {
        // --listar-jogos: imprime a biblioteca em JSON e sai, sem abrir janela (serve de teste e de contrato com outras implementações)
        if (args.Contains("--listar-jogos"))
        {
            var games = Games.Library.Scan(LinuxSettings.Load());
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(games, new System.Text.Json.JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
            return 0;
        }
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new X11PlatformOptions { RenderingMode = new[] { X11RenderingMode.Software } })
#if DEBUG
#endif
            .WithInterFont()
            .LogToTrace(Avalonia.Logging.LogEventLevel.Warning);
}
