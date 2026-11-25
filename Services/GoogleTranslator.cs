using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;

namespace Understander.Services;

public class GoogleTranslator : ITranslator
{
    public string Name => "Google";

    private readonly HttpClient _http;

    public GoogleTranslator(HttpClient http)
    {
        _http = http;
        _http.Timeout = TimeSpan.FromSeconds(20);
    }

    public async Task<string> TranslateAsync(string text, string fromLang, string toLang)
    {
        Debug.WriteLine($"[GoogleTranslate] '{text[..Math.Min(30, text.Length)]}' -> {toLang}");
        
        var sl = fromLang == "auto" ? "auto" : fromLang;
        var url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl={sl}&tl={toLang}&dt=t&q={Uri.EscapeDataString(text)}";

        // Retry logic with exponential backoff
        var retries = 3;
        for (var i = 0; i < retries; i++)
        {
            if (i > 0) await Task.Delay(1000 * i); // 1s, 2s delay

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Android; Understander)");
            request.Headers.TryAddWithoutValidation("Accept", "application/json,text/plain,*/*");
            var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

            // Check if response is HTML (CAPTCHA page)
            var contentType = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant() ?? "";
            if (contentType.Contains("html") || !response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                
                // Detect CAPTCHA/block page
                if (errorContent.Contains("captcha") || errorContent.Contains("CAPTCHA") ||
                    errorContent.Contains("blocked") || errorContent.Contains("robots") ||
                    errorContent.Contains("enterprise.js") || errorContent.Contains("<title>"))
                {
                    Debug.WriteLine($"[GoogleTranslate] Blocked: {errorContent[..Math.Min(100, errorContent.Length)]}");
                    throw new Exception($"Google Translate заблокировал запрос (попробуйте позже или используйте MyMemory)");
                }

                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    continue; // retry

                Debug.WriteLine($"[GoogleTranslate] Error: {errorContent[..Math.Min(200, errorContent.Length)]}");
                throw new Exception($"Google Translate ({response.StatusCode}): {errorContent[..Math.Min(200, errorContent.Length)]}");
            }

            var rawJson = await response.Content.ReadAsStringAsync();

            // Check if response is actually JSON
            if (rawJson.Contains("<!DOCTYPE") || rawJson.Contains("<html") || rawJson.Contains("<title"))
            {
                Debug.WriteLine($"[GoogleTranslate] CAPTCHA page returned");
                throw new Exception("Google Translate вернул страницу CAPTCHA. Попробуйте позже или используйте MyMemory");
            }

            Debug.WriteLine($"[GoogleTranslate] Raw JSON: {rawJson[..Math.Min(200, rawJson.Length)]}");

            // Parse as raw JSON array
            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
            {
                throw new Exception($"Неожиданный формат ответа Google: {rawJson[..Math.Min(200, rawJson.Length)]}");
            }

            // root[0] содержит сегменты формата [translatedText, sourceText, ...].
            // Индекс 0 — перевод, индекс 1 — исходный фрагмент.
            var firstElement = root[0];

            if (firstElement.ValueKind != JsonValueKind.Array)
            {
                throw new Exception($"root[0] не массив: {firstElement.GetRawText()}");
            }

            var translatedParts = new List<string>();

            // Iterate through each translation group
            foreach (var group in firstElement.EnumerateArray())
            {
                if (group.ValueKind == JsonValueKind.Array)
                {
                    var arr = group;
                    var count = arr.GetArrayLength();

                    if (count > 0 && arr[0].ValueKind == JsonValueKind.String)
                    {
                        var val = arr[0].GetString();
                        if (!string.IsNullOrEmpty(val)) translatedParts.Add(val);
                    }
                }
                else if (group.ValueKind == JsonValueKind.String)
                {
                    var val = group.GetString();
                    if (!string.IsNullOrEmpty(val)) translatedParts.Add(val);
                }
            }

            var result = string.Concat(translatedParts);

            Debug.WriteLine($"[GoogleTranslate] Result: '{result}'");

            if (string.IsNullOrWhiteSpace(result))
            {
                throw new Exception($"Google вернул пустой результат: {rawJson[..Math.Min(200, rawJson.Length)]}");
            }

            return result.Trim();
        }

        throw new Exception("Google Translate: превышено количество попыток (слишком много запросов)");
    }
}
