namespace Understander.Services;

/// <summary>
/// Заглушка для платформ без поддержки распознавания речи.
/// </summary>
public class NoOpSpeechRecognizer : ISpeechRecognizer
{
    public bool IsAvailable => false;

    public Task<string> StartListeningAsync(string languageCode)
    {
        throw new NotSupportedException("Распознавание речи недоступно на этой платформе.");
    }

    public void StopListening()
    {
        // No-op
    }
}
