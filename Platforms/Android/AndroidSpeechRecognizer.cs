using Android.Content;
using Android.OS;
using Android.Speech;
using Android.Util;
using Microsoft.Maui.ApplicationModel;

namespace Understander.Services;

/// <summary>Одна простая сессия системного Android SpeechRecognizer.</summary>
public sealed class AndroidSpeechRecognizer : ISpeechRecognizer, IDisposable
{
    internal const string Tag = "Understander.Speech";
    static readonly TimeSpan ResultTimeout = TimeSpan.FromSeconds(12);

    readonly object _gate = new();
    SpeechRecognizer? _recognizer;
    TaskCompletionSource<string>? _result;
    int _session;
    bool _disposed;

    public bool IsAvailable { get; }

    public AndroidSpeechRecognizer()
    {
        try { IsAvailable = SpeechRecognizer.IsRecognitionAvailable(Android.App.Application.Context); }
        catch (Exception ex) { Log.Warn(Tag, $"Service check failed: {ex.Message}"); IsAvailable = false; }
    }

    public async Task<string> StartListeningAsync(string languageCode)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AndroidSpeechRecognizer));
        if (!IsAvailable) throw new NotSupportedException("Системный сервис распознавания речи недоступен.");

        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        int session;
        lock (_gate)
        {
            if (_result is not null) throw new InvalidOperationException("Распознавание уже запущено.");
            session = ++_session;
            _result = completion;
        }

        try { await MainThread.InvokeOnMainThreadAsync(() => StartOnMainThread(languageCode, session)); }
        catch (Exception ex) { Finish(session, null, ex); }

        _ = TimeoutAsync(session);
        return await completion.Task;
    }

    void StartOnMainThread(string languageCode, int session)
    {
        if (!IsCurrent(session)) return;

        try
        {
            var context = Platform.CurrentActivity ?? Android.App.Application.Context;
            var recognizer = SpeechRecognizer.CreateSpeechRecognizer(context)
                ?? throw new InvalidOperationException("Не удалось создать сервис распознавания речи.");
            recognizer.SetRecognitionListener(new Listener(this, session));

            lock (_gate)
            {
                if (!IsCurrent(session)) { recognizer.Destroy(); return; }
                _recognizer = recognizer;
            }

            var locale = ToLocale(languageCode);
            var intent = new Intent(RecognizerIntent.ActionRecognizeSpeech);
            intent.PutExtra(RecognizerIntent.ExtraLanguageModel, RecognizerIntent.LanguageModelFreeForm);
            intent.PutExtra(RecognizerIntent.ExtraLanguage, locale);
            intent.PutExtra(RecognizerIntent.ExtraLanguagePreference, locale);
            intent.PutExtra(RecognizerIntent.ExtraPartialResults, false);
            intent.PutExtra(RecognizerIntent.ExtraMaxResults, 1);
            intent.PutExtra(RecognizerIntent.ExtraPreferOffline, false);
            intent.PutExtra(RecognizerIntent.ExtraCallingPackage, context.PackageName);
            recognizer.StartListening(intent);
            Log.Info(Tag, $"Started: {locale}");
        }
        catch (Exception ex) { Finish(session, null, ex); }
    }

    public void StopListening()
    {
        int session;
        lock (_gate) session = _session;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (!IsCurrent(session)) return;
            try { _recognizer?.StopListening(); Log.Info(Tag, "Stop requested"); }
            catch (Exception ex) { Finish(session, null, ex); }
        });
    }

    async Task TimeoutAsync(int session)
    {
        await Task.Delay(ResultTimeout);
        if (IsCurrent(session)) Finish(session, null, new TimeoutException("Сервис распознавания не вернул результат."));
    }

    bool IsCurrent(int session)
    {
        lock (_gate) return _result is not null && _session == session;
    }

    internal void OnResults(int session, Bundle? bundle)
    {
        var matches = bundle?.GetStringArrayList(RecognizerIntent.ExtraResults);
        Finish(session, matches?.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)), null);
    }

    internal void OnError(int session, SpeechRecognizerError error)
    {
        Log.Warn(Tag, $"Recognition error: {error}");
        if (error is SpeechRecognizerError.NoMatch or SpeechRecognizerError.SpeechTimeout)
            Finish(session, string.Empty, null);
        else
            Finish(session, null, new InvalidOperationException(ErrorMessage(error)));
    }

    void Finish(int session, string? text, Exception? error)
    {
        TaskCompletionSource<string>? completion;
        SpeechRecognizer? recognizer;
        lock (_gate)
        {
            if (_result is null || _session != session) return;
            completion = _result; _result = null;
            recognizer = _recognizer; _recognizer = null;
        }

        void Cleanup()
        {
            try { recognizer?.SetRecognitionListener(null); recognizer?.Destroy(); }
            catch (Exception ex) { Log.Warn(Tag, $"Cleanup failed: {ex.Message}"); }
        }
        if (MainThread.IsMainThread) Cleanup(); else MainThread.BeginInvokeOnMainThread(Cleanup);

        if (error is not null) completion.TrySetException(error);
        else completion.TrySetResult(text?.Trim() ?? string.Empty);
    }

    static string ToLocale(string code) => code.ToLowerInvariant() switch
    {
        "ru" or "ru-ru" => "ru-RU", "en" or "en-us" => "en-US",
        "de" => "de-DE", "fr" => "fr-FR", "es" => "es-ES",
        "it" => "it-IT", "tr" => "tr-TR", "zh" => "zh-CN", _ => code
    };

    static string ErrorMessage(SpeechRecognizerError error) => error switch
    {
        SpeechRecognizerError.Audio => "Ошибка записи звука. Проверьте доступ к микрофону.",
        SpeechRecognizerError.Network or SpeechRecognizerError.NetworkTimeout => "Нет соединения с сервисом распознавания речи.",
        SpeechRecognizerError.RecognizerBusy => "Сервис распознавания занят.",
        SpeechRecognizerError.InsufficientPermissions => "Нет разрешения на микрофон.",
        _ => $"Ошибка распознавания речи: {error}."
    };

    public void Dispose()
    {
        int session;
        lock (_gate) { if (_disposed) return; _disposed = true; session = _session; }
        Finish(session, null, new System.OperationCanceledException("Распознавание остановлено."));
    }

    sealed class Listener : Java.Lang.Object, IRecognitionListener
    {
        readonly AndroidSpeechRecognizer _owner; readonly int _session;
        public Listener(AndroidSpeechRecognizer owner, int session) { _owner = owner; _session = session; }
        public void OnResults(Bundle? results) => _owner.OnResults(_session, results);
        public void OnError(SpeechRecognizerError error) => _owner.OnError(_session, error);
        public void OnReadyForSpeech(Bundle? parameters) { }
        public void OnBeginningOfSpeech() { }
        public void OnRmsChanged(float rmsdB) { }
        public void OnBufferReceived(byte[]? buffer) { }
        public void OnEndOfSpeech() { }
        public void OnPartialResults(Bundle? partialResults) { }
        public void OnEvent(int eventType, Bundle? parameters) { }
    }
}