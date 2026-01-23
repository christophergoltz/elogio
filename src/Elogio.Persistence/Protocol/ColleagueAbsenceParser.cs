using System.Text.RegularExpressions;
using Elogio.Persistence.Dto;

namespace Elogio.Persistence.Protocol;

/// <summary>
/// Parser for CalendrierGroupeBWT (colleague absence calendar) GWT-RPC responses.
/// Extracts absence data for colleagues from the Kelio group calendar.
///
/// Data Format (from /open/da endpoint):
/// - String table at start (count + quoted strings)
/// - Numeric payload with employee blocks
/// - Block marker: 3, {blockIndex}, {typeIndex}, 62, {dayCount}
/// - Day list: 62, {dayCount}, followed by cells
/// - Cell boundary: 64
/// - Absence color: -15026469 (turquoise for approved absence)
///
/// IMPORTANT - Index Offsets:
/// - Employee index = blockIndex + 2 (the data belongs to the employee 2 positions ahead in string table)
/// - Calendar day = API day + 1 (the API uses 0-based days internally)
///
/// Special Cases Handled:
/// - Badge numbers: Numeric strings (e.g., "1598") following employee names are treated
///   as aliases and mapped back to the preceding employee.
/// - Linked employees: When the type index references another employee (instead of a
///   department), that employee is added to results (with empty absences if no data block).
/// - System accounts: Names without ID (e.g., "Kelio Entwickler") are also recognized.
/// </summary>
public partial class ColleagueAbsenceParser
{
    private const int AbsenceColor = -15026469; // Turquoise color for approved absence

    /// <summary>
    /// Parse a GWT-RPC response containing colleague absence calendar data.
    /// </summary>
    /// <param name="gwtRpcData">The raw GWT-RPC data from bwpContent</param>
    /// <param name="targetMonth">The month to extract (1-12)</param>
    /// <param name="targetYear">The year</param>
    /// <returns>List of colleague absences, or empty list if parsing fails</returns>
    public List<ColleagueAbsenceDto> Parse(string gwtRpcData, int targetMonth, int targetYear)
    {
        if (string.IsNullOrEmpty(gwtRpcData))
            return [];

        try
        {
            // Parse string table
            var stringTable = ParseStringTable(gwtRpcData);
            if (stringTable.Count == 0)
                return [];

            // Extract numeric tokens
            var tokens = ExtractNumericTokens(gwtRpcData);
            if (tokens.Count == 0)
                return [];

            // Find employee name indices in string table
            var employeeIndices = FindEmployeeNameIndices(stringTable);

            // Find day count for the month
            var daysInMonth = DateTime.DaysInMonth(targetYear, targetMonth);

            // Find all day-list positions (62, {daysInMonth})
            var dayListPositions = FindDayListPositions(tokens, daysInMonth);

            // Find employee markers and map to day lists
            var employeeMarkers = FindEmployeeMarkers(tokens, employeeIndices, daysInMonth);

            // Extract absences for each employee
            var results = new List<ColleagueAbsenceDto>();
            var addedEmployees = new HashSet<string>();

            foreach (var (markerPos, nameIndex, name, linkedEmployeeIndex) in employeeMarkers)
            {
                // Find the day list that follows this marker
                var dayListPos = FindNextDayList(dayListPositions, markerPos);
                if (dayListPos < 0)
                    continue;

                // Find the end position (next marker or next day list)
                var endPos = FindEndPosition(tokens, dayListPos, dayListPositions, employeeMarkers.Select(m => m.Position).ToList());

                // Extract absence days from this segment
                var absenceDays = ExtractAbsenceDays(tokens, dayListPos, endPos, daysInMonth);

                // Avoid duplicates (same employee might appear via name and via badge)
                if (!addedEmployees.Contains(name))
                {
                    results.Add(new ColleagueAbsenceDto
                    {
                        Name = name,
                        EmployeeId = ExtractEmployeeId(name),
                        AbsenceDays = absenceDays,
                        Month = targetMonth,
                        Year = targetYear
                    });
                    addedEmployees.Add(name);
                }

                // If this marker has a linked employee (in type index), add them too
                // They won't have a separate data block, so their absences are empty
                if (linkedEmployeeIndex.HasValue &&
                    employeeIndices.TryGetValue(linkedEmployeeIndex.Value, out var linkedName) &&
                    !addedEmployees.Contains(linkedName))
                {
                    results.Add(new ColleagueAbsenceDto
                    {
                        Name = linkedName,
                        EmployeeId = ExtractEmployeeId(linkedName),
                        AbsenceDays = [], // Linked employee has no separate data block
                        Month = targetMonth,
                        Year = targetYear
                    });
                    addedEmployees.Add(linkedName);
                }
            }

            return results;
        }
        catch (Exception)
        {
            // Return empty list on any parsing error
            return [];
        }
    }

