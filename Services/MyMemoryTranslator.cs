using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;

namespace Understander.Services;

public class MyMemoryTranslator : ITranslator
{
    public string Name => "MyMemory";

    private readonly HttpClient _http;

    public MyMemoryTranslator(HttpClient http)
    {
        _http = http;
    }

    public async Task<string> TranslateAsync(string text, string fromLang, string toLang)
    {
        Debug.WriteLine($"[MyMemory] '{text[..Math.Min(30, text.Length)]}' -> {toLang}");
        
        var langPair = $"{fromLang}|{toLang}";
        var url = $"https://api.mymemory.translated.net/get?q={Uri.EscapeDataString(text)}&langpair={Uri.EscapeDataString(langPair)}";

        var response = await _http.GetAsync(url);
        
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            Debug.WriteLine($"[MyMemory] HTTP Error: {errorContent}");
            throw new Exception($"MyMemory ({response.StatusCode}): {errorContent}");
        }

        var rawJson = await response.Content.ReadAsStringAsync();
        Debug.WriteLine($"[MyMemory] Raw JSON: {rawJson[..Math.Min(200, rawJson.Length)]}");
        
        // MyMemory returns: {"responseStatus":200,"responseData":{"translatedText":"..."},...}
        using var doc = JsonDocument.Parse(rawJson);
        var root = doc.RootElement;

        // Check for error response first
        if (root.TryGetProperty("responseStatus", out var status))
        {
            if (status.ValueKind == JsonValueKind.Object)
            {
                var statusCode = 0;
                if (status.TryGetProperty("status", out var st) && st.ValueKind == JsonValueKind.Number)
                    statusCode = st.GetInt32();
                
                if (statusCode >= 400)
                {
                    var match = status.TryGetProperty("matchType", out var mt) && mt.ValueKind == JsonValueKind.String 
                        ? mt.GetString() ?? "" 
                        : "";
                    var info = status.TryGetProperty("info", out var i) && i.ValueKind == JsonValueKind.String 
                        ? i.GetString() ?? "" 
                        : "";
                    Debug.WriteLine($"[MyMemory] API Error: {match} {info}");
                    throw new Exception($"MyMemory error: {match} {info}");
                }
            }
        }

        // responseData might be an object or null
        if (!root.TryGetProperty("responseData", out var responseData) || responseData.ValueKind == JsonValueKind.Null)
        {
            Debug.WriteLine($"[MyMemory] Empty responseData");
            throw new Exception($"MyMemory вернул пустой responseData: {rawJson[..Math.Min(200, rawJson.Length)]}");
        }

        if (responseData.ValueKind != JsonValueKind.Object)
        {
            Debug.WriteLine($"[MyMemory] responseData not object: {responseData.GetRawText()}");
            throw new Exception($"MyMemory responseData не объект: {responseData.GetRawText()}");
        }

        var translated = "";
        if (responseData.TryGetProperty("translatedText", out var tt) && tt.ValueKind == JsonValueKind.String)
        {
            translated = tt.GetString() ?? "";
        }
        
        Debug.WriteLine($"[MyMemory] Result: '{translated}'");

        if (string.IsNullOrWhiteSpace(translated))
            throw new Exception("MyMemory вернул пустой перевод");

        return translated.Trim();
    }
}
