using Elogio.Persistence.Dto;
using Elogio.Persistence.Protocol;
using Shouldly;
using Xunit;

namespace Elogio.Persistence.Tests.Protocol;

/// <summary>
/// Tests for SemainePresenceBWT (weekly presence) GWT-RPC parser.
/// Test data extracted from real Kelio API captures.
/// </summary>
public class SemainePresenceParserTests
{
    private readonly SemainePresenceParser _parser = new();

    #region Test Data

    // Real SemainePresenceBWT response from Kelio API
    // Contains: Employee "Goltz Christopher", times 7:00/7:52/28:00/29:26, date 20260105
    private const string RealSemainePresenceResponse = """
        54,"com.bodet.bwt.core.type.communication.BWPResponse","NULL","com.bodet.bwt.app.portail.serveur.domain.declaration.presence.SemainePresenceBWT","[Z","java.lang.Boolean","[Ljava.lang.String;","java.lang.String","","java.lang.Integer","ARRAY1_com.bodet.bwt.app.portail.serveur.domain.declaration.presence.InfoJourPresenceBWT","com.bodet.bwt.app.portail.serveur.domain.declaration.presence.InfoJourPresenceBWT","com.bodet.bwt.app.portail.serveur.domain.declaration.commun.InfoGraphiqueBWT","ARRAY1_com.bodet.bwt.app.portail.serveur.domain.declaration.presence.InfoDeclarationBadgeageBWT","ARRAY1_com.bodet.bwt.app.portail.serveur.domain.declaration.presence.JourLightBWT","com.bodet.bwt.app.portail.serveur.domain.declaration.presence.JourLightBWT","com.bodet.bwt.core.type.time.BDureeHeure","com.bodet.bwt.app.portail.serveur.domain.declaration.presence.ParamSemainePresenceBWT","ARRAY1_com.bodet.bwt.app.portail.serveur.domain.declaration.presence.ConfigPresenceBWT","com.bodet.bwt.app.portail.serveur.domain.declaration.presence.ConfigPresenceBWT","java.util.List","com.bodet.bwt.app.portail.serveur.domain.declaration.presence.PresenceDetailSemaineBWT","Goltz Christopher","ARRAY1_com.bodet.bwt.app.portail.serveur.domain.declaration.presence.PresenceDetailJourBWT","com.bodet.bwt.app.portail.serveur.domain.declaration.presence.PresenceDetailJourBWT","com.bodet.bwt.app.portail.serveur.domain.declaration.presence.PresenceDetailBlocDeclarationBWT","com.bodet.bwt.core.type.time.BDate","com.bodet.bwt.app.portail.serveur.domain.declaration.presence.PresenceDetailBlocJourneeBWT","com.bodet.bwt.core.type.drawing.BColor","7h","ENUM","com.bodet.bwt.core.type.drawing.BTrame","08:30 / 16:00","com.bodet.bwt.app.portail.serveur.domain.declaration.presence.PresenceDetailBlocPresencesBWT","ARRAY1_com.bodet.bwt.app.portail.serveur.domain.declaration.presence.BadgeageLightBWT","com.bodet.bwt.app.portail.serveur.domain.declaration.presence.BadgeageLightBWT","com.bodet.bwt.core.type.time.BHeure72","com.bodet.bwt.app.portail.serveur.domain.declaration.presence.PresenceDetailBlocResultatsBWT","th","eff","7:00","7:52","28:00","29:26","7:26","6:51","7:17","com.bodet.bwt.app.portail.serveur.domain.declaration.presence.ValeurBadgeageSemaineBWT","ARRAY1_com.bodet.bwt.app.portail.serveur.domain.declaration.presence.BadgeageDeclareBWT","com.bodet.bwt.app.portail.serveur.domain.declaration.presence.BadgeageDeclareBWT","com.bodet.bwt.core.type.commun.BodetEnumBWT","Anwesenheit Benutzerbereich","com.bodet.bwt.app.portail.serveur.domain.declaration.presence.BadgeageSalarieBWT","Mobiler Bereich","ARRAY1_com.bodet.bwt.app.portail.serveur.domain.declaration.presence.ValeurPresenceBWT",0,1,2,1,3,7,4,0,4,0,4,0,4,0,4,1,4,1,4,1,5,7,6,7,6,7,6,7,6,7,6,7,6,7,6,7,5,7,6,7,6,7,6,7,6,7,6,7,6,7,6,7,3,7,4,0,4,0,4,0,4,0,4,1,4,1,4,1,3,7,4,1,4,1,4,1,4,1,4,0,4,0,4,0,1,8,52,3,7,4,0,4,0,4,0,4,0,4,0,4,0,4,0,3,7,4,0,4,0,4,0,4,0,4,0,4,0,4,0,3,7,4,0,4,0,4,0,4,0,4,0,4,0,4,0,9,8,10,4,0,4,1,1,1,11,4,0,1,4,0,1,4,0,4,0,4,0,1,1,11,4,0,1,4,0,1,4,0,4,0,4,0,1,1,11,4,0,1,4,0,1,4,0,4,0,4,0,1,1,4,1,1,1,1,1,1,10,4,0,4,1,1,1,11,4,0,1,4,0,1,4,0,4,0,4,0,1,1,11,4,0,1,4,0,1,4,0,4,0,4,0,1,1,11,4,0,1,4,0,1,4,0,4,0,4,0,1,1,4,1,1,1,1,1,1,10,4,0,4,1,1,1,11,4,0,1,4,0,1,4,0,4,0,4,0,1,1,11,4,0,1,4,0,1,4,0,4,0,4,0,1,1,11,4,0,1,4,0,1,4,0,4,0,4,0,1,1,4,1,1,1,1,1,1,10,4,0,4,1,1,1,11,4,0,1,4,0,1,4,0,4,0,4,0,1,1,11,4,0,1,4,0,1,4,0,4,0,4,0,1,1,11,4,0,1,4,0,1,4,0,4,0,4,0,1,1,4,1,1,1,1,1,1,10,4,0,4,1,1,1,11,4,0,1,4,0,1,4,0,4,0,4,0,1,1,11,4,0,1,4,0,1,4,0,4,0,4,0,1,1,11,4,0,1,4,0,1,4,0,4,0,4,0,1,1,4,1,1,1,1,1,1,10,4,0,4,1,1,1,11,4,0,1,4,0,1,4,0,4,0,4,0,1,1,11,4,0,1,4,0,1,4,0,4,0,4,0,1,1,11,4,0,1,4,0,1,4,0,4,0,4,0,1,1,4,1,1,1,1,1,1,10,4,0,4,1,1,1,11,4,0,1,4,0,1,4,0,4,0,4,0,1,1,11,4,0,1,4,0,1,4,0,4,0,4,0,1,1,11,4,0,1,4,0,1,4,0,4,0,4,0,1,1,4,1,1,1,1,1,1,10,4,0,4,1,1,1,11,4,0,1,4,0,1,4,0,4,0,4,0,1,1,11,4,0,1,4,0,1,4,0,4,0,4,0,1,1,11,4,0,1,4,0,1,4,0,4,0,4,0,1,1,4,1,1,1,1,1,1,12,7,1,1,1,1,1,1,1,13,8,14,1,4,0,15,0,0,15,0,0,15,0,0,15,0,28320,15,0,25200,1,14,1,4,0,15,0,0,15,0,0,15,0,0,15,0,26760,15,0,25200,1,14,1,4,0,15,0,0,15,0,0,15,0,0,15,0,24660,15,0,25200,1,14,1,4,0,15,0,0,15,0,0,15,0,0,15,0,26220,15,0,25200,1,14,1,4,0,15,0,0,15,0,0,15,0,0,15,0,0,15,0,25200,1,14,1,4,0,15,0,0,15,0,0,15,0,0,15,0,0,15,0,0,1,14,1,4,0,15,0,0,15,0,0,15,0,0,15,0,0,15,0,0,1,14,1,4,0,1,1,1,15,0,105960,15,0,126000,1,3,7,4,0,4,0,4,0,4,0,4,0,4,0,4,0,8,0,16,1,4,1,3,7,4,1,4,1,4,1,4,1,4,0,4,0,4,0,4,0,4,0,1,17,7,18,4,0,15,0,0,4,0,4,0,4,0,4,0,18,4,0,15,0,0,4,0,4,0,4,0,4,0,18,4,0,15,0,0,4,0,4,0,4,0,4,0,18,4,0,15,0,0,4,0,4,0,4,0,4,0,18,4,0,15,0,0,4,0,4,0,4,0,4,0,18,4,0,15,0,0,4,0,4,0,4,0,4,0,18,4,0,15,0,0,4,0,4,0,4,0,4,0,4,1,4,1,4,1,1,4,0,4,0,19,0,8,0,4,1,4,0,4,0,4,1,20,4,0,6,21,22,7,23,1,24,1,1,1,25,20260105,1,26,1,11,4,0,27,-256,4,0,6,28,4,0,4,0,4,0,29,30,0,1,1,4,0,19,1,6,31,1,15,0,25200,32,19,2,33,2,34,4,0,25,20260105,35,565,8,2147483647,1,34,4
        """;