    /// <summary>
    /// Parse the string table from GWT-RPC data.
    /// Format: {count},"string1","string2",...
    /// </summary>
    private static List<string> ParseStringTable(string data)
    {
        var strings = new List<string>();

        // First, find the count
        var commaIndex = data.IndexOf(',');
        if (commaIndex < 0 || !int.TryParse(data[..commaIndex], out var count))
            return strings;

        // Extract quoted strings
        var pos = commaIndex + 1;
        while (strings.Count < count && pos < data.Length)
        {
            // Skip whitespace
            while (pos < data.Length && char.IsWhiteSpace(data[pos]))
                pos++;

            if (pos >= data.Length || data[pos] != '"')
                break;

            // Read quoted string
            pos++; // Skip opening quote
            var sb = new System.Text.StringBuilder();
            while (pos < data.Length && data[pos] != '"')
            {
                if (data[pos] == '\\' && pos + 1 < data.Length)
                {
                    pos++;
                    sb.Append(data[pos] switch
                    {
                        'n' => '\n',
                        'r' => '\r',
                        't' => '\t',
                        _ => data[pos]
                    });
                }
                else
                {
                    sb.Append(data[pos]);
                }
                pos++;
            }

            strings.Add(sb.ToString());
            pos++; // Skip closing quote

            // Skip comma
            while (pos < data.Length && (data[pos] == ',' || char.IsWhiteSpace(data[pos])))
                pos++;
        }

        return strings;
    }

    /// <summary>
    /// Extract numeric tokens from the data (after string table).
    /// </summary>
    private static List<int> ExtractNumericTokens(string data)
    {
        // Find the last quote (end of string table)
        var lastQuote = data.LastIndexOf('"');
        if (lastQuote < 0)
            return [];

        var numericPart = data[(lastQuote + 2)..]; // Skip quote and comma
        var tokens = new List<int>();

        foreach (var part in numericPart.Split(','))
        {
            var trimmed = part.Trim();
            if (int.TryParse(trimmed, out var value))
            {
                tokens.Add(value);
            }
        }

        return tokens;
    }

    /// <summary>
    /// Find indices of employee names in the string table.
    /// Employee names match either:
    /// - "Name (ID)" like "Goltz Christopher (14)"
    /// - "Name" like "Kelio Entwickler" (system accounts without ID)
    /// Also tracks badge numbers (numeric strings) that follow employee names.
    /// </summary>
    private static Dictionary<int, string> FindEmployeeNameIndices(List<string> stringTable)
    {
        var result = new Dictionary<int, string>();
        var employeeWithIdPattern = EmployeeNamePattern();
        var employeeWithoutIdPattern = EmployeeNameWithoutIdPattern();
        var badgeNumberPattern = BadgeNumberPattern();

        string? lastEmployeeName = null;

        for (var i = 0; i < stringTable.Count; i++)
        {
            var str = stringTable[i];
            if (employeeWithIdPattern.IsMatch(str) || employeeWithoutIdPattern.IsMatch(str))
            {
                // GWT-RPC uses 1-based indexing
                result[i + 1] = str;
                lastEmployeeName = str;
            }
            // Check if this is a badge number (numeric string like "1598")
            // that follows an employee - associate it with the previous employee
            else if (badgeNumberPattern.IsMatch(str) && lastEmployeeName != null)
            {
                // This badge number maps to the previous employee
                result[i + 1] = lastEmployeeName;
                // Don't update lastEmployeeName - only actual names should be tracked
            }
            else
            {
                // Reset tracking when we hit a non-employee, non-badge string
                lastEmployeeName = null;
            }
        }

        return result;
    }

    /// <summary>
    /// Find all positions of day lists (62, {daysInMonth}).
    /// </summary>
    private static List<int> FindDayListPositions(List<int> tokens, int daysInMonth)
    {
        var positions = new List<int>();

        for (var i = 0; i < tokens.Count - 1; i++)
        {
            if (tokens[i] == 62 && tokens[i + 1] == daysInMonth)
            {
                positions.Add(i);
            }
        }

        return positions;
    }

