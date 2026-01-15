using System.Text.RegularExpressions;
using Elogio.Persistence.Dto;

namespace Elogio.Persistence.Protocol;

/// <summary>
/// Parser for CalendrierDemandesDataBWT (absence calendar) GWT-RPC responses.
/// Extracts vacation, sick leave, holidays, and other absence data.
/// </summary>
public partial class AbsenceCalendarParser
{
    private readonly GwtRpcTokenizer _tokenizer = new();

    /// <summary>
    /// Parse a decoded GWT-RPC response containing absence calendar data.
    /// </summary>
    public AbsenceCalendarDto? Parse(string gwtRpcData, int employeeId, DateOnly startDate, DateOnly endDate)
    {
        if (string.IsNullOrEmpty(gwtRpcData))
            return null;

        var message = _tokenizer.Tokenize(gwtRpcData);

        if (!message.IsResponse || message.ResponseType == null)
            return null;

        // Check if this is a CalendrierDemandesDataBWT response
        if (!message.ResponseType.Contains("CalendrierDemandesDataBWT"))
            return null;

        return ParseCalendarData(message, gwtRpcData, employeeId, startDate, endDate);
    }

    private AbsenceCalendarDto ParseCalendarData(
        GwtRpcMessage message,
        string rawData,
        int employeeId,
        DateOnly startDate,
        DateOnly endDate)
    {
        var days = new List<AbsenceDayDto>();
        var legend = ParseLegend(message, rawData);

        // Find all day entries in the raw data
        // Pattern: 5,{DATE},6,{dayData},...,3,{isHoliday},3,{isWeekend},3,{isRestDay}
        var dayEntries = ExtractDayEntries(rawData, message.StringTable);

        foreach (var entry in dayEntries)
        {
            days.Add(entry);
        }

        // Sort by date
        days = days.OrderBy(d => d.Date).ToList();

        return new AbsenceCalendarDto
        {
            EmployeeId = employeeId,
            StartDate = startDate,
            EndDate = endDate,
            Days = days,
            Legend = legend
        };
    }

    private List<AbsenceDayDto> ExtractDayEntries(string rawData, string[] stringTable)
    {
        var days = new List<AbsenceDayDto>();
        var processedDates = new HashSet<DateOnly>();

        // Find BDate type index in string table
        var bDateIndex = Array.FindIndex(stringTable, s => s.Contains("BDate"));

        // Pattern to find day entries: 5,{DATE},6,...
        // Where 5 is the index for java.util.Map and DATE is YYYYMMDD format
        var datePattern = DateEntryPattern();
        var matches = datePattern.Matches(rawData);
        var matchList = matches.Cast<Match>().ToList();

        for (var i = 0; i < matchList.Count; i++)
        {
            var match = matchList[i];
            if (!int.TryParse(match.Groups[1].Value, out var dateInt))
                continue;

            // Validate date range (reasonable bounds to filter false positives)
            if (dateInt < 20000101 || dateInt > 20991231)
                continue;

            DateOnly date;
            try
            {
                date = KelioTimeHelpers.ParseDate(dateInt);
            }
            catch
            {
                continue;
            }

            // Skip if already processed
            if (processedDates.Contains(date))
                continue;

            processedDates.Add(date);

            // Extract the day data context - use position of next date entry as boundary
            var startIdx = match.Index;
            int endIdx;

            // Find the next valid date match to use as boundary
            var foundNextBoundary = false;
            for (var j = i + 1; j < matchList.Count; j++)
            {
                if (int.TryParse(matchList[j].Groups[1].Value, out var nextDateInt) &&
                    nextDateInt >= 20000101 && nextDateInt <= 20991231)
                {
                    endIdx = matchList[j].Index;
                    foundNextBoundary = true;
                    var dayContext = rawData[startIdx..endIdx];
                    var dayDto = ParseDayEntry(date, dayContext, stringTable);
                    days.Add(dayDto);
                    break;
                }
            }

            // If no next date found, use remaining data (max 200 chars)
            if (!foundNextBoundary)
            {
                endIdx = Math.Min(rawData.Length, startIdx + 200);
                var dayContext = rawData[startIdx..endIdx];
                var dayDto = ParseDayEntry(date, dayContext, stringTable);
                days.Add(dayDto);
            }
        }

        return days;
    }

