using CommunityToolkit.Mvvm.ComponentModel;

namespace Elogio.Desktop.ViewModels;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title = "Elogio";
}
