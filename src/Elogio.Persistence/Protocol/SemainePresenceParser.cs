using System.Text.RegularExpressions;
using Elogio.Persistence.Dto;
using Serilog;

namespace Elogio.Persistence.Protocol;

/// <summary>
/// Parser for SemainePresenceBWT (weekly presence) GWT-RPC responses.
/// The response contains per-day worked/expected times in seconds format.
/// </summary>
public partial class SemainePresenceParser
{
    private readonly GwtRpcTokenizer _tokenizer = new();

    /// <summary>
    /// Parse a decoded GWT-RPC response containing SemainePresenceBWT data.
    /// </summary>
    public WeekPresenceDto? Parse(string gwtRpcData)
    {
        var message = _tokenizer.Tokenize(gwtRpcData);

        if (!message.IsResponse || message.ResponseType == null)
        {
            return null;
        }

        // Check if this is a SemainePresenceBWT response
        if (!message.ResponseType.Contains("SemainePresenceBWT"))
        {
            return null;
        }

        return ParseSemainePresence(message, gwtRpcData);
    }

    private static WeekPresenceDto ParseSemainePresence(GwtRpcMessage message, string rawData)
    {
        // Find employee name in string table
        var employeeName = FindEmployeeName(message);

        // Find dates in data tokens (patterns like 20260105)
        var dates = FindDates(message);

        // Find the BDureeHeure type index in string table (varies per response!)
        var bDureeHeureIndex = FindTypeIndex(message.StringTable, "BDureeHeure");
        Log.Debug("ParseSemainePresence: BDureeHeure type index = {Index}", bDureeHeureIndex);

        // Extract per-day worked/expected seconds from data tokens
        // Pattern: {typeIndex},0,{worked_seconds},{typeIndex},0,{expected_seconds} for each day
        var dailyTimes = ExtractDailyTimesFromSeconds(rawData, bDureeHeureIndex);

        // Extract badge entries (clock in/out times)
        var badgeEntriesByDate = ExtractBadgeEntries(message, rawData, dates);

        // Build day entries
        var days = BuildDayEntries(dates, dailyTimes, badgeEntriesByDate);

        // Calculate totals from dailyTimes
        var totalWorked = dailyTimes.Sum(d => d.WorkedSeconds);
        var totalExpected = dailyTimes.Sum(d => d.ExpectedSeconds);

        // Calculate week start from first date
        var weekStart = dates.Count > 0 ? dates[0] : DateOnly.FromDateTime(DateTime.Today);

        return new WeekPresenceDto
        {
            EmployeeName = employeeName ?? "Unknown",
            WeekStartDate = weekStart,
            Days = days,
            TotalWorked = TimeSpan.FromSeconds(totalWorked),
            TotalExpected = TimeSpan.FromSeconds(totalExpected)
        };
    }

    private static string? FindEmployeeName(GwtRpcMessage message)
    {
        // Look for a string that looks like a name (contains space, doesn't look like a class name)
        foreach (var str in message.StringTable)
        {
            if (str.Contains(' ') &&
                !str.Contains('.') &&
                !str.Contains('/') &&
                !str.StartsWith("ARRAY") &&
                !str.Contains("com.bodet") &&
                str.Length > 3 &&
                str.Length < 100 &&
                char.IsUpper(str[0]))
            {
                // Check if it could be a name (at least two parts)
                var parts = str.Split(' ');
                if (parts.Length >= 2 && parts.All(p => p.Length > 0 && char.IsUpper(p[0])))
                {
                    return str;
                }
            }
        }
        return null;
    }

    private static List<DateOnly> FindDates(GwtRpcMessage message)
    {
        var dates = new List<DateOnly>();

        foreach (var token in message.DataTokens)
        {
            if (token.Type == GwtRpcTokenType.Integer && token.TryAsInt(out var value))
            {
                // Check if it looks like a date (20200101 - 20301231)
                if (value >= 20200101 && value <= 20301231)
                {
                    try
                    {
                        var date = KelioTimeHelpers.ParseDate(value);
                        if (!dates.Contains(date))
                        {
                            dates.Add(date);
                        }
                    }
                    catch
                    {
                        // Not a valid date
                    }
                }
            }
        }

        dates.Sort();
        return dates;
    }

