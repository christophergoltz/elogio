using CommunityToolkit.Mvvm.ComponentModel;

namespace Elogio.ViewModels;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title = "Elogio";
}
