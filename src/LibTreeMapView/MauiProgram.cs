using LibTreeMapView.Core;
using LibTreeMapView.Core.Caching;
using LibTreeMapView.ViewModels;
using LibTreeMapView.Views;
using Microsoft.Extensions.Logging;

namespace LibTreeMapView;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        builder.Services.AddSingleton(LibraryCache.Default);
        builder.Services.AddSingleton(services => new LibraryLoader(services.GetRequiredService<LibraryCache>()));
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<CompareViewModel>();
        builder.Services.AddSingleton<ComparePage>();
        builder.Services.AddSingleton<SymbolAnalyzerService>();
        builder.Services.AddSingleton<SymbolsViewModel>();
        builder.Services.AddSingleton<SymbolsPage>();
        builder.Services.AddSingleton<MainPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
