using System.Net.Http.Json;
using System.Text.Json;

namespace Understander.Services;

public class GoogleTranslator : ITranslator
{
    public string Name => "Google Translate";

    private readonly HttpClient _http;

    public GoogleTranslator(HttpClient http)
    {
        _http = http;
    }

    public async Task<string> TranslateAsync(string text, string fromLang, string toLang)
    {
        var url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl={fromLang}&tl={toLang}&dt=t&q={Uri.EscapeDataString(text)}";
        
        var response = await _http.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        
        var segments = json.GetProperty("sentences");
        var result = "";
        foreach (var segment in segments.EnumerateArray())
        {
            if (segment.TryGetProperty("trans", out var trans))
                result += trans.GetString() ?? "";
        }

        return result.Trim();
    }
}