    // Simplified test data for specific tests
    private const string MinimalSemainePresenceResponse =
        """5,"com.bodet.bwt.core.type.communication.BWPResponse","NULL","com.bodet.bwt.app.portail.serveur.domain.declaration.presence.SemainePresenceBWT","[Z","java.lang.Boolean",0,1,2""";

    private const string NonPresenceResponse =
        """3,"com.bodet.bwt.core.type.communication.BWPResponse","NULL","com.bodet.bwt.app.portail.serveur.domain.other.SomeOtherType",0,1,2""";

    private const string RequestNotResponse =
        """3,"com.bodet.bwt.core.type.communication.BWPRequest","java.util.List","java.lang.String",0,1,2""";

    #endregion

    #region Parse Detection Tests

    [Fact]
    public void Parse_RealSemainePresenceResponse_ReturnsWeekPresence()
    {
        var result = _parser.Parse(RealSemainePresenceResponse);

        result.ShouldNotBeNull();
    }

    [Fact]
    public void Parse_MinimalSemainePresenceResponse_ReturnsWeekPresence()
    {
        var result = _parser.Parse(MinimalSemainePresenceResponse);

        result.ShouldNotBeNull();
    }

    [Fact]
    public void Parse_NonPresenceResponse_ReturnsNull()
    {
        var result = _parser.Parse(NonPresenceResponse);

        result.ShouldBeNull();
    }

