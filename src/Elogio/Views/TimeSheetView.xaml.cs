using System.Windows.Controls;
using Elogio.Core.Api;
using Elogio.Desktop.ViewModels;

namespace Elogio.Desktop.Views;

public partial class TimeSheetView : UserControl
{
    public TimeSheetView(KelioClient client)
    {
        InitializeComponent();
        DataContext = new TimeSheetViewModel(client);
    }
}
