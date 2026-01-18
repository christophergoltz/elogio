using System.Text;
using Serilog;

namespace Elogio.Persistence.Api.Parsing;

/// <summary>
/// Extracts employee IDs from GWT-RPC responses.
/// This is a pure parser with no external dependencies - only string manipulation.
/// </summary>
public static class EmployeeIdExtractor
{
    /// <summary>
    /// Extract the dynamic employee ID from GlobalBWTService connect response.
    /// The employee ID appears near the END of the data tokens, before the user's name.
    /// Pattern: [..., TYPE_REF, EMPLOYEE_ID, TYPE_REF, FIRSTNAME_IDX, TYPE_REF, LASTNAME_IDX, ...]
    /// </summary>
    /// <param name="responseBody">The raw GWT-RPC response body</param>
    /// <returns>The employee ID, or 0 if extraction failed</returns>
    public static int ExtractFromConnectResponse(string responseBody)
    {
        try
        {
            var parts = responseBody.Split(',');

            // Parse GWT-RPC: first number is string count
            if (!int.TryParse(parts[0], out var stringCount))
                return 0;

            // Extract strings (we need to skip past them to get to data tokens)
            var strings = new List<string>();
            var idx = 1;
            while (idx < parts.Length && strings.Count < stringCount)
            {
                var part = parts[idx];
                if (part.StartsWith("\""))
                {
                    var fullString = new StringBuilder(part[1..]);  // Remove opening quote
                    while (idx < parts.Length && !parts[idx].EndsWith("\""))
                    {
                        idx++;
                        if (idx < parts.Length)
                            fullString.Append(',').Append(parts[idx]);
                    }
                    // Remove closing quote
                    var str = fullString.ToString();
                    if (str.EndsWith("\""))
                        str = str[..^1];
                    strings.Add(str);
                }
                idx++;
            }

            // Get data tokens (after all strings)
            var dataTokens = parts[idx..].Select(p => p.Trim()).ToList();

            // Find the last two name strings (firstname, lastname) - they're at the very end
            // These are strings that look like personal names (capitalized, no dots, etc.)
            var lastNameStrIdx = -1;
            var firstNameStrIdx = -1;

            // Search backwards for two consecutive name-like strings
            for (var i = strings.Count - 1; i >= 1; i--)
            {
                var s = strings[i];
                // Name strings: capitalized, no dots, reasonable length, not type names
                if (!s.Contains('.') && s != "NULL" && !string.IsNullOrWhiteSpace(s) &&
                    s.Length >= 2 && s.Length <= 30 &&
                    !s.Contains("java") && !s.Contains("com") && !s.Contains("ENUM") &&
                    char.IsUpper(s[0]) && !s.All(char.IsUpper)) // Starts with capital but not ALL CAPS
                {
                    if (lastNameStrIdx < 0)
                    {
                        lastNameStrIdx = i;
                    }
                    else
                    {
                        firstNameStrIdx = i;
                        break; // Found both
                    }
                }
            }

            Log.Debug("ExtractEmployeeId: firstName=[{FirstNameIdx}] '{FirstName}', lastName=[{LastNameIdx}] '{LastName}'",
                firstNameStrIdx, firstNameStrIdx >= 0 ? strings[firstNameStrIdx] : "?",
                lastNameStrIdx, lastNameStrIdx >= 0 ? strings[lastNameStrIdx] : "?");

            if (firstNameStrIdx < 0 || lastNameStrIdx < 0)
            {
                Log.Debug("ExtractEmployeeId: Could not find name strings!");
                return 0;
            }

            // Log last 50 data tokens for analysis
            Log.Debug("ExtractEmployeeId: Last 50 data tokens: {Tokens}", string.Join(",", dataTokens.TakeLast(50)));

            // Find pattern: EMPLOYEE_ID, 4, firstNameStrIdx, 4, lastNameStrIdx
            // Search for the firstname index in data tokens, then look backwards for employee ID
            for (var i = dataTokens.Count - 1; i >= 2; i--)
            {
                if (!int.TryParse(dataTokens[i], out var tokenVal))
                    continue;

                // Found firstname index reference
                if (tokenVal == firstNameStrIdx)
                {
                    // Pattern should be: EMPLOYEE_ID, 4, firstNameStrIdx, 4, lastNameStrIdx
                    // So employee ID is at i-2 (loop guarantees i >= 2)
                    if (int.TryParse(dataTokens[i - 1], out var typeRef) && typeRef == 4 &&
                        int.TryParse(dataTokens[i - 2], out var employeeId) &&
                        employeeId > 0 && employeeId <= 99999)
                    {
                        Log.Debug("ExtractEmployeeId: Found employee ID {EmployeeId} before name pattern at pos {Position}",
                            employeeId, i - 2);
                        return employeeId;
                    }
                    else
                    {
                        // Log why it failed for debugging
                        Log.Debug("ExtractEmployeeId: Pattern found but validation failed. i-1={Val1}, i-2={Val2}",
                            dataTokens[i - 1], dataTokens[i - 2]);
                    }
                }
            }

            Log.Debug("ExtractEmployeeId: No valid employee ID found!");
            return 0;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "ExtractEmployeeIdFromConnectResponse error");
            return 0;
        }
    }

    /// <summary>
    /// Extract the REAL employee ID from getParametreIntranet response.
    /// Response format: ...,"Name",0,1,2,3,0,3,3,4,1,3,0,5,6,3,0,3,{employeeId}
    /// The employee ID appears at the very end, preceded by type reference "3".
    /// </summary>
    /// <param name="responseBody">The decoded GWT-RPC response body</param>
    /// <returns>The employee ID, or 0 if extraction failed</returns>
    public static int ExtractFromParametreIntranetResponse(string responseBody)
    {
        try
        {
            // Extract employee ID from the end of the response
            // The pattern is: ,3,{employeeId} at the end (where 3 is the Integer type index)
            var parts = responseBody.Split(',');
            if (parts.Length < 2)
                return 0;

            // The last numeric value is the employee ID
            for (int i = parts.Length - 1; i >= 0; i--)
            {
                if (int.TryParse(parts[i], out var potentialId) && potentialId > 0 && potentialId < 100000)
                {
                    // Verify it's preceded by a type reference (3 for Integer in this context)
                    if (i > 0 && parts[i - 1] == "3")
                    {
                        Log.Debug("Extracted REAL employee ID from getParametreIntranet: {EmployeeId}", potentialId);
                        return potentialId;
                    }
                }
            }

            Log.Debug("ExtractFromParametreIntranetResponse: No valid employee ID found!");
            return 0;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "ExtractFromParametreIntranetResponse error");
            return 0;
        }
    }
}
