namespace Elogio.ViewModels.Models;

public enum DayCellState
{
    Empty,
    Normal,
    OverHours,
    UnderHours,
    MissingEntry,  // Expected > 0 but no time booked
    Weekend,
    Future,
    NoWork
}
