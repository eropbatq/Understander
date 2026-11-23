namespace Understander.Services;

/// <summary>
/// Интерфейс распознавания речи (Speech-to-Text).
/// </summary>
public interface ISpeechRecognizer
{
    /// <summary>
    /// Начинает распознавание речи. Возвращает распознанный текст по завершении.
    /// </summary>
    Task<string> StartListeningAsync(string languageCode);

    /// <summary>
    /// Останавливает распознавание речи и возвращает текущий результат.
    /// </summary>
    void StopListening();

    /// <summary>
    /// Проверяет, поддерживается ли распознавание речи на устройстве.
    /// </summary>
    bool IsAvailable { get; }
}
