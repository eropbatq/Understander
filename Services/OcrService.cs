using System.Diagnostics;
using Plugin.Maui.OCR;

namespace Understander.Services;

/// <summary>
/// Обёртка над OCR-движком.
/// Умеет выбирать язык распознавания (в т.ч. русский) и корректно
/// обрабатывать случай, когда нужный язык в системе не установлен.
/// </summary>
public class OcrService
{
    private readonly IOcrService _ocrService;

    // Дополнительные попытки, если запрошенный язык не дал текста.
    private static readonly string[] FallbackLanguages = { "ru-RU", "en-US", "de-DE" };

    public OcrService(IOcrService ocrService) => _ocrService = ocrService;

    /// <summary>
    /// Распознаёт текст на изображении.
    /// </summary>
    /// <param name="file">Изображение.</param>
    /// <param name="languageCode">Язык текста: ru, en, de … (допускается BCP-47).</param>
    public async Task<string> ReadTextAsync(FileResult file, string? languageCode = null)
    {
        try
        {
            using var stream = await file.OpenReadAsync();
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            var bytes = ms.ToArray();

            var bcp47 = ToBcp47(languageCode);
            Debug.WriteLine($"[OCR] Image {bytes.Length} bytes, requested '{bcp47}'");

#if WINDOWS
            return await ReadTextWithWindowsOcrAsync(bytes, bcp47);
#else
            return await ReadTextWithPluginAsync(bytes, bcp47);
#endif
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[OCR] Error: {ex.Message}");
            return $"Ошибка OCR: {ex.Message}";
        }
    }

    /// <summary>Короткий код языка (ru) → BCP-47 (ru-RU).</summary>
    internal static string ToBcp47(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return "ru-RU";
        if (code.Contains('-')) return code;

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

    /// <summary>
    /// Распознавание через Plugin.Maui.OCR (Android / iOS / MacCatalyst).
    /// Пробуем запрошенный язык, затем резервные, и выбираем лучший результат.
    /// </summary>
    private async Task<string> ReadTextWithPluginAsync(byte[] bytes, string bcp47)
    {
        var supported = _ocrService.SupportedLanguages?.ToList();
        if (supported is { Count: > 0 })
            Debug.WriteLine($"[OCR] Supported: {string.Join(", ", supported)}");

        var candidates = new List<string> { bcp47 };
        foreach (var fallback in FallbackLanguages)
            if (!candidates.Contains(fallback, StringComparer.OrdinalIgnoreCase))
                candidates.Add(fallback);

        var best = "";
        var bestScore = 0.0;

        foreach (var lang in candidates)
        {
            try
            {
                var options = new OcrOptions.Builder()
                    .SetLanguage(lang)
                    .SetTryHard(true)
                    .Build();

                var result = await _ocrService.RecognizeTextAsync(bytes, options);
                var text = result?.AllText?.Trim() ?? "";

                Debug.WriteLine($"[OCR] lang={lang} success={result?.Success} length={text.Length}");

                var score = ScoreText(text, lang);
                if (score > bestScore || (Math.Abs(score - bestScore) < 0.001 && text.Length > best.Length))
                {
                    bestScore = score;
                    best = text;
                }

                // Хороший результат по нужной письменности — дальше не пробуем.
                if (bestScore >= 0.75) break;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[OCR] lang={lang} failed: {ex.Message}");
            }
        }

        return string.IsNullOrWhiteSpace(best) ? "Текст не найден на изображении" : best;
    }

    /// <summary>
    /// Доля букв в ожидаемой письменности. Для ru/uk/bg/sr ожидаем кириллицу,
    /// иначе — латиницу. Помогает выбрать удачную попытку распознавания.
    /// </summary>
    private static double ScoreText(string text, string bcp47)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;

        var letters = text.Where(char.IsLetter).ToArray();
        if (letters.Length == 0) return 0;

        var cyrillicExpected =
            bcp47.StartsWith("ru", StringComparison.OrdinalIgnoreCase) ||
            bcp47.StartsWith("uk", StringComparison.OrdinalIgnoreCase) ||
            bcp47.StartsWith("bg", StringComparison.OrdinalIgnoreCase) ||
            bcp47.StartsWith("sr", StringComparison.OrdinalIgnoreCase);

        var matched = cyrillicExpected
            ? letters.Count(c => c >= '\u0400' && c <= '\u04FF')
            : letters.Count(char.IsAsciiLetter);

        return (double)matched / letters.Length;
    }