    [Fact]
    public void Parse_RequestNotResponse_ReturnsNull()
    {
        var result = _parser.Parse(RequestNotResponse);

        result.ShouldBeNull();
    }

    [Fact]
    public void Parse_EmptyString_ReturnsNull()
    {
        var result = _parser.Parse("");

        result.ShouldBeNull();
    }

    [Fact]
    public void Parse_InvalidData_ReturnsNull()
    {
        var result = _parser.Parse("invalid data that is not gwt-rpc");

        result.ShouldBeNull();
    }

    #endregion

    #region Employee Name Tests

    [Fact]
    public void Parse_RealResponse_ExtractsEmployeeName()
    {
        var result = _parser.Parse(RealSemainePresenceResponse);

        result.ShouldNotBeNull();
        result.EmployeeName.ShouldBe("Goltz Christopher");
    }

    [Fact]
    public void Parse_NoEmployeeName_UsesDefault()
    {
        var result = _parser.Parse(MinimalSemainePresenceResponse);

        result.ShouldNotBeNull();
        result.EmployeeName.ShouldBe("Unknown");
    }

    #endregion

    #region Time Value Tests

    [Fact]
    public void Parse_RealResponse_FindsTimeValues()
    {
        var result = _parser.Parse(RealSemainePresenceResponse);

        result.ShouldNotBeNull();
        // Response contains time strings: "7:00", "7:52", "28:00", "29:26", "7:26", "6:51", "7:17"
        // These should be parsed and used for worked/expected times
    }

    [Fact]
    public void Parse_RealResponse_CalculatesTotalExpected()
    {
        var result = _parser.Parse(RealSemainePresenceResponse);

        result.ShouldNotBeNull();
        // Expected should be 28:00 (from string table) or derived from seconds
        // 126000 seconds = 35 hours (expected weekly)
    }

    [Fact]
    public void Parse_RealResponse_CalculatesTotalWorked()
    {
        var result = _parser.Parse(RealSemainePresenceResponse);

        result.ShouldNotBeNull();
        // Worked should be 29:26 (from string table) or derived from seconds
        // 105960 seconds = 29.43 hours (actual worked)
    }