    /// <summary>
    /// Find employee data blocks in token stream.
    /// Pattern: 3, {blockIndex}, {typeIndex}, 62, {dayCount}
    ///
    /// IMPORTANT: The actual employee is at string index = blockIndex + 2
    /// This offset was discovered through analysis: block labels are 2 positions
    /// behind the actual employee data they contain.
    /// </summary>
    private static List<(int Position, int EmployeeIndex, string Name, int? LinkedEmployeeIndex)> FindEmployeeMarkers(
        List<int> tokens,
        Dictionary<int, string> employeeIndices,
        int daysInMonth)
    {
        var markers = new List<(int, int, string, int?)>();

        for (var i = 0; i < tokens.Count - 4; i++)
        {
            // Pattern: 3, {blockIdx}, {typeIdx}, 62, {daysInMonth}
            // The 62, {days} immediately after confirms this is a real data block header
            if (tokens[i] == 3 &&
                tokens[i + 3] == 62 &&
                tokens[i + 4] == daysInMonth)
            {
                var blockIndex = tokens[i + 1];
                // The actual employee is 2 positions ahead in the string table
                var employeeIndex = blockIndex + 2;

                // Check if this maps to a known employee
                if (employeeIndices.TryGetValue(employeeIndex, out var name))
                {
                    // Check if the type index (position i+2) is also an employee
                    // This happens when one employee's marker references another (e.g., Rosenblatt -> Taube)
                    int? linkedEmployee = null;
                    var typeIndex = tokens[i + 2];
                    // Also apply +2 offset to linked employee
                    if (employeeIndices.ContainsKey(typeIndex + 2))
                    {
                        linkedEmployee = typeIndex + 2;
                    }

                    markers.Add((i, employeeIndex, name, linkedEmployee));
                }
            }
        }

        return markers;
    }

    /// <summary>
    /// Find the first day list position after the given marker position.
    /// </summary>
    private static int FindNextDayList(List<int> dayListPositions, int afterPosition)
    {
        foreach (var pos in dayListPositions)
        {
            if (pos > afterPosition)
                return pos;
        }
        return -1;
    }

    /// <summary>
    /// Find the end position for a segment (next marker or next day list).
    /// </summary>
    private static int FindEndPosition(
        List<int> tokens,
        int startPos,
        List<int> dayListPositions,
        List<int> markerPositions)
    {
        var nextDayList = dayListPositions.FirstOrDefault(p => p > startPos);
        var nextMarker = markerPositions.FirstOrDefault(p => p > startPos);

        if (nextDayList > 0 && nextMarker > 0)
            return Math.Min(nextDayList, nextMarker);
        if (nextDayList > 0)
            return nextDayList;
        if (nextMarker > 0)
            return nextMarker;

        return Math.Min(startPos + 500, tokens.Count);
    }

    /// <summary>
    /// Extract absence days from a token segment.
    /// Note: API days are 0-based internally, so we add +1 to get calendar days.
    /// </summary>
    private static List<int> ExtractAbsenceDays(List<int> tokens, int startPos, int endPos, int maxDays)
    {
        var absenceDays = new HashSet<int>();
        var currentDay = 0;

        for (var i = startPos; i < endPos && i < tokens.Count; i++)
        {
            if (tokens[i] == 64) // Cell boundary
            {
                currentDay++;
            }
            else if (tokens[i] == AbsenceColor && currentDay > 0 && currentDay <= maxDays)
            {
                // Add +1 to convert from API day (0-based) to calendar day (1-based)
                var calendarDay = currentDay + 1;
                if (calendarDay <= maxDays)
                {
                    absenceDays.Add(calendarDay);
                }
            }
        }

        return absenceDays.OrderBy(d => d).ToList();
    }

    /// <summary>
    /// Extract employee ID from name string like "Goltz Christopher (14)".
    /// </summary>
    private static int? ExtractEmployeeId(string name)
    {
        var match = EmployeeIdPattern().Match(name);
        if (match.Success && int.TryParse(match.Groups[1].Value, out var id))
        {
            return id;
        }
        return null;
    }

    // Regex patterns
    [GeneratedRegex(@"^[A-Za-zäöüÄÖÜß\s]+ \(\d+\)$")]
    private static partial Regex EmployeeNamePattern();

    // Pattern for system accounts without ID (e.g., "Kelio Entwickler")
    // Must be exactly two words, each starting with uppercase, followed by lowercase
    // This avoids matching German phrases like "Abwesenheit genehmigt"
    [GeneratedRegex(@"^[A-ZÄÖÜ][a-zäöüß]+\s+[A-ZÄÖÜ][a-zäöüß]+$")]
    private static partial Regex EmployeeNameWithoutIdPattern();

    // Pattern for badge numbers (3-5 digit numbers like "1598")
    // These appear after employee names and serve as alternative identifiers
    [GeneratedRegex(@"^\d{3,5}$")]
    private static partial Regex BadgeNumberPattern();

    [GeneratedRegex(@"\((\d+)\)$")]
    private static partial Regex EmployeeIdPattern();
}
