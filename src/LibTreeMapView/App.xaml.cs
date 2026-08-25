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

        var window = new Window(new NavigationPage(page))
        {
            Title = "Lib Tree Map View — C++ 静的ライブラリのセクションサイズ",
            Width = 1360,
            Height = 900,
            MinimumWidth = 900,
            MinimumHeight = 600,
        };

        // "app.exe foo.lib" なら単一表示、"app.exe a.lib b.lib" なら比較ビューで開く。
        string[] files = Environment.GetCommandLineArgs().Skip(1).Where(File.Exists).ToArray();

        if (files.Length >= 2)
        {
            ComparePage comparePage = services.GetRequiredService<ComparePage>();
            window.Created += async (_, _) =>
            {
                await page.Navigation.PushAsync(comparePage);
                await comparePage.LoadAsync(files[0], files[1]);
            };
        }
        else if (files.Length == 1)
        {
            window.Created += async (_, _) => await page.LoadAsync(files[0]);
        }

        return window;
    }
}
