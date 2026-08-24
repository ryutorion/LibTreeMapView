using LibTreeMapView.Views;

namespace LibTreeMapView;

public partial class App : Application
{
    private readonly IServiceProvider services;

    public App(IServiceProvider services)
    {
        this.services = services;
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var page = services.GetRequiredService<MainPage>();

        var window = new Window(page)
        {
            Title = "Lib Tree Map View — C++ 静的ライブラリのセクションサイズ",
            Width = 1360,
            Height = 900,
            MinimumWidth = 900,
            MinimumHeight = 600,
        };

        // "app.exe foo.lib" のように渡された .lib をそのまま開く。
        string[] args = Environment.GetCommandLineArgs();
        if (args.Length > 1 && File.Exists(args[1]))
        {
            window.Created += async (_, _) => await page.LoadAsync(args[1]);
        }

        return window;
    }
}
