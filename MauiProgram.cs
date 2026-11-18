using CommunityToolkit.Maui;
using Understander.Services;
using Understander.ViewModels;

namespace Understander;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>()
               .UseMauiCommunityToolkit();

        builder.Services.AddSingleton<HttpClient>();
        builder.Services.AddSingleton<ITranslator, GoogleTranslator>();
        builder.Services.AddSingleton<ITranslator, MyMemoryTranslator>();
        builder.Services.AddSingleton<OcrService>();
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<MainPage>();

        return builder.Build();
    }
}
