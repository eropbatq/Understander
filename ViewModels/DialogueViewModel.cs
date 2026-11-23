using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Understander.Services;

namespace Understander.ViewModels;

/// <summary>
/// Сообщение диалога: текст, язык источника, направление перевода.
/// </summary>
public record DialogueMessage(string Id, string Text, string FromLang, string ToLang, string? TranslatedText, int SpeakerIndex);

public partial class DialogueViewModel : INotifyPropertyChanged
{
    readonly ITranslator _translator;
    readonly ISpeechRecognizer _speechRecognizer;

    public List<Lang> Langs { get; } = new()
    {
        new("ru", "Русский"), new("en", "English"), new("de", "Deutsch"),
        new("fr", "Français"), new("es", "Español"), new("it", "Italiano"), new("tr", "Türkçe"), new("zh", "中文")
    };

    private Lang _person1Lang = null!;
    private Lang _person2Lang = null!;
    private bool _isBusy;
    Task<string>? _listeningTask;

    public ObservableCollection<DialogueMessage> Messages { get; } = new();

    public Lang Person1Lang
    {
        get => _person1Lang;
        set { _person1Lang = value; OnPropertyChanged(); }
    }
    public Lang Person2Lang
    {
        get => _person2Lang;
        set { _person2Lang = value; OnPropertyChanged(); }
    }
    public bool IsBusy
    {
        get => _isBusy;
        set { _isBusy = value; OnPropertyChanged(); }
    }

    public ICommand StartRecordingCommand { get; }
    public ICommand StopRecordingCommand { get; }
    public ICommand ClearCommand { get; }

    public DialogueViewModel(ITranslator translator, ISpeechRecognizer speechRecognizer)
    {
        _translator = translator;
        _speechRecognizer = speechRecognizer;
        Person1Lang = Langs[0];
        Person2Lang = Langs[1];

        StartRecordingCommand = new Command(OnStartRecording);
        StopRecordingCommand = new Command(async () => await OnStopRecording());
        ClearCommand = new Command(ClearDialogue);
    }

    void OnStartRecording()
    {
        if (!_speechRecognizer.IsAvailable) return;
        _listeningTask = _speechRecognizer.StartListeningAsync(Person1Lang.Code);
    }

    async Task OnStopRecording()
    {
        _speechRecognizer.StopListening();

        if (_listeningTask == null) return;

        IsBusy = true;
        try
        {
            var text = await _listeningTask;

            if (string.IsNullOrWhiteSpace(text))
                return;

            await ProcessTextAsync(text, 0);
        }
        catch (Exception ex)
        {
            // Ignore cancellation/timeout errors
            if (ex is OperationCanceledException or TimeoutException) return;
            await Shell.Current.DisplayAlert("Ошибка", ex.Message, "OK");
        }
        finally
        {
            IsBusy = false;
            _listeningTask = null;
        }
    }

    async Task ProcessTextAsync(string text, int speakerIndex)
    {
        var fromLang = speakerIndex == 0 ? Person1Lang.Code : Person2Lang.Code;
        var toLang = speakerIndex == 0 ? Person2Lang.Code : Person1Lang.Code;

        var msgId = Guid.NewGuid().ToString("N")[..8];
        var message = new DialogueMessage(msgId, text, fromLang, toLang, null, speakerIndex);
        Messages.Add(message);

        try
        {
            var translated = await _translator.TranslateAsync(text, fromLang, toLang);
            var updated = message with { TranslatedText = translated };
            var index = Messages.IndexOf(message);
            Messages[index] = updated;
        }
        catch (Exception ex)
        {
            await Shell.Current.DisplayAlert("Ошибка перевода", ex.Message, "OK");
        }
    }

    void ClearDialogue()
    {
        Messages.Clear();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
