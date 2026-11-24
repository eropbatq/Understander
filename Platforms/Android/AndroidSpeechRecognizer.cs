using Android.Content;
using Android.OS;
using Android.Speech;
using Android.Util;
using Microsoft.Maui.ApplicationModel;

namespace Understander.Services;

/// <summary>
/// Android-распознавание речи через нативный SpeechRecognizer.
/// Работает офлайн через локальный движок на устройстве.
/// </summary>
public class AndroidSpeechRecognizer : ISpeechRecognizer, IDisposable
{
    internal const string Tag = "Understander.Speech";

    /// <summary>Сколько ждём, пока движок начнёт отдавать результат.</summary>
    static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(60);

    /// <summary>Сколько ждём финальный результат после StopListening.</summary>
    static readonly TimeSpan ResultTimeout = TimeSpan.FromSeconds(5);

    readonly bool _isAvailable;
    readonly object _sync = new();

    SpeechRecognizer? _recognizer;
    TaskCompletionSource<string>? _tcs;
    string _partialText = "";
    int _session;
    bool _disposed;

    public bool IsAvailable => _isAvailable && !_disposed;

    public AndroidSpeechRecognizer()
    {
        _isAvailable = CheckAvailability();
    }

    static bool CheckAvailability()
    {
        try
        {
            return SpeechRecognizer.IsRecognitionAvailable(Android.App.Application.Context);
        }
        catch (Exception ex)
        {
            Log.Warn(Tag, $"IsRecognitionAvailable failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Запускает распознавание речи. Возвращает Task, который завершится,
    /// когда движок отдаст результат (или произойдёт ошибка/таймаут).
    /// </summary>
    public async Task<string> StartListeningAsync(string languageCode)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(AndroidSpeechRecognizer));

        Context context = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity
                          ?? Android.App.Application.Context;

        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        int sessionId;

        lock (_sync)
        {
            // Если прошлая сессия по какой-то причине не завершилась — закрываем её,
            // чтобы ожидающий код не остался висеть вечно.
            _tcs?.TrySetCanceled();
            sessionId = ++_session;
            _tcs = tcs;
            _partialText = "";
        }

        // SpeechRecognizer обязан создаваться и запускаться на главном потоке.
        await MainThread.InvokeOnMainThreadAsync(() => StartOnMainThread(context, languageCode, sessionId));

        // Страховка: движок не должен «подвешивать» кнопку записи.
        _ = TimeoutAsync(sessionId, StartTimeout);

        Log.Info(Tag, "[Understander] Speech recognition started");
        return await tcs.Task;
    }

    void StartOnMainThread(Context context, string languageCode, int sessionId)
    {
        if (sessionId != _session) return;

        try
        {
            ReleaseRecognizer();

            var recognizer = SpeechRecognizer.CreateSpeechRecognizer(context);

            if (recognizer is null)
                throw new InvalidOperationException("Системный сервис распознавания речи недоступен.");

            recognizer.SetRecognitionListener(new SpeechRecognitionListener(this, sessionId));
            _recognizer = recognizer;

            var intent = new Intent(RecognizerIntent.ActionRecognizeSpeech);
            intent.PutExtra(RecognizerIntent.ExtraLanguageModel, RecognizerIntent.LanguageModelFreeForm);
            intent.PutExtra(RecognizerIntent.ExtraPrompt, "Говорите...");
            intent.PutExtra(RecognizerIntent.ExtraLanguage, Bcp47Code(languageCode));
            intent.PutExtra(RecognizerIntent.ExtraMaxResults, 3);
            intent.PutExtra(RecognizerIntent.ExtraPartialResults, true);
            intent.PutExtra(RecognizerIntent.ExtraCallingPackage, context.PackageName);

            recognizer.StartListening(intent);
        }
        catch (Exception ex)
        {
            Log.Error(Tag, $"[Understander] Failed to start: {ex.Message}");
            CompleteSession(sessionId, null, $"Не удалось запустить распознавание речи: {ex.Message}");
        }
    }

    /// <summary>
    /// Просит движок отдать финальный результат. Полученный текст завершит Task.
    /// </summary>
    public void StopListening()
    {
        int sessionId;
        lock (_sync)
        {
            sessionId = _session;
        }

        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                _recognizer?.StopListening();
                Log.Info(Tag, "[Understander] StopListening requested");
            }
            catch (Exception ex)
            {
                Log.Warn(Tag, $"[Understander] Error stopping: {ex.Message}");
            }
        });

        // Некоторые движки не отвечают на StopListening — подстрахуемся таймером,
        // иначе TCS никогда не завершится.
        _ = TimeoutAsync(sessionId, ResultTimeout);
    }

    async Task TimeoutAsync(int sessionId, TimeSpan delay)
    {
        await Task.Delay(delay);

        lock (_sync)
        {
            if (sessionId != _session || _tcs is null || _tcs.Task.IsCompleted) return;
        }

        Log.Warn(Tag, $"[Understander] Recognition timeout ({delay.TotalSeconds:F0}s)");
        CompleteSession(sessionId, null, "Речь не распознана. Попробуйте ещё раз.");
    }

    /// <summary>Запоминает промежуточный результат — он пригодится, если финального не будет.</summary>
    internal void ReportPartial(int sessionId, string text)
    {
        lock (_sync)
        {
            if (sessionId != _session) return;
            _partialText = text;
        }
    }

    /// <summary>
    /// Завершает текущую сессию: отдаёт распознанный текст (или последний
    /// промежуточный) и освобождает SpeechRecognizer.
    /// </summary>
    internal void CompleteSession(int sessionId, string? text, string? error)
    {
        TaskCompletionSource<string>? tcs;
        string partial;
        lock (_sync)
        {
            if (sessionId != _session) return;
            tcs = _tcs;
            _tcs = null;
            partial = _partialText;
        }

        ReleaseRecognizer();

        if (tcs is null || tcs.Task.IsCompleted) return;

        var final = !string.IsNullOrWhiteSpace(text) ? text.Trim() : partial.Trim();

        if (!string.IsNullOrWhiteSpace(final))
            tcs.TrySetResult(final);
        else if (error is null || error.Contains("не распознана", StringComparison.OrdinalIgnoreCase))
            // NoMatch/SpeechTimeout — это обычный пустой результат, а не ошибка
            // системного сервиса. ViewModel сама покажет пользователю подсказку.
            tcs.TrySetResult(string.Empty);
        else
            tcs.TrySetException(new InvalidOperationException(
                error ?? "Речь не распознана. Попробуйте ещё раз."));
    }

    void ReleaseRecognizer()
    {
        var recognizer = _recognizer;
        _recognizer = null;

        if (recognizer is null) return;

        // Destroy() тоже должен вызываться на главном потоке.
        MainThread.BeginInvokeOnMainThread(() =>
        {
            try { recognizer.Destroy(); }
            catch (Exception ex) { Log.Warn(Tag, $"[Understander] Error destroying: {ex.Message}"); }
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _session++;
        _tcs = null;
        ReleaseRecognizer();
    }

    static string Bcp47Code(string code)
    {
        return code.ToLowerInvariant() switch
        {
            "ru" => "ru-RU",
            "en" => "en-US",
            "de" => "de-DE",
            "fr" => "fr-FR",
            "es" => "es-ES",
            "it" => "it-IT",
            "tr" => "tr-TR",
            "zh" => "zh-Hans",
            _ => code
        };
    }
}

/// <summary>
/// RecognitionListener для SpeechRecognizer.
/// Не управляет жизненным циклом recognizer'а — только сообщает результаты владельцу.
/// </summary>
sealed class SpeechRecognitionListener : Java.Lang.Object, Android.Speech.IRecognitionListener
{
    readonly AndroidSpeechRecognizer _owner;
    readonly int _sessionId;

    public SpeechRecognitionListener(AndroidSpeechRecognizer owner, int sessionId)
    {
        _owner = owner;
        _sessionId = sessionId;
    }

    public void OnReadyForSpeech(Bundle? readyEvent) =>
        Log.Info(AndroidSpeechRecognizer.Tag, "[Understander] Ready for speech");

    public void OnBeginningOfSpeech() =>
        Log.Info(AndroidSpeechRecognizer.Tag, "[Understander] Beginning of speech");

    public void OnRmsChanged(float rmsAmplitude) { }

    public void OnBufferReceived(byte[]? buffer) { }

    public void OnEndOfSpeech() =>
        Log.Info(AndroidSpeechRecognizer.Tag, "[Understander] End of speech");

    public void OnEvent(int eventType, Bundle? @event) { }

    public void OnError(SpeechRecognizerError error)
    {
        Log.Error(AndroidSpeechRecognizer.Tag, $"[Understander] Speech error: {error}");

        // При NoMatch/SpeechTimeout в partial-результате часто уже лежит
        // распознанный текст — CompleteSession подхватит его.
        _owner.CompleteSession(_sessionId, null, ErrorMessage(error));
    }

    public void OnPartialResults(Bundle? partialResults)
    {
        var text = FirstResult(partialResults);

        if (!string.IsNullOrWhiteSpace(text))
        {
            Log.Info(AndroidSpeechRecognizer.Tag, $"[Understander] Partial: '{text}'");
            _owner.ReportPartial(_sessionId, text!);
        }
    }

    public void OnResults(Bundle? results)
    {
        var text = FirstResult(results);

        Log.Info(AndroidSpeechRecognizer.Tag, $"[Understander] Final: '{text}'");
        _owner.CompleteSession(_sessionId, text, null);
    }

    /// <summary>
    /// Берёт первый непустой вариант распознавания. Confidence-массив намеренно
    /// не используется: большинство движков его не отдают, из-за чего раньше
    /// результат всегда отбрасывался.
    /// </summary>
    static string? FirstResult(Bundle? bundle)
    {
        var list = bundle?.GetStringArrayList(RecognizerIntent.ExtraResults);

        if (list is null) return null;

        foreach (var item in list)
        {
            if (!string.IsNullOrWhiteSpace(item))
                return item;
        }

        return null;
    }

    static string ErrorMessage(SpeechRecognizerError error) => (int)error switch
    {
        1 => "Нет соединения с сервисом распознавания речи.",          // NetworkTimeout
        2 => "Нет соединения с сервисом распознавания речи.",          // Network
        3 => "Ошибка записи звука. Проверьте микрофон.",               // Audio
        4 => "Ошибка сервиса распознавания речи.",                     // Server
        5 => "Ошибка распознавания речи. Попробуйте ещё раз.",          // Client
        6 => "Речь не распознана. Попробуйте ещё раз.",                 // SpeechTimeout
        7 => "Речь не распознана. Попробуйте ещё раз.",                 // NoMatch
        8 => "Сервис распознавания занят. Попробуйте ещё раз.",         // RecognizerBusy
        9 => "Нет доступа к микрофону. Разрешите его в настройках.",    // InsufficientPermissions
        10 => "Слишком много запросов. Попробуйте позже.",              // TooManyRequests
        11 => "Соединение с сервисом распознавания прервано.",          // ServerDisconnected
        _ => $"Ошибка распознавания речи ({error})."
    };
}