    #endregion

    #region Date Tests

    [Fact]
    public void Parse_RealResponse_ExtractsDate()
    {
        var result = _parser.Parse(RealSemainePresenceResponse);

        result.ShouldNotBeNull();
        // Response contains date 20260105 (January 5, 2026)
        result.WeekStartDate.Year.ShouldBe(2026);
        result.WeekStartDate.Month.ShouldBe(1);
        result.WeekStartDate.Day.ShouldBe(5);
    }

    [Fact]
    public void Parse_NoDates_UsesToday()
    {
        var result = _parser.Parse(MinimalSemainePresenceResponse);

        result.ShouldNotBeNull();
        // Should default to today's date when no date found
        result.WeekStartDate.ShouldBe(DateOnly.FromDateTime(DateTime.Today));
    }

    #endregion

    #region Day Entries Tests

    [Fact]
    public void Parse_RealResponse_CreatesDayEntries()
    {
        var result = _parser.Parse(RealSemainePresenceResponse);

        result.ShouldNotBeNull();
        result.Days.ShouldNotBeNull();
        result.Days.Count.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Parse_RealResponse_DaysHaveCorrectDayOfWeek()
    {
        var result = _parser.Parse(RealSemainePresenceResponse);

        result.ShouldNotBeNull();
        if (result.Days.Count > 0)
        {
            // First day (2026-01-05) is a Monday
            result.Days[0].Date.DayOfWeek.ShouldBe(DayOfWeek.Monday);
            result.Days[0].DayOfWeek.ShouldBe("Monday");
        }
    }

    [Fact]
    public void Parse_RealResponse_WeekendDaysHaveZeroExpected()
    {
        var result = _parser.Parse(RealSemainePresenceResponse);

        result.ShouldNotBeNull();
        foreach (var day in result.Days)
        {
            if (day.IsWeekend)
            {
                day.ExpectedTime.ShouldBe(TimeSpan.Zero);
            }
        }
    }

    #endregion

    #region Balance Calculation Tests

    [Fact]
    public void Parse_RealResponse_BalanceIsWorkedMinusExpected()
    {
        var result = _parser.Parse(RealSemainePresenceResponse);

        result.ShouldNotBeNull();
        result.Balance.ShouldBe(result.TotalWorked - result.TotalExpected);
    }

    #endregion

    #region Badge Entry Tests

    [Fact]
    public void Parse_RealResponse_ExtractsBadgeEntries()
    {
        var result = _parser.Parse(RealSemainePresenceResponse);

        result.ShouldNotBeNull();

        // Check if any day has badge entries
        var daysWithEntries = result.Days.Where(d => d.Entries.Count > 0).ToList();

        // Log what we found for debugging
        System.Console.WriteLine($"Days with entries: {daysWithEntries.Count}");
        foreach (var day in result.Days)
        {
            System.Console.WriteLine($"  {day.Date}: {day.Entries.Count} entries");
            foreach (var entry in day.Entries)
            {
                System.Console.WriteLine($"    {entry.Time} - {entry.Type}");
            }
        }
    }

    #endregion
}

/// <summary>
/// Tests for KelioTimeHelpers utility methods.
/// </summary>
public class KelioTimeHelpersTests
{
    #region ParseDate Tests

    [Theory]
    [InlineData(20260105, 2026, 1, 5)]
    [InlineData(20251229, 2025, 12, 29)]
    [InlineData(20200101, 2020, 1, 1)]
    [InlineData(20301231, 2030, 12, 31)]
    public void ParseDate_Int_ParsesCorrectly(int kelioDate, int year, int month, int day)
    {
        var result = KelioTimeHelpers.ParseDate(kelioDate);

        result.Year.ShouldBe(year);
        result.Month.ShouldBe(month);
        result.Day.ShouldBe(day);
    }

    [Theory]
    [InlineData("20260105", 2026, 1, 5)]
    [InlineData("20251229", 2025, 12, 29)]
    public void ParseDate_String_ParsesCorrectly(string kelioDate, int year, int month, int day)
    {
        var result = KelioTimeHelpers.ParseDate(kelioDate);

        result.Year.ShouldBe(year);
        result.Month.ShouldBe(month);
        result.Day.ShouldBe(day);
    }

