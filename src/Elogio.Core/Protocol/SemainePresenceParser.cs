using System.Text.RegularExpressions;
using Elogio.Core.Models;

namespace Elogio.Core.Protocol;

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
    public WeekPresence? Parse(string gwtRpcData)
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

    private WeekPresence? ParseSemainePresence(GwtRpcMessage message, string rawData)
    {
        // Find employee name in string table
        var employeeName = FindEmployeeName(message);

        // Find dates in data tokens (patterns like 20260105)
        var dates = FindDates(message);

        // Extract per-day worked/expected seconds from data tokens
        // Pattern: 15,0,{worked_seconds},15,0,{expected_seconds} for each day
        var dailyTimes = ExtractDailyTimesFromSeconds(rawData);

        // Extract badge entries (clock in/out times)
        var badgeEntriesByDate = ExtractBadgeEntries(message, rawData, dates);

        // Build day entries
        var days = BuildDayEntries(dates, dailyTimes, badgeEntriesByDate);

        // Calculate totals from dailyTimes
        var totalWorked = dailyTimes.Sum(d => d.WorkedSeconds);
        var totalExpected = dailyTimes.Sum(d => d.ExpectedSeconds);

        // Calculate week start from first date
        var weekStart = dates.Count > 0 ? dates[0] : DateOnly.FromDateTime(DateTime.Today);

        return new WeekPresence
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
    /// Extract daily worked/expected times from raw response.
    /// The data contains patterns like: 15,0,{seconds} repeated 5 times per day.
    /// Structure per day: [val0, val1, val2, workedSeconds, expectedSeconds]
    /// After 7 days, there's a weekly total entry with [worked_total, expected_total]
    /// </summary>
    private static List<(int WorkedSeconds, int ExpectedSeconds)> ExtractDailyTimesFromSeconds(string rawData)
    {
        var dailyTimes = new List<(int, int)>();

        // Find all "15,0,{number}" patterns (BDureeHeure type with seconds value)
        var pattern = TimeSecondsPattern();
        var matches = pattern.Matches(rawData);

        var secondsValues = new List<int>();
        foreach (Match m in matches)
        {
            if (int.TryParse(m.Groups[1].Value, out var seconds))
            {
                secondsValues.Add(seconds);
            }
        }

        // Each day has 5 values: [val0, val1, val2, worked, expected]
        // Find the first block of 5 values where the last 2 look like daily hours
        const int valuesPerDay = 5;

        // Find start index - look for pattern where val[3] and val[4] are reasonable daily times
        int startIndex = 0;
        for (int i = 0; i <= secondsValues.Count - valuesPerDay; i++)
        {
            var potentialWorked = secondsValues[i + 3];
            var potentialExpected = secondsValues[i + 4];

            // Expected time should be around 7 hours (25200 seconds) or 0 for weekends
            // Also check that first 3 values are small (usually 0)
            if ((potentialExpected == 25200 || potentialExpected == 0) &&
                potentialWorked >= 0 && potentialWorked <= 50400 &&
                secondsValues[i] <= 1000 && secondsValues[i + 1] <= 1000 && secondsValues[i + 2] <= 1000)
            {
                startIndex = i;
                break;
            }
        }

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

        return dailyTimes;
    }

    private static List<DayPresence> BuildDayEntries(
        List<DateOnly> dates,
        List<(int WorkedSeconds, int ExpectedSeconds)> dailyTimes,
        Dictionary<DateOnly, List<TimeEntry>> badgeEntriesByDate)
    {
        var days = new List<DayPresence>();

        for (int i = 0; i < 7; i++)
        {
            var date = i < dates.Count ? dates[i] : DateOnly.FromDateTime(DateTime.Today).AddDays(i);
            var (workedSeconds, expectedSeconds) = i < dailyTimes.Count ? dailyTimes[i] : (0, 0);

            // Get badge entries for this date if available
            var entries = badgeEntriesByDate.TryGetValue(date, out var badgeEntries)
                ? badgeEntries
                : [];

            days.Add(new DayPresence
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
    private static Dictionary<DateOnly, List<TimeEntry>> ExtractBadgeEntries(
        GwtRpcMessage message,
        string rawData,
        List<DateOnly> dates)
    {
        var result = new Dictionary<DateOnly, List<TimeEntry>>();

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

        // Group by date and create TimeEntry objects
        foreach (var dateGroup in allBadgeTimes.GroupBy(bt => bt.Date))
        {
            var entries = new List<TimeEntry>();
            var times = dateGroup.OrderBy(bt => bt.Time).ToList();

            // Alternate between BadgeIn and BadgeOut
            for (int i = 0; i < times.Count; i++)
            {
                entries.Add(new TimeEntry
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
            var pos = searchText.LastIndexOf(dateStr);
            if (pos > closestPos)
            {
                closestPos = pos;
                closest = date;
            }
        }

        return closest;
    }

    [GeneratedRegex(@"15,0,(\d+)")]
    private static partial Regex TimeSecondsPattern();

    [GeneratedRegex(@",(\d+)")]
    private static partial Regex BadgeTimePattern();

    private static Regex AltBadgeTimePattern(int bHeure72Index) =>
        new($@"{bHeure72Index},(\d+)", RegexOptions.Compiled);
}
