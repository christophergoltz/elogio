using System.Windows.Controls;
using Elogio.Persistence.Api;
using Elogio.ViewModels;

namespace Elogio.Views;

public partial class TimeSheetView : UserControl
{
    public TimeSheetView(KelioClient client)
    {
        InitializeComponent();
        DataContext = new TimeSheetViewModel(client);
    }
}
