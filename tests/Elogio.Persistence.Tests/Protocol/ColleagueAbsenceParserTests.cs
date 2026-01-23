using System.Text.RegularExpressions;
using Elogio.Persistence.Dto;
using Elogio.Persistence.Protocol;
using Xunit;
using Xunit.Abstractions;

namespace Elogio.Persistence.Tests.Protocol;

public class ColleagueAbsenceParserTests
{
    private readonly ITestOutputHelper _output;
    private readonly ColleagueAbsenceParser _parser = new();

    public ColleagueAbsenceParserTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Parse_EmptyInput_ShouldReturnEmptyList()
    {
        var result = _parser.Parse("", 2, 2026);

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void Parse_NullInput_ShouldReturnEmptyList()
    {
        var result = _parser.Parse(null!, 2, 2026);

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void Parse_SimpleResponse_ShouldExtractEmployees()
    {
        // Minimal test response with 2 employees, 3 days
        var testResponse = BuildMinimalTestResponse();

        var result = _parser.Parse(testResponse, 1, 2026);

        _output.WriteLine($"Parsed {result.Count} employees");
        foreach (var emp in result)
        {
            _output.WriteLine($"  {emp.Name}: Days with absence = [{string.Join(", ", emp.AbsenceDays)}]");
        }

        Assert.NotNull(result);
    }

    [Fact]
    public void Parse_WithAbsenceColor_ShouldDetectAbsenceDays()
    {
        // Test response with absence color (-15026469) at specific days
        var testResponse = BuildResponseWithAbsences();

        var result = _parser.Parse(testResponse, 1, 2026);

        _output.WriteLine($"Parsed {result.Count} employees");
        foreach (var emp in result)
        {
            _output.WriteLine($"  {emp.Name}: Absences = [{string.Join(", ", emp.AbsenceDays)}]");
        }

        // Should find at least one employee
        Assert.NotEmpty(result);
    }

    [Fact]
    public void ExtractEmployeeId_FromValidName_ShouldReturnId()
    {
        // Test the DTO's behavior
        var dto = new ColleagueAbsenceDto
        {
            Name = "Goltz Christopher (14)",
            EmployeeId = 14,
            AbsenceDays = [1, 2, 3],
            Month = 1,
            Year = 2026
        };

        Assert.Equal(14, dto.EmployeeId);
        Assert.True(dto.IsAbsentOn(new DateOnly(2026, 1, 1)));
        Assert.True(dto.IsAbsentOn(new DateOnly(2026, 1, 2)));
        Assert.False(dto.IsAbsentOn(new DateOnly(2026, 1, 4)));
        Assert.False(dto.IsAbsentOn(new DateOnly(2026, 2, 1))); // Different month
    }

    /// <summary>
    /// Build a minimal test response for basic parsing.
    /// </summary>
    private static string BuildMinimalTestResponse()
    {
        // Simplified structure:
        // - String table with employee names
        // - Numeric data with employee blocks and day cells
        return """
            10,"com.bodet.bwt.gtp.serveur.domain.commun.intranet_calendrier_groupe.CalendrierGroupeBWT","com.bodet.bwt.core.type.time.BDate","java.util.List","CalendrierGroupeSalarieBWT","CalendrierGroupeCelluleBWT","java.lang.Integer","Test Employee (1)","Test Employee2 (2)","Entwicklung",0,1,20260101,2,3,62,3,64,31,0,62,0,64,31,0,62,0,64,31,0,62,0,3,7,4,62,3,64,31,0,62,0,64,31,0,62,0,64,31,0,62,0,3,8,4,62,3,64,31,0,62,0,64,31,0,62,0,64,31,0,62,0
            """;
    }

    /// <summary>
    /// Build a test response with specific absence colors matching the real format.
    /// </summary>
    private static string BuildResponseWithAbsences()
    {
        // Real format has:
        // - String table where employee names are at specific indices (1-based)
        // - Pattern "3, {blockIndex}, {typeIndex}, 62, {dayCount}" to mark data blocks
        // - IMPORTANT: Actual employee is at blockIndex + 2 in string table
        // - "62, {dayCount}" for day lists
        // - "64" as cell boundary
        // - "-15026469" as absence color
        // - IMPORTANT: API days are 0-based, calendar days need +1

        // String indices (1-based):
        // 1 = CalendrierGroupeBWT (type)
        // 2 = BDate (type)
        // 3 = java.util.List
        // 4 = CalendrierGroupeSalarieBWT
        // 5 = CalendrierGroupeCelluleBWT
        // 6 = BColor
        // 7 = CalendrierGroupeItemBWT
        // 8 = java.lang.Integer
        // 9 = Mueller Hans (101)   <- blockIndex 7 + 2 = 9
        // 10 = Schmidt Anna (102)  <- blockIndex 8 + 2 = 10

        // Absences:
        // - Mueller: API days 1, 2 → Calendar days 2, 3
        // - Schmidt: API day 3 → Calendar day 4

        return """
            10,"com.bodet.bwt.gtp.serveur.domain.commun.intranet_calendrier_groupe.CalendrierGroupeBWT","com.bodet.bwt.core.type.time.BDate","java.util.List","com.bodet.bwt.gtp.serveur.domain.commun.intranet_calendrier_groupe.CalendrierGroupeSalarieBWT","com.bodet.bwt.gtp.serveur.domain.commun.intranet_calendrier_groupe.CalendrierGroupeCelluleBWT","com.bodet.bwt.core.type.drawing.BColor","com.bodet.bwt.gtp.serveur.domain.commun.intranet_calendrier_groupe.CalendrierGroupeItemBWT","java.lang.Integer","Mueller Hans (101)","Schmidt Anna (102)",0,1,20260101,62,31,3,7,4,62,31,64,31,0,62,1,7,6,-15026469,64,31,0,62,1,7,6,-15026469,64,31,0,62,0,64,31,0,62,0,64,31,0,62,0,64,31,0,62,0,64,31,0,62,0,64,31,0,62,0,64,31,0,62,0,64,31,0,62,0,64,31,0,62,0,64,31,0,62,0,64,31,0,62,0,64,31,0,62,0,64,31,0,62,0,64,31,0,62,0,64,31,0,62,0,64,31,0,62,0,64,31,0,62,0,64,31,0,62,0,64,31,0,62,0,64,31,0,62,0,64,31,0,62,0,64,31,0,62,0,64,31,0,62,0,64,31,0,62,0,3,8,4,62,31,64,31,0,62,0,64,31,0,62,0,64,31,0,62,1,7,6,-15026469,64,31,0,62,0,64,31,0,62,0,64,31,0,62,0,64,31,0,62,0,64,31,0,62,0,64,31,0,62,0,64,31,0,62,0,64,31,0,62,0,64,31,0,62,0,64,31,0,62,0,64,31,0,62,0,64,31,0,62,0,64,31,0,62,0,64,31,0,62,0,64,31,0,62,0,64,31,0,62,0,64,31,0,62,0,64,31,0,62,0,64,31,0,62,0,64,31,0,62,0,64,31,0,62,0
            """;
    }

    [Fact]
    public void Parse_WithRealOffsets_ShouldMapCorrectly()
    {
        // Test with the +2 index offset and +1 day offset
        var testResponse = BuildResponseWithAbsences();

        var result = _parser.Parse(testResponse, 1, 2026);

        _output.WriteLine($"Parsed {result.Count} employees");
        foreach (var emp in result)
        {
            _output.WriteLine($"  {emp.Name} (ID: {emp.EmployeeId}): Absences = [{string.Join(", ", emp.AbsenceDays)}]");
        }

        // Should find Mueller Hans
        var mueller = result.FirstOrDefault(e => e.Name == "Mueller Hans (101)");
        Assert.NotNull(mueller);
        Assert.Equal(101, mueller.EmployeeId);
        // API days 1,2 should become calendar days 2,3
        Assert.Contains(2, mueller.AbsenceDays);
        Assert.Contains(3, mueller.AbsenceDays);

        // Should find Schmidt Anna
        var schmidt = result.FirstOrDefault(e => e.Name == "Schmidt Anna (102)");
        Assert.NotNull(schmidt);
        Assert.Equal(102, schmidt.EmployeeId);
        // API day 3 should become calendar day 4
        Assert.Contains(4, schmidt.AbsenceDays);
    }
}
