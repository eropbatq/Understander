using CommunityToolkit.Maui;
using Plugin.Maui.OCR;
using Understander.Services;
using Understander.ViewModels;
using Understander.Views;

namespace Understander;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>()
               .UseMauiCommunityToolkit()
               .UseOcr();

        // Database path
        string dbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "understander_history.db");
        var historyDb = new HistoryDatabase(dbPath);
        builder.Services.AddSingleton(historyDb);
        
        // Initialize database on startup
        _ = historyDb.InitializeAsync();

        builder.Services.AddSingleton<HttpClient>();
        builder.Services.AddSingleton<ITranslator, GoogleTranslator>();
        builder.Services.AddSingleton<ITranslator, MyMemoryTranslator>();
        builder.Services.AddSingleton<ITranslator, LaraTranslator>();
        builder.Services.AddSingleton<OcrService>();
        builder.Services.AddSingleton<HistoryViewModel>();

        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<MainPage>();

        builder.Services.AddSingleton<HistoryPage>();

        builder.Services.AddSingleton<DialogueViewModel>();
        builder.Services.AddSingleton<DialoguePage>();

#if ANDROID
        // Activity здесь не кэшируется: сервис сам берёт актуальный контекст
        // при каждом запуске записи, поэтому пересоздание Activity не ломает микрофон.
        builder.Services.AddSingleton<ISpeechRecognizer, AndroidSpeechRecognizer>();
#else
        builder.Services.AddSingleton<ISpeechRecognizer, NoOpSpeechRecognizer>();
#endif

        return builder.Build();
    }
}
