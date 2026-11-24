using Understander.ViewModels;

namespace Understander.Views;

public partial class DialoguePage : ContentPage
{
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

        // Режим «тап-тап»: первое нажатие запускает запись, второе завершает её.
        // Не останавливаем запись в Released: на эмуляторе отпускание пальца
        // после обычного нажатия иначе преждевременно обрывало аудиофрагмент.
        if (vm.IsRecording)
        {
            vm.StopRecordingCommand.Execute(null);
            return;
        }

        vm.StartRecordingCommand.Execute(null);
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
