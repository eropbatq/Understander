using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Understander.Services;

namespace Understander.ViewModels;

public record Lang(string Code, string Name);
public record HistoryItem(string Source, string Result, string Pair);

public partial class MainViewModel : INotifyPropertyChanged
{
    readonly OcrService _ocr;

    public List<ITranslator> Providers { get; }
    public List<Lang> FromLangs { get; } = new()
    {
        new("auto", "Определить"), new("ru", "Русский"), new("en", "English"), new("de", "Deutsch"),
        new("fr", "Français"), new("es", "Español"), new("it", "Italiano"), new("tr", "Türkçe"), new("zh", "中文")
    };
    public List<Lang> ToLangs => FromLangs.Where(l => l.Code != "auto").ToList();
    public ObservableCollection<HistoryItem> History { get; } = new();

    private ITranslator _provider = null!;
    private Lang _from = null!;
    private Lang _to = null!;
    private string _input = "";
    private string _result = "";
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
        set { _result = value; OnPropertyChanged(); }
    }
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

    public MainViewModel(IEnumerable<ITranslator> providers, OcrService ocr)
    {
        Providers = providers.ToList();
        _ocr = ocr;
        Provider = Providers[0];
        From = FromLangs[0];
        To = ToLangs[1]; // English

        TranslateCommand = new AsyncCommand(Translate);
        SwapCommand = new Command(Swap);
        PhotoCommand = new AsyncCommand(Photo);
        SpeakCommand = new AsyncCommand(Speak);
        CopyCommand = new AsyncCommand(Copy);
        ClearCommand = new Command(Clear);
        UseHistoryCommand = new Command<HistoryItem>(UseHistory);
    }

    async Task Translate()
    {
        if (string.IsNullOrWhiteSpace(Input)) return;

        IsBusy = true;
        try
        {
            Result = await Provider.TranslateAsync(Input, From.Code, To.Code);
            History.Insert(0, new(Input, Result, $"{From.Code} → {To.Code} · {Provider.Name}"));
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
        From = FromLangs.First(l => l.Code == To.Code);
        To = ToLangs.First(l => l.Code == From.Code);
        var temp = Input;
        Input = Result;
        Result = temp;
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
            if (!string.IsNullOrWhiteSpace(Input))
                await Translate();
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

    void Clear() { Input = ""; Result = ""; }

    void UseHistory(HistoryItem item) { Input = item.Source; Result = item.Result; }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

// Simple async command wrapper
public class AsyncCommand : ICommand
{
    readonly Func<Task> _execute;
    public AsyncCommand(Func<Task> execute) { _execute = execute; }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) { _ = _execute(); }
    public event EventHandler? CanExecuteChanged;
}