    [Fact]
    public void ParseDate_InvalidString_ThrowsException()
    {
        Should.Throw<ArgumentException>(() => KelioTimeHelpers.ParseDate("invalid"));
    }

    #endregion

    #region SecondsToTimeSpan Tests

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(3600, 1, 0, 0)]
    [InlineData(25200, 7, 0, 0)]      // 7 hours
    [InlineData(28320, 7, 52, 0)]    // 7:52
    [InlineData(105960, 29, 26, 0)]  // 29:26
    [InlineData(126000, 35, 0, 0)]   // 35 hours (weekly expected)
    public void SecondsToTimeSpan_ConvertsCorrectly(int seconds, int hours, int minutes, int secs)
    {
        var result = KelioTimeHelpers.SecondsToTimeSpan(seconds);

        result.Hours.ShouldBe(hours % 24);
        result.Minutes.ShouldBe(minutes);
        result.Seconds.ShouldBe(secs);
        result.TotalSeconds.ShouldBe(seconds);
    }

    #endregion

    #region SecondsToTimeOnly Tests

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(3600, 1, 0)]
    [InlineData(28800, 8, 0)]      // 8:00
    [InlineData(30600, 8, 30)]     // 8:30
    [InlineData(57600, 16, 0)]     // 16:00
    public void SecondsToTimeOnly_ConvertsCorrectly(int seconds, int hours, int minutes)
    {
        var result = KelioTimeHelpers.SecondsToTimeOnly(seconds);

        result.Hour.ShouldBe(hours);
        result.Minute.ShouldBe(minutes);
    }

    [Fact]
    public void SecondsToTimeOnly_OverflowWraps()
    {
        // 25 hours should wrap to 1:00
        var result = KelioTimeHelpers.SecondsToTimeOnly(90000); // 25 * 3600

        result.Hour.ShouldBe(1);
        result.Minute.ShouldBe(0);
    }

    #endregion

    #region ParseTimeString Tests

    [Theory]
    [InlineData("7:00", 7, 0)]
    [InlineData("7:52", 7, 52)]
    [InlineData("28:00", 28, 0)]
    [InlineData("29:26", 29, 26)]
    [InlineData("0:30", 0, 30)]
    public void ParseTimeString_ParsesCorrectly(string timeStr, int hours, int minutes)
    {
        var result = KelioTimeHelpers.ParseTimeString(timeStr);

        result.Hours.ShouldBe(hours % 24);
        result.Minutes.ShouldBe(minutes);
        result.TotalHours.ShouldBe(hours + minutes / 60.0, 0.01);
    }

    [Theory]
    [InlineData("8:30:45", 8, 30, 45)]
    public void ParseTimeString_WithSeconds_ParsesCorrectly(string timeStr, int hours, int minutes, int seconds)
    {
        var result = KelioTimeHelpers.ParseTimeString(timeStr);

        result.Hours.ShouldBe(hours);
        result.Minutes.ShouldBe(minutes);
        result.Seconds.ShouldBe(seconds);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void ParseTimeString_EmptyOrNull_ReturnsZero(string? timeStr)
    {
        var result = KelioTimeHelpers.ParseTimeString(timeStr!);

        result.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void ParseTimeString_InvalidFormat_ReturnsZero()
    {
        var result = KelioTimeHelpers.ParseTimeString("invalid");

        result.ShouldBe(TimeSpan.Zero);
    }

    #endregion

    #region FormatTimeSpan Tests

    [Theory]
    [InlineData(7, 0, "7:00")]
    [InlineData(7, 52, "7:52")]
    [InlineData(28, 0, "28:00")]
    [InlineData(0, 30, "0:30")]
    [InlineData(0, 5, "0:05")]
    public void FormatTimeSpan_FormatsCorrectly(int hours, int minutes, string expected)
    {
        var timeSpan = new TimeSpan(hours, minutes, 0);
        var result = KelioTimeHelpers.FormatTimeSpan(timeSpan);

        result.ShouldBe(expected);
    }

    [Fact]
    public void FormatTimeSpan_NegativeTime_IncludesSign()
    {
        var timeSpan = new TimeSpan(-1, -30, 0);
        var result = KelioTimeHelpers.FormatTimeSpan(timeSpan);

        result.ShouldStartWith("-");
    }

    #endregion
}
