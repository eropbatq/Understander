using System.Globalization;
using Understander.ViewModels;

namespace Understander.Views;

public partial class DialoguePage : ContentPage
{
    public DialoguePage(DialogueViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    void OnMicPressed(object? sender, EventArgs e)
    {
        MicButton.Scale = 1.15;
    }

    void OnMicReleased(object? sender, EventArgs e)
    {
        MicButton.Scale = 1;
        if (BindingContext is DialogueViewModel vm)
            vm.StopRecordingCommand.Execute(null);
    }

    void OnSwapTapped(object? sender, TappedEventArgs e)
    {
        if (BindingContext is DialogueViewModel vm)
        {
            var temp = vm.Person1Lang;
            vm.Person1Lang = vm.Person2Lang;
            vm.Person2Lang = temp;
        }
    }
}
