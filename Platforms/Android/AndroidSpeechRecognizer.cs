using Android.App;
using Android.Content;
using Android.OS;
using Android.Speech;
using Android.Util;

namespace Understander.Services;

/// <summary>
/// Android-распознавание речи через нативный SpeechRecognizer.
/// Работает офлайн через локальный движок на устройстве.
/// </summary>
public class AndroidSpeechRecognizer : ISpeechRecognizer, IDisposable
{
    readonly Android.Content.Context _context;
    readonly bool _isAvailable;
    SpeechRecognizer? _speechRecognizer;
    TaskCompletionSource<string>? _tcs;
    CancellationTokenSource? _timer;
    bool _isDestroyed;

    public bool IsAvailable => _isAvailable;

    public AndroidSpeechRecognizer(Activity activity)
    {
        _context = activity;
        _isAvailable = CheckAvailability();
    }

    static bool CheckAvailability()
    {
        try
        {
            return SpeechRecognizer.IsRecognitionAvailable(Android.App.Application.Context);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Запускает распознавание речи. Возвращает Task, который завершится при OnResults.
    /// </summary>
    public Task<string> StartListeningAsync(string languageCode)
    {
        if (_isDestroyed)
            throw new InvalidOperationException("Распознавание речи недоступно.");

        _tcs = new TaskCompletionSource<string>();
        _isDestroyed = false;

        // Таймаут 15 секунд
        _timer = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        _timer.Token.Register(() =>
        {
            if (!_isDestroyed && _tcs != null)
            {
                Log.Warn("Speech", "[Understander] Speech recognition timed out");
                _tcs.TrySetException(new TimeoutException("Распознавание речи заняло слишком много времени."));
                DestroySafe();
            }
        });

        var listener = new SpeechRecognitionListener(this);

        _speechRecognizer = SpeechRecognizer.CreateSpeechRecognizer(_context);
        _speechRecognizer.SetRecognitionListener(listener);

        var intent = new Intent(RecognizerIntent.ActionRecognizeSpeech);
        intent.PutExtra(RecognizerIntent.ExtraLanguageModel, RecognizerIntent.LanguageModelFreeForm);
        intent.PutExtra(RecognizerIntent.ExtraPrompt, "Говорите...");
        intent.PutExtra(RecognizerIntent.ExtraLanguage, Bcp47Code(languageCode));
        intent.PutExtra(RecognizerIntent.ExtraMaxResults, 1);

        try
        {
            _speechRecognizer.StartListening(intent);
            Log.Info("Speech", "[Understander] Speech recognition started");
        }
        catch (Exception ex)
        {
            Log.Error("Speech", $"[Understander] Failed to start: {ex.Message}");
            _timer?.Cancel();
            _tcs?.TrySetException(ex);
            DestroySafe();
        }

        return _tcs.Task;
    }

    /// <summary>
    /// Останавливает распознавание речи. OnResults будет вызван с текущим результатом.
    /// </summary>
    public void StopListening()
    {
        try
        {
            _speechRecognizer?.StopListening();
            Log.Info("Speech", "[Understander] Speech recognition stopped");
        }
        catch (Exception ex)
        {
            Log.Warn("Speech", $"[Understander] Error stopping: {ex.Message}");
        }
    }

    /// <summary>
    /// Безопасно уничтожает SpeechRecognizer (только один раз).
    /// </summary>
    internal void DestroySafe()
    {
        if (_isDestroyed) return;
        _isDestroyed = true;

        try
        {
            _speechRecognizer?.Destroy();
            _speechRecognizer = null;
        }
        catch (Exception ex)
        {
            Log.Warn("Speech", $"[Understander] Error destroying: {ex.Message}");
        }
    }

    internal void SetResultSafe(string text)
    {
        if (_isDestroyed) return;
        _tcs?.TrySetResult(text);
        DestroySafe();
    }

    internal void SetErrorSafe(Exception ex)
    {
        if (_isDestroyed) return;
        _tcs?.TrySetException(ex);
        DestroySafe();
    }

    public void Dispose()
    {
        _timer?.Dispose();
        DestroySafe();
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
/// НЕ вызывает Destroy() — только завершает TCS.
/// </summary>
sealed class SpeechRecognitionListener : Java.Lang.Object, Android.Speech.IRecognitionListener
{
    readonly AndroidSpeechRecognizer _owner;
    string? _bestText;
    float _bestConfidence;

    public SpeechRecognitionListener(AndroidSpeechRecognizer owner)
    {
        _owner = owner;
        _bestText = "";
        _bestConfidence = 0f;
    }

    public void OnReadyForSpeech(Bundle? readyEvent)
    {
        Log.Info("Speech", "[Understander] Ready for speech");
    }

    public void OnBeginningOfSpeech()
    {
        Log.Info("Speech", "[Understander] Beginning of speech");
    }

    public void OnRmsChanged(float rmsAmplitude) { }

    public void OnBufferReceived(byte[]? buffer) { }

    public void OnEndOfSpeech()
    {
        Log.Info("Speech", "[Understander] End of speech");
    }

    public void OnError(SpeechRecognizerError error)
    {
        Log.Error("Speech", $"[Understander] Speech error: {error}");
        _owner.SetErrorSafe(new InvalidOperationException($"Ошибка распознавания: {error}"));
    }

    public void OnEvent(int eventType, Bundle? @event) { }

    public void OnPartialResults(Bundle? partialResults)
    {
        if (partialResults != null)
        {
            var results = partialResults.GetStringArrayList(RecognizerIntent.ExtraResults);
            var confidences = partialResults.GetFloatArray("android.speech.extra.CONFIDENCE_SCORES");
            
            if (results != null && results.Count > 0)
            {
                var text = results[0];
                var confidence = confidences != null && confidences.Length > 0 ? confidences[0] : 0f;
                
                Log.Info("Speech", $"[Understander] Partial: '{text}' conf={confidence:F2}");
                
                if (confidence > _bestConfidence)
                {
                    _bestConfidence = confidence;
                    _bestText = text;
                }
            }
        }
    }

    public void OnResults(Bundle? results)
    {
        Log.Info("Speech", "[Understander] OnResults received");
        
        if (results != null)
        {
            var resultsList = results.GetStringArrayList(RecognizerIntent.ExtraResults);
            var confidences = results.GetFloatArray("android.speech.extra.CONFIDENCE_SCORES");
            
            if (resultsList != null && resultsList.Count > 0)
            {
                var text = resultsList[0];
                var confidence = confidences != null && confidences.Length > 0 ? confidences[0] : 0f;
                
                Log.Info("Speech", $"[Understander] Final: '{text}' conf={confidence:F2}");
                
                if (confidence > _bestConfidence)
                {
                    _bestConfidence = confidence;
                    _bestText = text;
                }
            }
        }

        if (!string.IsNullOrEmpty(_bestText))
        {
            _owner.SetResultSafe(_bestText);
        }
        else
        {
            _owner.SetErrorSafe(new InvalidOperationException("Речь не распознана. Попробуйте ещё раз."));
        }
    }
}
