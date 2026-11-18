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
        var langPair = $"{fromLang}|{toLang}";
        var url = $"https://api.mymemory.translated.net/get?q={Uri.EscapeDataString(text)}&langpair={langPair}";

        var response = await _http.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var matched = json.GetProperty("responseData").GetProperty("translatedText").GetString();

        return matched ?? throw new Exception("Translation empty");
    }
}
