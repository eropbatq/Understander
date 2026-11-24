using Understander.ViewModels;

namespace Understander.Views;

public partial class DialoguePage : ContentPage
{
    /// <summary>Нажатие дольше этого порога считается «удержанием» (push-to-talk).</summary>
    const int HoldToTalkMs = 400;

    DateTime _pressStartedAt;
    bool _pressStartedRecording;

    public DialoguePage(DialogueViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;

        // Держим последнее сообщение в поле зрения.
        vm.Messages.CollectionChanged += (_, _) =>
        {
            if (vm.Messages.Count == 0) return;

            MainThread.BeginInvokeOnMainThread(() =>
                MessagesView.ScrollTo(vm.Messages.Count - 1, position: ScrollToPosition.End, animate: false));
        };
    }

    void OnMicPressed(object? sender, EventArgs e)
    {
        MicButton.Scale = 1.12;

        if (BindingContext is not DialogueViewModel vm) return;

        // Повторное нажатие во время записи — завершаем её (режим «тап-тап»).
        if (vm.IsRecording)
        {
            _pressStartedRecording = false;
            vm.StopRecordingCommand.Execute(null);
            return;
        }

        _pressStartedAt = DateTime.UtcNow;
        _pressStartedRecording = true;

        vm.StartRecordingCommand.Execute(null);
    }

    void OnMicReleased(object? sender, EventArgs e)
    {
        MicButton.Scale = 1;

        if (BindingContext is not DialogueViewModel vm) return;

        // Короткое касание оставляем «в записи» до следующего нажатия,
        // иначе быстрый тап не успевает ничего записать.
        if (_pressStartedRecording && (DateTime.UtcNow - _pressStartedAt).TotalMilliseconds >= HoldToTalkMs)
            vm.StopRecordingCommand.Execute(null);

        _pressStartedRecording = false;
    }

    void OnSwapTapped(object? sender, TappedEventArgs e)
    {
        if (BindingContext is DialogueViewModel vm)
            vm.SwapLanguages();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();

        // Уходя с вкладки, обязательно завершаем запись — иначе микрофон
        // останется занятым, а результат придёт в уже скрытую страницу.
        MicButton.Scale = 1;

        if (BindingContext is DialogueViewModel vm)
            vm.CancelRecording();
    }
}
