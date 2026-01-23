using Elogio.Persistence.Dto;
using Serilog;

namespace Elogio.Services;

/// <summary>
/// Service for managing punch (clock-in/clock-out) operations and state.
/// </summary>
public class PunchService : IPunchService
{
    private readonly IKelioService _kelioService;

    private bool? _punchState;
    private bool _isKommenEnabled;
    private bool _isGehenEnabled;
    private bool _isPunchInProgress;

    public PunchService(IKelioService kelioService)
    {
        _kelioService = kelioService;
    }

    public bool? PunchState => _punchState;
    public bool IsKommenEnabled => _isKommenEnabled;
    public bool IsGehenEnabled => _isGehenEnabled;
    public bool IsPunchInProgress => _isPunchInProgress;

    public event EventHandler? StateChanged;

    public async Task<PunchResultDto?> PunchAsync()
    {
        _isPunchInProgress = true;
        UpdateButtonState();
        RaiseStateChanged();

        try
        {
            var result = await _kelioService.PunchAsync();

            if (result is { Success: true })
            {
                // Update state based on punch result
                _punchState = result.Type == PunchType.ClockIn;
                Log.Information("PunchService: Punch successful, new state: {State}", 
                    _punchState == true ? "clocked in" : "clocked out");
            }

            return result;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "PunchService: Punch failed");
            return null;
        }
        finally
        {
            _isPunchInProgress = false;
            UpdateButtonState();
            RaiseStateChanged();
        }
    }

    public void UpdateStateFromEntryCount(int entryCount)
    {
        // Odd number of entries = clocked in, even = clocked out
        _punchState = entryCount % 2 == 1;
        Log.Debug("PunchService: Updated state from {EntryCount} entries, state: {State}", 
            entryCount, _punchState == true ? "clocked in" : "clocked out");
        
        UpdateButtonState();
        RaiseStateChanged();
    }

    public void Reset()
    {
        _punchState = null;
        _isPunchInProgress = false;
        UpdateButtonState();
        RaiseStateChanged();
    }

    private void UpdateButtonState()
    {
        if (_isPunchInProgress)
        {
            _isKommenEnabled = false;
            _isGehenEnabled = false;
            return;
        }

        switch (_punchState)
        {
            case true:
                // Currently clocked in -> GEHEN is the expected action
                _isKommenEnabled = false;
                _isGehenEnabled = true;
                break;

            case false:
                // Currently clocked out -> KOMMEN is the expected action
                _isKommenEnabled = true;
                _isGehenEnabled = false;
                break;

            default:
                // Unknown state -> both disabled
                _isKommenEnabled = false;
                _isGehenEnabled = false;
                break;
        }
    }

    private void RaiseStateChanged()
    {
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
