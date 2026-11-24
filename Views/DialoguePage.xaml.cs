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

    void OnSwapTapped(object? sender, TappedEventArgs e)
    {
        if (BindingContext is DialogueViewModel vm)
            vm.SwapLanguages();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();

        // RecognizerIntent временно открывает системный экран Google и из-за
        // этого MAUI может вызвать OnDisappearing. Нельзя отменять здесь
        // ожидаемый результат: он вернётся через MainActivity.OnActivityResult.
        MicButton.Scale = 1;
    }
}