    private static AbsenceDayDto ParseDayEntry(DateOnly date, string dayContext, string[] stringTable)
    {
        // Default values
        var type = AbsenceType.None;
        var isHoliday = false;
        var isWeekend = false;
        var isRestDay = false;
        var isHalfHoliday = false;
        int? colorValue = null;
        string? motifId = null;

        // Look for boolean flags FIRST: 3,{isHoliday},3,{isWeekend},3,{isRestDay}
        // These appear as: 3,1 (true) or 3,0 (false)
        var flagsMatch = BooleanFlagsPattern().Match(dayContext);
        if (flagsMatch.Success)
        {
            isHoliday = flagsMatch.Groups[1].Value == "1";
            isWeekend = flagsMatch.Groups[2].Value == "1";
            isRestDay = flagsMatch.Groups[3].Value == "1";
        }

        // Find ALL colors in day context to detect multiple absence types
        var colorMatches = ColorPattern().Matches(dayContext);
        var foundColors = new HashSet<int>();
        foreach (Match m in colorMatches)
        {
            if (int.TryParse(m.Groups[1].Value, out var c))
                foundColors.Add(c);
        }

        // Check if half holiday color is present
        if (foundColors.Contains(AbsenceTypeHelper.ColorHalfHoliday))
        {
            isHalfHoliday = true;
        }

        // Determine type based on flags FIRST (weekends override all)
        if (isWeekend)
        {
            type = AbsenceType.Weekend;
        }
        else
        {
            // Look for absence colors (vacation, sick leave, private appointment)
            foreach (var color in foundColors)
            {
                var colorType = AbsenceTypeHelper.FromColor(color);

                // Prioritize vacation/sick/private over holiday colors
                if (colorType is AbsenceType.Vacation or AbsenceType.SickLeave or AbsenceType.PrivateAppointment)
                {
                    colorValue = color;
                    type = colorType;

                    // Try to extract motif ID (appears before the color)
                    var motifMatch = MotifPattern().Match(dayContext);
                    if (motifMatch.Success && int.TryParse(motifMatch.Groups[1].Value, out var motifIdx))
                    {
                        if (motifIdx >= 0 && motifIdx < stringTable.Length)
                        {
                            motifId = stringTable[motifIdx];
                        }
                    }
                    break; // Found primary absence type
                }
            }

            // If no vacation/sick/private, check for half holiday
            if (type == AbsenceType.None && isHalfHoliday)
            {
                colorValue = AbsenceTypeHelper.ColorHalfHoliday;
                type = AbsenceType.HalfHoliday;
            }

            // If still nothing, check flags
            if (type == AbsenceType.None)
            {
                if (isHoliday)
                    type = AbsenceType.PublicHoliday;
                else if (isRestDay)
                    type = AbsenceType.RestDay;
            }
        }

        return new AbsenceDayDto
        {
            Date = date,
            Type = type,
            IsPublicHoliday = isHoliday,
            IsWeekend = isWeekend,
            IsRestDay = isRestDay,
            IsHalfHoliday = isHalfHoliday,
            ColorValue = colorValue,
            MotifId = motifId,
            Label = type != AbsenceType.None ? AbsenceTypeHelper.GetLabel(type) : null
        };
    }

    private static List<AbsenceLegendEntryDto> ParseLegend(GwtRpcMessage message, string rawData)
    {
        var legend = new List<AbsenceLegendEntryDto>();

        // Find legend section at the end of response
        // Pattern: 38,8,{motifTypeIdx},10,{color},8,{labelIdx},13,14,0
        var legendPattern = LegendEntryPattern();
        var matches = legendPattern.Matches(rawData);

        foreach (Match match in matches)
        {
            if (!int.TryParse(match.Groups[1].Value, out var colorValue))
                continue;

            if (!int.TryParse(match.Groups[2].Value, out var labelIdx))
                continue;

            // Get label from string table
            var label = labelIdx >= 0 && labelIdx < message.StringTable.Length
                ? message.StringTable[labelIdx]
                : "Unknown";

            // Skip empty labels or type names
            if (string.IsNullOrEmpty(label) || label.Contains("com.bodet") || label.Contains("ARRAY"))
                continue;

            var type = AbsenceTypeHelper.FromColor(colorValue);

            legend.Add(new AbsenceLegendEntryDto
            {
                ColorValue = colorValue,
                Label = label,
                Type = type
            });
        }

        return legend;
    }

    // Pattern: 5,{DATE},6 where DATE is 8 digits (YYYYMMDD)
    [GeneratedRegex(@"5,(\d{8}),6")]
    private static partial Regex DateEntryPattern();

    // Pattern: 10,{negative_color_value}
    [GeneratedRegex(@"10,(-\d+)")]
    private static partial Regex ColorPattern();

    // Pattern: 8,{motifIdx},10,
    [GeneratedRegex(@"8,(\d+),10,")]
    private static partial Regex MotifPattern();

    // Pattern: 3,{0|1},3,{0|1},3,{0|1} for isHoliday, isWeekend, isRestDay
    [GeneratedRegex(@"3,([01]),3,([01]),3,([01])")]
    private static partial Regex BooleanFlagsPattern();

    // Pattern: 38,8,{motifTypeIdx},10,{color},8,{labelIdx},13,14,0
    // Simplified to find color and label index
    [GeneratedRegex(@"38,8,\d+,10,(-?\d+),8,(\d+),13,14,0")]
    private static partial Regex LegendEntryPattern();
}
