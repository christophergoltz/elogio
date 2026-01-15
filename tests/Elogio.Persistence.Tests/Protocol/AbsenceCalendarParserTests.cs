using Elogio.Persistence.Dto;
using Elogio.Persistence.Protocol;
using Xunit;
using Xunit.Abstractions;

namespace Elogio.Persistence.Tests.Protocol;

public class AbsenceCalendarParserTests
{
    private readonly ITestOutputHelper _output;
    private readonly AbsenceCalendarParser _parser = new();

    public AbsenceCalendarParserTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Parse_ValidResponse_ShouldExtractDays()
    {
        // Simplified test response with a few days
        var testResponse = BuildTestResponse();

        var result = _parser.Parse(testResponse, 52, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));

        Assert.NotNull(result);
        Assert.True(result.Days.Count > 0, "Should parse some days");

        _output.WriteLine($"Parsed {result.Days.Count} days");
        foreach (var day in result.Days.Take(10))
        {
            _output.WriteLine($"  {day.Date:dd.MM.yyyy}: {day.Type} (Holiday={day.IsPublicHoliday}, Weekend={day.IsWeekend})");
        }
    }

    [Fact]
    public void Parse_WithHoliday_ShouldDetectHoliday()
    {
        // Test response with a holiday (3,1 = isHoliday=true)
        var response = @"32,""com.bodet.bwt.core.type.communication.BWPResponse"",""NULL"",""com.bodet.bwt.gtp.serveur.domain.commun.intranet_calendrier_absence.CalendrierDemandesDataBWT"",""java.lang.Boolean"",""java.util.Map"",""com.bodet.bwt.core.type.time.BDate"",0,1,2,3,1,4,5,5,20260101,6,1,1,1,3,1,3,0,3,0,1,1,1";

        var result = _parser.Parse(response, 52, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1));

        Assert.NotNull(result);
        var newYearsDay = result.Days.FirstOrDefault(d => d.Date == new DateOnly(2026, 1, 1));

        _output.WriteLine($"New Year's Day: {newYearsDay?.Type}, IsPublicHoliday={newYearsDay?.IsPublicHoliday}");

        Assert.NotNull(newYearsDay);
        Assert.True(newYearsDay.IsPublicHoliday, "Jan 1st should be marked as public holiday");
    }

    [Fact]
    public void Parse_WithVacation_ShouldDetectVacationType()
    {
        // Test response with vacation (Blue color = -16776961) - using fictional test date
        var response = @"32,""com.bodet.bwt.core.type.communication.BWPResponse"",""NULL"",""com.bodet.bwt.gtp.serveur.domain.commun.intranet_calendrier_absence.CalendrierDemandesDataBWT"",""java.lang.Boolean"",""java.util.Map"",""com.bodet.bwt.core.type.time.BDate"",""com.bodet.bwt.gtp.serveur.domain.commun.intranet_calendrier_absence.CalendrierDemandeJourDataBWT"",""com.bodet.bwt.gtp.serveur.domain.commun.intranet_calendrier_absence.CalendrierAbsenceCellBWT"",""java.lang.String"",""URL"",""com.bodet.bwt.core.type.drawing.BColor"",0,1,2,3,1,4,5,5,20260315,6,1,7,8,10,10,-16776961,1,11,1,3,0,11,0,3,1,8,10,13,14,0,1,3,0,3,0,3,0,1,1,1";

        var result = _parser.Parse(response, 52, new DateOnly(2026, 3, 15), new DateOnly(2026, 3, 15));

        Assert.NotNull(result);
        var vacationDay = result.Days.FirstOrDefault(d => d.Date == new DateOnly(2026, 3, 15));

        _output.WriteLine($"March 15: {vacationDay?.Type}, ColorValue={vacationDay?.ColorValue}");

        Assert.NotNull(vacationDay);
        Assert.Equal(AbsenceType.Vacation, vacationDay.Type);
        Assert.Equal(-16776961, vacationDay.ColorValue);
    }

    [Fact]
    public void Parse_WithSickLeave_ShouldDetectSickLeaveType()
    {
        // Test response with sick leave (Red color = -65536) - using fictional test date
        var response = @"32,""com.bodet.bwt.core.type.communication.BWPResponse"",""NULL"",""com.bodet.bwt.gtp.serveur.domain.commun.intranet_calendrier_absence.CalendrierDemandesDataBWT"",""java.lang.Boolean"",""java.util.Map"",""com.bodet.bwt.core.type.time.BDate"",""com.bodet.bwt.gtp.serveur.domain.commun.intranet_calendrier_absence.CalendrierDemandeJourDataBWT"",""com.bodet.bwt.gtp.serveur.domain.commun.intranet_calendrier_absence.CalendrierAbsenceCellBWT"",""java.lang.String"",""KRK2"",""com.bodet.bwt.core.type.drawing.BColor"",0,1,2,3,1,4,5,5,20250210,6,1,7,8,10,10,-65536,1,11,1,3,0,11,0,3,1,8,10,13,14,0,1,3,0,3,0,3,0,1,1,1";

        var result = _parser.Parse(response, 52, new DateOnly(2025, 2, 10), new DateOnly(2025, 2, 10));

        Assert.NotNull(result);
        var sickDay = result.Days.FirstOrDefault(d => d.Date == new DateOnly(2025, 2, 10));

        _output.WriteLine($"Feb 10: {sickDay?.Type}, ColorValue={sickDay?.ColorValue}");

        Assert.NotNull(sickDay);
        Assert.Equal(AbsenceType.SickLeave, sickDay.Type);
        Assert.Equal(-65536, sickDay.ColorValue);
    }

    [Fact]
    public void Parse_WithHalfHoliday_ShouldDetectHalfHolidayType()
    {
        // Test response with half holiday (Yellow color = -256)
        var response = @"32,""com.bodet.bwt.core.type.communication.BWPResponse"",""NULL"",""com.bodet.bwt.gtp.serveur.domain.commun.intranet_calendrier_absence.CalendrierDemandesDataBWT"",""java.lang.Boolean"",""java.util.Map"",""com.bodet.bwt.core.type.time.BDate"",""com.bodet.bwt.gtp.serveur.domain.commun.intranet_calendrier_absence.CalendrierDemandeJourDataBWT"",""com.bodet.bwt.gtp.serveur.domain.commun.intranet_calendrier_absence.CalendrierAbsenceCellBWT"",""java.lang.String"",""HFT"",""com.bodet.bwt.core.type.drawing.BColor"",0,1,2,3,1,4,5,5,20261224,6,7,8,10,10,-256,1,11,1,3,0,11,0,3,1,8,10,13,14,0,1,1,3,0,3,0,3,0,1,1,1";

        var result = _parser.Parse(response, 52, new DateOnly(2026, 12, 24), new DateOnly(2026, 12, 24));

        Assert.NotNull(result);
        var christmasEve = result.Days.FirstOrDefault(d => d.Date == new DateOnly(2026, 12, 24));

        _output.WriteLine($"Dec 24: {christmasEve?.Type}, ColorValue={christmasEve?.ColorValue}");

        Assert.NotNull(christmasEve);
        Assert.Equal(AbsenceType.HalfHoliday, christmasEve.Type);
        Assert.Equal(-256, christmasEve.ColorValue);
    }

    [Fact]
    public void Parse_WithPrivateAppointment_ShouldDetectPrivateType()
    {
        // Test response with private appointment (Green color = -16711808) - using fictional test date
        var response = @"32,""com.bodet.bwt.core.type.communication.BWPResponse"",""NULL"",""com.bodet.bwt.gtp.serveur.domain.commun.intranet_calendrier_absence.CalendrierDemandesDataBWT"",""java.lang.Boolean"",""java.util.Map"",""com.bodet.bwt.core.type.time.BDate"",""com.bodet.bwt.gtp.serveur.domain.commun.intranet_calendrier_absence.CalendrierDemandeJourDataBWT"",""com.bodet.bwt.gtp.serveur.domain.commun.intranet_calendrier_absence.CalendrierAbsenceCellBWT"",""java.lang.String"",""Priv"",""com.bodet.bwt.core.type.drawing.BColor"",0,1,2,3,1,4,5,5,20250505,6,1,1,7,8,10,10,-16711808,1,11,1,3,0,11,1,3,1,8,10,13,14,0,3,0,3,0,3,0,1,1,1";

        var result = _parser.Parse(response, 52, new DateOnly(2025, 5, 5), new DateOnly(2025, 5, 5));

        Assert.NotNull(result);
        var privateDay = result.Days.FirstOrDefault(d => d.Date == new DateOnly(2025, 5, 5));

        _output.WriteLine($"May 5: {privateDay?.Type}, ColorValue={privateDay?.ColorValue}");

        Assert.NotNull(privateDay);
        Assert.Equal(AbsenceType.PrivateAppointment, privateDay.Type);
        Assert.Equal(-16711808, privateDay.ColorValue);
    }

    [Fact]
    public void AbsenceTypeHelper_FromColor_ShouldMapCorrectly()
    {
        Assert.Equal(AbsenceType.SickLeave, AbsenceTypeHelper.FromColor(-65536));
        Assert.Equal(AbsenceType.Vacation, AbsenceTypeHelper.FromColor(-16776961));
        Assert.Equal(AbsenceType.PrivateAppointment, AbsenceTypeHelper.FromColor(-16711808));
        Assert.Equal(AbsenceType.HalfHoliday, AbsenceTypeHelper.FromColor(-256));
        Assert.Equal(AbsenceType.RestDay, AbsenceTypeHelper.FromColor(-3355444));
        Assert.Equal(AbsenceType.Unknown, AbsenceTypeHelper.FromColor(12345));
    }

    [Fact]
    public void AbsenceTypeHelper_GetLabel_ShouldReturnGermanLabels()
    {
        Assert.Equal("Krankheit", AbsenceTypeHelper.GetLabel(AbsenceType.SickLeave));
        Assert.Equal("Urlaub", AbsenceTypeHelper.GetLabel(AbsenceType.Vacation));
        Assert.Equal("Privat", AbsenceTypeHelper.GetLabel(AbsenceType.PrivateAppointment));
        Assert.Equal("Halber Feiertag", AbsenceTypeHelper.GetLabel(AbsenceType.HalfHoliday));
        Assert.Equal("Feiertag", AbsenceTypeHelper.GetLabel(AbsenceType.PublicHoliday));
    }

    private static string BuildTestResponse()
    {
        // Build a minimal valid response with a few days
        return @"32,""com.bodet.bwt.core.type.communication.BWPResponse"",""NULL"",""com.bodet.bwt.gtp.serveur.domain.commun.intranet_calendrier_absence.CalendrierDemandesDataBWT"",""java.lang.Boolean"",""java.util.Map"",""com.bodet.bwt.core.type.time.BDate"",""com.bodet.bwt.gtp.serveur.domain.commun.intranet_calendrier_absence.CalendrierDemandeJourDataBWT"",0,1,2,3,1,4,5,5,20260101,6,1,1,1,3,1,3,0,3,0,1,1,1,5,20260102,6,1,1,1,3,0,3,0,3,0,1,1,1,5,20260103,6,1,1,1,3,0,3,1,3,1,1,1,1,5,20260104,6,1,1,1,3,0,3,1,3,1,1,1,1";
    }
}
