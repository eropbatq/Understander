using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Microsoft.Maui.Controls;
using Understander.Models;
using Understander.Services;

namespace Understander.ViewModels;

public record Lang(string Code, string Name);

public record HistoryItem(string Source, string Result, string Pair);

public partial class MainViewModel : INotifyPropertyChanged
{
    readonly OcrService _ocr;
    readonly HistoryDatabase _database;

    public List<ITranslator> Providers { get; }
    public List<Lang> FromLangs { get; } = new()
    {
        new("ru", "Русский"), new("en", "English"), new("de", "Deutsch"),
        new("fr", "Français"), new("es", "Español"), new("it", "Italiano"), new("tr", "Türkçe"), new("zh", "中文")
    };
    public List<Lang> ToLangs => FromLangs.Where(l => l.Code != "auto").ToList();
    public ObservableCollection<HistoryItem> History { get; } = new();

    private ITranslator _provider = null!;
    private Lang _from = null!;
    private Lang _to = null!;
    private string _input = "";
    private string _result = "";
    private string _pair = "";
    private bool _isBusy;

    public ITranslator Provider
    {
        get => _provider;
        set { _provider = value; OnPropertyChanged(); }
    }
    public Lang From
    {
        get => _from;
        set { _from = value; OnPropertyChanged(); }
    }
    public Lang To
    {
        get => _to;
        set { _to = value; OnPropertyChanged(); }
    }
    public string Input
    {
        get => _input;
        set { _input = value; OnPropertyChanged(); }
    }
    public string Result
    {
        get => _result;
        set { _result = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasResult)); }
    }
    /// <summary>Языковая пара и движок последнего успешного перевода (для "чипа" на карточке результата).</summary>
    public string Pair
    {
        get => _pair;
        set { _pair = value; OnPropertyChanged(); }
    }
    /// <summary>Есть ли результат для показа карточки перевода.</summary>
    public bool HasResult => !string.IsNullOrWhiteSpace(_result);
    public bool IsBusy
    {
        get => _isBusy;
        set { _isBusy = value; OnPropertyChanged(); }
    }

    public ICommand TranslateCommand { get; }
    public ICommand SwapCommand { get; }
    public ICommand PhotoCommand { get; }
    public ICommand SpeakCommand { get; }
    public ICommand CopyCommand { get; }
    public ICommand ClearCommand { get; }
    public ICommand UseHistoryCommand { get; }
    public ICommand DeleteHistoryCommand { get; }

    public MainViewModel(IEnumerable<ITranslator> providers, OcrService ocr, HistoryDatabase database)
    {
        Providers = providers.ToList();
        _ocr = ocr;
        _database = database;
        Provider = Providers[0];
        From = FromLangs[0]; // Russian
        To = ToLangs[1]; // English

        TranslateCommand = new AsyncCommand(Translate);
        SwapCommand = new Command(Swap);
        PhotoCommand = new AsyncCommand(Photo);
        SpeakCommand = new AsyncCommand(Speak);
        CopyCommand = new AsyncCommand(Copy);
        ClearCommand = new Command(Clear);
        UseHistoryCommand = new Command<HistoryItem>(UseHistory);
        DeleteHistoryCommand = new Command<HistoryItem>(DeleteHistory);

        _ = LoadHistory();

        // Подписываемся на сообщение из HistoryViewModel
#pragma warning disable CS0618 // Type or member is obsolete
        MessagingCenter.Subscribe<HistoryViewModel, HistoryRecord>(this, "UseHistoryItem", (sender, item) =>
        {
            Input = item.Source;
            Result = item.Result;
            Pair = item.Pair;
        });
#pragma warning restore CS0618
    }

    async Task LoadHistory()
    {
        try
        {
            var records = await _database.GetRecordsAsync();
            foreach (var r in records)
            {
                History.Add(new(r.Source, r.Result, r.Pair));
            }
        }
        catch
        {
            // Silent fail — app works without history
        }
    }

    async Task Translate()
    {
        if (string.IsNullOrWhiteSpace(Input)) return;

        IsBusy = true;
        try
        {
            string result;
            string pair;
            var lastError = (Exception?)null;

            // Try selected provider first, then fallback to others
            foreach (var provider in Providers)
            {
                try
                {
                    result = await provider.TranslateAsync(Input, From.Code, To.Code);
                    pair = $"{From.Code.ToUpperInvariant()} → {To.Code.ToUpperInvariant()} · {provider.Name}";
                    Provider = provider; // Update selected provider on success
                    Pair = pair;
                    Result = result; // <-- показать результат на экране
                    History.Insert(0, new(Input, result, pair));

                    // Save to database
                    try
                    {
                        await _database.InsertRecordAsync(new HistoryRecord
                        {
                            Source = Input,
                            Result = result,
                            Pair = pair
                        });
                    }
                    catch { /* Silent fail */ }

                    return;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    // Continue to next provider
                }
            }

            // All providers failed
            throw lastError ?? new Exception("Все переводчики недоступны");
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Ошибка", ex.Message, "OK");
        }
        finally
        {
            IsBusy = false;
        }
    }

    void Swap()
    {
        if (From.Code == "auto") return;
        // Простой обмен языков
        var tempLang = From;
        From = To;
        To = tempLang;
        var tempInput = Input;
        Input = Result;
        Result = tempInput;
    }

    async Task Photo()
    {
        var choice = await Shell.Current.DisplayActionSheet("Выберите источник", "Отмена", null, "Камера", "Галерея");
        FileResult? file = choice switch
        {
            "Камера" => await MediaPicker.Default.CapturePhotoAsync(),
            "Галерея" => await MediaPicker.Default.PickPhotoAsync(),
            _ => null
        };

        if (file is null) return;

        IsBusy = true;
        try
        {
            Input = await _ocr.ReadTextAsync(file);
            if (!string.IsNullOrWhiteSpace(Input) && !Input.StartsWith("Ошибка") && !Input.StartsWith("("))
                await Translate();
            else
            {
                await Shell.Current.DisplayAlert("OCR", $"Не удалось распознать текст: {Input}", "OK");
            }
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("OCR", ex.Message, "OK");
        }
        finally
        {
            IsBusy = false;
        }
    }

    async Task Speak()
    {
        if (string.IsNullOrWhiteSpace(Result)) return;

        var locales = await TextToSpeech.Default.GetLocalesAsync();
        var locale = locales.FirstOrDefault(l => l.Language.StartsWith(To.Code, StringComparison.OrdinalIgnoreCase));

        await TextToSpeech.Default.SpeakAsync(Result, new SpeechOptions { Locale = locale });
    }

    async Task Copy() => await Clipboard.Default.SetTextAsync(Result);

    void Clear() { Input = ""; Result = ""; Pair = ""; }

    void UseHistory(HistoryItem item) { Input = item.Source; Result = item.Result; Pair = item.Pair; }

    void DeleteHistory(HistoryItem item)
    {
        History.Remove(item);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
