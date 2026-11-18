using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Microsoft.Maui.Controls;
using Understander.Models;
using Understander.Services;

namespace Understander.ViewModels;

public class HistoryViewModel : INotifyPropertyChanged
{
    readonly HistoryDatabase _database;

    public ObservableCollection<HistoryRecord> History { get; private set; } = new();

    public ICommand LoadCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand ClearAllCommand { get; }
    public ICommand UseItemCommand { get; }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set { _isBusy = value; OnPropertyChanged(); }
    }

    public HistoryViewModel(HistoryDatabase database)
    {
        _database = database;

        LoadCommand = new AsyncCommand(Load);
        DeleteCommand = new AsyncCommand<HistoryRecord>(Delete);
        ClearAllCommand = new AsyncCommand(ClearAll);
        UseItemCommand = new Command<HistoryRecord>(UseItem);
    }

    async Task Load()
    {
        IsBusy = true;
        try
        {
            var records = await _database.GetRecordsAsync();
            History = new ObservableCollection<HistoryRecord>(records);
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

    async Task Delete(HistoryRecord record)
    {
        await _database.DeleteRecordAsync(record);
        History.Remove(record);
    }

    async Task ClearAll()
    {
        await _database.ClearAllAsync();
        History.Clear();
    }

    async void UseItem(HistoryRecord item)
    {
#pragma warning disable CS0618 // Type or member is obsolete
        // MessagingCenter used for cross-viewmodel communication
        MessagingCenter.Send<HistoryViewModel, HistoryRecord>(this, "UseHistoryItem", item);
#pragma warning restore CS0618
        
        // Возвращаемся на главную
        await Shell.Current.GoToAsync("//Views.MainPage");
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
