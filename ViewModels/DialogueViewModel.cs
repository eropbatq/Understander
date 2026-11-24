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

public class DialogueViewModel : INotifyPropertyChanged
{
    const string IdleStatus = "Нажмите на микрофон и говорите";

    readonly IReadOnlyList<ITranslator> _translators;
    readonly ISpeechRecognizer _speechRecognizer;

    Task<string>? _listeningTask;

    private Lang _person1Lang = null!;
    private Lang _person2Lang = null!;
    private int _activeSpeaker;
    private bool _isBusy;
    private bool _isRecording;
    private string _statusText = IdleStatus;

    public List<Lang> Langs { get; } = new()
    {
        new("ru", "Русский"), new("en", "English"), new("de", "Deutsch"),
        new("fr", "Français"), new("es", "Español"), new("it", "Italiano"), new("tr", "Türkçe"), new("zh", "中文")
    };

    public ObservableCollection<DialogueMessage> Messages { get; } = new();

    public Lang Person1Lang
    {
        get => _person1Lang;
        set { _person1Lang = value; OnPropertyChanged(); OnPropertyChanged(nameof(ActiveLang)); }
    }

    public Lang Person2Lang
    {
        get => _person2Lang;
        set { _person2Lang = value; OnPropertyChanged(); OnPropertyChanged(nameof(ActiveLang)); }
    }

    /// <summary>Кто сейчас говорит: 0 — Person1Lang, 1 — Person2Lang.</summary>
    public int ActiveSpeaker
    {
        get => _activeSpeaker;
        set { _activeSpeaker = value; OnPropertyChanged(); OnPropertyChanged(nameof(ActiveLang)); }
    }

    /// <summary>Язык, с которого сейчас распознаётся речь.</summary>
    public Lang ActiveLang => _activeSpeaker == 0 ? Person1Lang : Person2Lang;

    public bool IsBusy
    {
        get => _isBusy;
        set { if (_isBusy == value) return; _isBusy = value; OnPropertyChanged(); }
    }

    /// <summary>Идёт запись с микрофона (кнопка нажата).</summary>
    public bool IsRecording
    {
        get => _isRecording;
        private set { if (_isRecording == value) return; _isRecording = value; OnPropertyChanged(); }
    }

    /// <summary>Подсказка/статус под кнопкой микрофона.</summary>
    public string StatusText
    {
        get => _statusText;
        set { if (_statusText == value) return; _statusText = value; OnPropertyChanged(); }
    }

    public ICommand StartRecordingCommand { get; }
    public ICommand StopRecordingCommand { get; }
    public ICommand ClearCommand { get; }

    public DialogueViewModel(IEnumerable<ITranslator> translators, ISpeechRecognizer speechRecognizer)
    {
        _translators = translators.ToList();
        _speechRecognizer = speechRecognizer;

        Person1Lang = Langs[0];
        Person2Lang = Langs[1];

        StartRecordingCommand = new Command(async () => await StartRecordingAsync());
        StopRecordingCommand = new Command(async () => await StopRecordingAsync());
        ClearCommand = new Command(ClearDialogue);
    }

    /// <summary>Обмен языками собеседников.</summary>
    public void SwapLanguages()
    {
        (Person1Lang, Person2Lang) = (Person2Lang, Person1Lang);
    }

    // ------------------------------------------------------------------
    // Запись / распознавание
    // ------------------------------------------------------------------

    async Task StartRecordingAsync()
    {
        if (IsRecording || IsBusy) return;

        if (!_speechRecognizer.IsAvailable)
        {
            StatusText = "Распознавание речи недоступно";
            await ShowAlertAsync("Микрофон",
                "На этом устройстве недоступно распознавание речи. " +
                "Установите приложение «Google» и включите голосовой ввод в настройках системы.");
            return;
        }

        if (!await EnsureMicrophonePermissionAsync())
        {
            StatusText = "Нет доступа к микрофону";
            await ShowAlertAsync("Микрофон",
                "Разрешите приложению доступ к микрофону в настройках системы.");
            return;
        }

        try
        {
            IsRecording = true;
            StatusText = $"Говорите… · {ActiveLang.Name}";

            _listeningTask = _speechRecognizer.StartListeningAsync(ActiveLang.Code);
        }
        catch (Exception ex)
        {
            IsRecording = false;
            _listeningTask = null;
            StatusText = IdleStatus;
            await ShowAlertAsync("Микрофон", ex.Message);
        }
    }

