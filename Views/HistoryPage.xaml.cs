using Understander.Models;
using Understander.ViewModels;

namespace Understander.Views;

public partial class HistoryPage : ContentPage
{
    public HistoryPage(HistoryViewModel viewModel)
    {
        BindingContext = viewModel;
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is HistoryViewModel vm)
        {
            vm.LoadCommand.Execute(null);
        }
    }
}
