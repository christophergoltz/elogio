using Velopack;

namespace Elogio;

/// <summary>
/// Application entry point with Velopack hooks.
/// VelopackApp.Build().Run() must execute before WPF initialization
/// to properly handle install/update/uninstall scenarios.
/// </summary>
public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Velopack MUST be first - handles install/update/uninstall hooks
        VelopackApp.Build().Run();

        // Now start the WPF application
        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