    async Task StopRecordingAsync()
    {
        if (!IsRecording) return;

        IsRecording = false;

        var task = _listeningTask;
        _listeningTask = null;

        // Просим движок отдать финальный результат по уже записанному фрагменту.
        try { _speechRecognizer.StopListening(); } catch { /* игнорируем */ }

        if (task is null)
        {
            StatusText = IdleStatus;
            return;
        }

        IsBusy = true;
        StatusText = "Распознаю…";

        try
        {
            var text = (await task)?.Trim();

            if (string.IsNullOrWhiteSpace(text))
            {
                StatusText = "Речь не распознана — попробуйте ещё раз";
                return;
            }

            var speaker = ActiveSpeaker;

            StatusText = "Перевожу…";
            await ProcessTextAsync(text, speaker);

            // Передаём слово второму собеседнику.
            ActiveSpeaker = speaker == 0 ? 1 : 0;
            StatusText = $"Говорите: {ActiveLang.Name}";
        }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException)
        {
            StatusText = "Не удалось распознать речь — попробуйте ещё раз";
        }
        catch (Exception ex)
        {
            StatusText = IdleStatus;
            await ShowAlertAsync("Распознавание речи", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Прерывает запись без обработки результата (например, при уходе с вкладки).
    /// </summary>
    public void CancelRecording()
    {
        if (!IsRecording) return;

        IsRecording = false;
        IsBusy = false;

        var task = _listeningTask;
        _listeningTask = null;

        try { _speechRecognizer.StopListening(); } catch { /* игнорируем */ }

        // Задача брошена — исключение нужно прочитать, иначе оно всплывёт
        // как UnobservedTaskException при сборке мусора.
        task?.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);

        StatusText = IdleStatus;
    }

    static async Task<bool> EnsureMicrophonePermissionAsync()
    {
        try
        {
            var status = await Permissions.CheckStatusAsync<Permissions.Microphone>();

            if (status != PermissionStatus.Granted)
                status = await Permissions.RequestAsync<Permissions.Microphone>();

            return status == PermissionStatus.Granted;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Dialogue] Microphone permission failed: {ex}");
            return false;
        }
    }

    // ------------------------------------------------------------------
    // Перевод
    // ------------------------------------------------------------------

    async Task ProcessTextAsync(string text, int speakerIndex)
    {
        var fromLang = speakerIndex == 0 ? Person1Lang.Code : Person2Lang.Code;
        var toLang = speakerIndex == 0 ? Person2Lang.Code : Person1Lang.Code;

        var message = new DialogueMessage(
            Guid.NewGuid().ToString("N")[..8], text, fromLang, toLang, null, speakerIndex);

        Messages.Add(message);

        try
        {
            var translated = await TranslateAsync(text, fromLang, toLang);

            var index = Messages.IndexOf(message);
            if (index >= 0)
                Messages[index] = message with { TranslatedText = translated };
        }
        catch (Exception ex)
        {
            await ShowAlertAsync("Перевод", ex.Message);
        }
    }

    /// <summary>
    /// Переводит текст, перебирая доступные движки: если один недоступен
    /// (блокировка, лимит запросов, нет сети) — пробуем следующий.
    /// </summary>
    async Task<string> TranslateAsync(string text, string fromLang, string toLang)
    {
        var errors = new List<string>();

        foreach (var translator in _translators)
        {
            try
            {
                var result = await translator.TranslateAsync(text, fromLang, toLang);

                if (!string.IsNullOrWhiteSpace(result))
                    return result.Trim();
            }
            catch (Exception ex)
            {
                errors.Add($"{translator.Name}: {ex.Message}");
            }
        }

        throw new InvalidOperationException(errors.Count == 0
            ? "Не настроен ни один движок перевода."
            : "Не удалось перевести текст.\n" + string.Join("\n", errors));
    }

    // ------------------------------------------------------------------

    void ClearDialogue()
    {
        Messages.Clear();
        ActiveSpeaker = 0;
        StatusText = IdleStatus;
    }

    static Task ShowAlertAsync(string title, string message)
    {
        var shell = Shell.Current;
        return shell is null ? Task.CompletedTask : shell.DisplayAlert(title, message, "OK");
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
