using System.Diagnostics;
using Lara.Sdk;

#pragma warning disable CS0618 // Type or member is obsolete (Credentials and Translate<string> deprecations will be fixed in future SDK version)

namespace Understander.Services;

public class LaraTranslator : ITranslator
{
    public string Name => "Lara";

    private readonly Translator _lara;

    public LaraTranslator()
    {
        var accessKeyId = Environment.GetEnvironmentVariable("LARA_ACCESS_KEY_ID") ?? "O27A0N3C027KTPARBSA01A022P";
        var accessKeySecret = Environment.GetEnvironmentVariable("LARA_ACCESS_KEY_SECRET") ?? "EKWSJzSImJtRQ0b6droV04-0r9M7kT8Mc9sr8icA-pc";

        if (string.IsNullOrWhiteSpace(accessKeyId) || string.IsNullOrWhiteSpace(accessKeySecret))
        {
            throw new Exception("Lara Translate: не указаны API-ключи. Установите переменные LARA_ACCESS_KEY_ID и LARA_ACCESS_KEY_SECRET");
        }

        var credentials = new Credentials(accessKeyId, accessKeySecret);
        _lara = new Translator(credentials);
    }

    public async Task<string> TranslateAsync(string text, string fromLang, string toLang)
    {
        Debug.WriteLine($"[LaraTranslate] '{text[..Math.Min(30, text.Length)]}' -> {toLang}");

        var result = await _lara.Translate(text, fromLang, toLang);
        var translated = result.Translation;

        Debug.WriteLine($"[LaraTranslate] Result: '{translated}'");

        if (string.IsNullOrWhiteSpace(translated))
            throw new Exception("Lara Translate вернул пустой перевод");

        return translated.Trim();
    }
}

#pragma warning restore CS0618
