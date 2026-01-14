using CommunityToolkit.Mvvm.ComponentModel;
using Elogio.Services;

namespace Elogio.ViewModels;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title;

    public MainViewModel(IUpdateService updateService)
    {
        _title = $"Elogio v{updateService.CurrentVersion}";
    }
}
