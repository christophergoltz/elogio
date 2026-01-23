using CommunityToolkit.Mvvm.ComponentModel;

namespace Elogio.ViewModels.Models;

/// <summary>
/// Represents a time entry pair (start - end) for display.
/// </summary>
public partial class TimeEntryDisplayItem : ObservableObject
{
    [ObservableProperty]
    private string _displayText = string.Empty;
}
