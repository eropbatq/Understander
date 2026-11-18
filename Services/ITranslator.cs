namespace Understander.Services;

public interface ITranslator
{
    string Name { get; }
    Task<string> TranslateAsync(string text, string fromLang, string toLang);
}