    /// <summary>
    /// Find the index of a type in the string table.
    /// </summary>
    private static int FindTypeIndex(string[] stringTable, string typeName)
    {
        for (int i = 0; i < stringTable.Length; i++)
        {
            if (stringTable[i].Contains(typeName))
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>
    /// Extract daily worked/expected times from raw response.
    /// The data contains patterns like: {typeIndex},0,{seconds} repeated 5 times per day.
    /// Structure per day: [val0, val1, val2, workedSeconds, expectedSeconds]
    /// After 7 days, there's a weekly total entry with [worked_total, expected_total]
    /// </summary>
    private static List<(int WorkedSeconds, int ExpectedSeconds)> ExtractDailyTimesFromSeconds(string rawData, int bDureeHeureIndex)
    {
        var dailyTimes = new List<(int, int)>();

        // If type index not found, fall back to common indices
        if (bDureeHeureIndex < 0)
        {
            Log.Warning("ExtractDailyTimesFromSeconds: BDureeHeure type not found in string table, trying fallback");
            bDureeHeureIndex = 15; // Common default
        }

        // Find all "{typeIndex},0,{number}" patterns (BDureeHeure type with seconds value)
        var pattern = new Regex($@"{bDureeHeureIndex},0,(\d+)");
        var matches = pattern.Matches(rawData);

        var secondsValues = new List<int>();
        foreach (Match m in matches)
        {
            if (int.TryParse(m.Groups[1].Value, out var seconds))
            {
                secondsValues.Add(seconds);
            }
        }

        Log.Debug("ExtractDailyTimesFromSeconds: Found {Count} time values", secondsValues.Count);

        // Each day has 5 values: [val0, val1, val2, worked, expected]
        // Find the first block of 5 values where the last 2 look like daily hours
        const int valuesPerDay = 5;

        // Find start index - look for pattern where val[3] and val[4] are reasonable daily times
        int startIndex = 0;
        bool foundStart = false;
        for (int i = 0; i <= secondsValues.Count - valuesPerDay; i++)
        {
            var potentialWorked = secondsValues[i + 3];
            var potentialExpected = secondsValues[i + 4];

            // Expected time should be a typical work day: 0 for weekends, or 5-10 hours for work days
            // Common values: 0, 25200 (7h), 27000 (7.5h), 28800 (8h), etc.
            // Also check that first 3 values are small (usually 0)
            var isReasonableExpected = potentialExpected == 0 ||
                (potentialExpected >= 18000 && potentialExpected <= 36000); // 5-10 hours

            if (isReasonableExpected &&
                potentialWorked >= 0 && potentialWorked <= 50400 &&
                secondsValues[i] <= 1000 && secondsValues[i + 1] <= 1000 && secondsValues[i + 2] <= 1000)
            {
                startIndex = i;
                foundStart = true;
                break;
            }
        }

        // If no valid start found, try a fallback: look for any block where val[4] is non-zero
        // This handles cases where expected time values are unusual
        if (!foundStart)
        {
            for (int i = 0; i <= secondsValues.Count - (valuesPerDay * 7); i++)
            {
                // Check if there's a consistent pattern of 7 days with 5 values each
                var hasValidPattern = true;
                var nonZeroExpectedCount = 0;

                for (int day = 0; day < 7 && hasValidPattern; day++)
                {
                    var idx = i + (day * valuesPerDay);
                    var worked = secondsValues[idx + 3];
                    var expected = secondsValues[idx + 4];

                    // Worked and expected should be reasonable
                    if (worked < 0 || worked > 86400 || expected < 0 || expected > 86400)
                    {
                        hasValidPattern = false;
                    }

                    if (expected > 0)
                    {
                        nonZeroExpectedCount++;
                    }
                }

                // Valid pattern: at least some days have non-zero expected time
                if (hasValidPattern && nonZeroExpectedCount >= 3)
                {
                    startIndex = i;
                    Log.Debug("ExtractDailyTimesFromSeconds: Found start via fallback at index {Index}", startIndex);
                    break;
                }
            }
        }

        Log.Debug("ExtractDailyTimesFromSeconds: Using startIndex={StartIndex}, foundStart={FoundStart}, totalValues={Count}",
            startIndex, foundStart, secondsValues.Count);

        // Extract 7 days of data
        for (int day = 0; day < 7; day++)
        {
            int idx = startIndex + (day * valuesPerDay);

            if (idx + 4 < secondsValues.Count)
            {
                var worked = secondsValues[idx + 3];
                var expected = secondsValues[idx + 4];

                // Sanity check: daily values should be 0-14 hours
                if (worked >= 0 && worked <= 50400 && expected >= 0 && expected <= 50400)
                {
                    dailyTimes.Add((worked, expected));
                }
                else
                {
                    dailyTimes.Add((0, 0));
                }
            }
            else
            {
                dailyTimes.Add((0, 0));
            }
        }

        // Log the extracted daily times for debugging
        var totalWorked = dailyTimes.Sum(d => d.Item1);
        var totalExpected = dailyTimes.Sum(d => d.Item2);
        Log.Debug("ExtractDailyTimesFromSeconds: Extracted {Days} days, totalWorked={Worked}s ({WorkedH:F1}h), totalExpected={Expected}s ({ExpectedH:F1}h)",
            dailyTimes.Count, totalWorked, totalWorked / 3600.0, totalExpected, totalExpected / 3600.0);

        return dailyTimes;
    }

    private static List<DayPresenceDto> BuildDayEntries(
        List<DateOnly> dates,
        List<(int WorkedSeconds, int ExpectedSeconds)> dailyTimes,
        Dictionary<DateOnly, List<TimeEntryDto>> badgeEntriesByDate)
    {
        var days = new List<DayPresenceDto>();

        for (int i = 0; i < 7; i++)
        {
            var date = i < dates.Count
                ? dates[i]
                : dates.Count > 0
                    ? dates[0].AddDays(i)
                    : DateOnly.FromDateTime(DateTime.Today).AddDays(i);
            var (workedSeconds, expectedSeconds) = i < dailyTimes.Count ? dailyTimes[i] : (0, 0);

            // Get badge entries for this date if available
            var entries = badgeEntriesByDate.TryGetValue(date, out var badgeEntries)
                ? badgeEntries
                : [];

            days.Add(new DayPresenceDto
            {
                Date = date,
                DayOfWeek = date.DayOfWeek.ToString(),
                Entries = entries,
                WorkedTime = TimeSpan.FromSeconds(workedSeconds),
                ExpectedTime = TimeSpan.FromSeconds(expectedSeconds)
            });
        }

        return days;
    }

    /// <summary>
    /// Extract badge entries (clock in/out times) from the response.
    /// Badge times are stored as BHeure72 values - the value represents minutes from midnight.
    /// Pattern in data: after a date (YYYYMMDD), look for time references followed by minute values.
    /// </summary>
    private static Dictionary<DateOnly, List<TimeEntryDto>> ExtractBadgeEntries(
        GwtRpcMessage message,
        string rawData,
        List<DateOnly> dates)
    {
        var result = new Dictionary<DateOnly, List<TimeEntryDto>>();

        // Find the string index for BHeure72 type
        var bHeure72Index = -1;
        for (int i = 0; i < message.StringTable.Length; i++)
        {
            if (message.StringTable[i].Contains("BHeure72"))
            {
                bHeure72Index = i;
                break;
            }
        }

        if (bHeure72Index < 0 || dates.Count == 0)
        {
            return result;
        }

        // Look for pattern: {date},{bHeure72Index},{minutes}
        // The badge times appear after dates in the data section
        var pattern = BadgeTimePattern();
        var matches = pattern.Matches(rawData);

        // Collect all badge times from the response
        var allBadgeTimes = new List<(DateOnly Date, TimeOnly Time)>();
        DateOnly? currentDate = null;

        foreach (Match match in matches)
        {
            if (int.TryParse(match.Groups[1].Value, out var value))
            {
                // Check if this is a date
                if (value >= 20200101 && value <= 20301231)
                {
                    try
                    {
                        currentDate = KelioTimeHelpers.ParseDate(value);
                    }
                    catch
                    {
                        // Not a valid date
                    }
                }
                // Check if this looks like a time in minutes (0-1440 = 24 hours)
                else if (value >= 0 && value <= 1440 && currentDate.HasValue)
                {
                    // Check if this is preceded by the BHeure72 type reference
                    var precedingText = rawData[..match.Index];
                    if (precedingText.EndsWith($"{bHeure72Index},") ||
                        precedingText.EndsWith($",{bHeure72Index},"))
                    {
                        var time = TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(value));
                        allBadgeTimes.Add((currentDate.Value, time));
                    }
                }
            }
        }

        // Alternative: Look for the specific pattern with BadgeageLightBWT
        // Pattern: date reference, then BHeure72 with time value
        var altPattern = AltBadgeTimePattern(bHeure72Index);
        var altMatches = altPattern.Matches(rawData);

        foreach (Match match in altMatches)
        {
            if (int.TryParse(match.Groups[1].Value, out var minutes) && minutes >= 0 && minutes <= 1440)
            {
                // Find the closest date before this match
                var closestDate = FindClosestDate(rawData, match.Index, dates);
                if (closestDate.HasValue)
                {
                    var time = TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(minutes));
                    if (!allBadgeTimes.Any(bt => bt.Date == closestDate.Value && bt.Time == time))
                    {
                        allBadgeTimes.Add((closestDate.Value, time));
                    }
                }
            }
        }

        // Group by date and create TimeEntryDto objects
        foreach (var dateGroup in allBadgeTimes.GroupBy(bt => bt.Date))
        {
            var entries = new List<TimeEntryDto>();
            var times = dateGroup.OrderBy(bt => bt.Time).ToList();

            // Alternate between BadgeIn and BadgeOut
            for (int i = 0; i < times.Count; i++)
            {
                entries.Add(new TimeEntryDto
                {
                    Time = times[i].Time,
                    Type = i % 2 == 0 ? TimeEntryType.BadgeIn : TimeEntryType.BadgeOut
                });
            }

            result[dateGroup.Key] = entries;
        }

        return result;
    }

    private static DateOnly? FindClosestDate(string rawData, int position, List<DateOnly> dates)
    {
        // Look backwards in the raw data for a date pattern
        var searchText = rawData[..position];
        DateOnly? closest = null;
        int closestPos = -1;

        foreach (var date in dates)
        {
            var dateStr = $"{date.Year}{date.Month:D2}{date.Day:D2}";
            var pos = searchText.LastIndexOf(dateStr, StringComparison.Ordinal);
            if (pos > closestPos)
            {
                closestPos = pos;
                closest = date;
            }
        }

        return closest;
    }

    [GeneratedRegex(@",(\d+)")]
    private static partial Regex BadgeTimePattern();

    private static Regex AltBadgeTimePattern(int bHeure72Index) =>
        new($@"{bHeure72Index},(\d+)", RegexOptions.Compiled);
}
