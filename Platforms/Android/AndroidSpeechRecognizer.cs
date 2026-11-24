using Android.App;
using Android.Content;
using Android.Speech;
using Android.Util;
using Microsoft.Maui.ApplicationModel;

namespace Understander.Services;

/// <summary>Системное распознавание через Google RecognizerIntent.</summary>
public sealed class AndroidSpeechRecognizer : ISpeechRecognizer
{
    internal const int RequestCode = 4711;
    internal const string Tag = "Understander.Speech";
    static readonly object Sync = new();
    static TaskCompletionSource<string>? _pending;

    public bool IsAvailable { get; }

    public AndroidSpeechRecognizer()
    {
        try { IsAvailable = SpeechRecognizer.IsRecognitionAvailable(Android.App.Application.Context); }
        catch (Exception ex) { Log.Warn(Tag, $"Service check failed: {ex.Message}"); IsAvailable = false; }
    }

    public async Task<string> StartListeningAsync(string languageCode)
    {
        if (!IsAvailable)
            throw new NotSupportedException("Системный сервис Google распознавания речи недоступен.");

        var activity = Platform.CurrentActivity
            ?? throw new InvalidOperationException("Активное Android-окно приложения не найдено.");
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (Sync)
        {
            if (_pending is not null) throw new InvalidOperationException("Распознавание речи уже запущено.");
            _pending = completion;
        }

        var locale = ToLocale(languageCode);
        var intent = new Intent(RecognizerIntent.ActionRecognizeSpeech);
        intent.PutExtra(RecognizerIntent.ExtraLanguageModel, RecognizerIntent.LanguageModelFreeForm);
        intent.PutExtra(RecognizerIntent.ExtraLanguage, locale);
        intent.PutExtra(RecognizerIntent.ExtraLanguagePreference, locale);
        intent.PutExtra(RecognizerIntent.ExtraPrompt, $"Говорите на языке: {locale}");
        intent.PutExtra(RecognizerIntent.ExtraMaxResults, 1);
        intent.PutExtra(RecognizerIntent.ExtraPartialResults, false);
        intent.PutExtra(RecognizerIntent.ExtraPreferOffline, false);

        try
        {
            Log.Info(Tag, $"Launching Google RecognizerIntent: {locale}");
            activity.StartActivityForResult(intent, RequestCode);
        }
        catch (Exception ex) { Complete(null, ex); }

        return await completion.Task;
    }

    internal static void CompleteFromActivity(int requestCode, Result resultCode, Intent? data)
    {
        if (requestCode != RequestCode) return;

        if (resultCode == Result.Ok)
        {
            var results = data?.GetStringArrayListExtra(RecognizerIntent.ExtraResults);
            var text = results?.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
            Log.Info(Tag, $"Google RecognizerIntent result: '{text ?? "<empty>"}'");
            Complete(text, null);
        }
        else
        {
            Log.Info(Tag, "Google RecognizerIntent cancelled or returned no result");
            Complete(string.Empty, null);
        }
    }

    public void StopListening() => Complete(string.Empty, new OperationCanceledException("Распознавание отменено."));

    static void Complete(string? text, Exception? error)
    {
        TaskCompletionSource<string>? completion;
        lock (Sync) { completion = _pending; _pending = null; }
        if (completion is null) return;
        if (error is not null) completion.TrySetException(error);
        else completion.TrySetResult(text?.Trim() ?? string.Empty);
    }

    static string ToLocale(string code) => code.ToLowerInvariant() switch
    {
        "ru" or "ru-ru" => "ru-RU", "en" or "en-us" => "en-US",
        "de" => "de-DE", "fr" => "fr-FR", "es" => "es-ES",
        "it" => "it-IT", "tr" => "tr-TR", "zh" => "zh-CN", _ => code
    };
}