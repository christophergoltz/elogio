using Elogio.Persistence.Dto;
using Elogio.Persistence.Protocol;
using Shouldly;
using Xunit;

namespace Elogio.Persistence.Tests.Protocol;

/// <summary>
/// Tests for BadgerSignaler (punch/clock-in/clock-out) response parser.
/// Test data is based on actual HAR captures from the Kelio API.
/// </summary>
public class BadgerSignalerResponseParserTests
{
    private readonly BadgerSignalerResponseParser _parser = new();

    #region Test Data from HAR Captures

    // Clock-in (Kommen) response from kommen_pharmagest.kelio.io.har
    private const string ClockInResponse = """
        8,"com.bodet.bwt.core.type.communication.BWPResponse","NULL","com.bodet.bwt.portail.serveur.domain.portail.vignette.badger_signaler.resultat.PortailVignetteBadgerSignalerResultatMessageBWT","java.lang.Boolean","java.lang.String","Beginnen/beenden","java.util.List","Letzte Buchung um 09:26 (Kommen)",0,1,2,3,1,3,0,3,1,4,5,6,1,4,7,1,3,0
        """;

    // Clock-out (Gehen) response from gehen_pharmagest.kelio.io.har
    private const string ClockOutResponse = """
        8,"com.bodet.bwt.core.type.communication.BWPResponse","NULL","com.bodet.bwt.portail.serveur.domain.portail.vignette.badger_signaler.resultat.PortailVignetteBadgerSignalerResultatMessageBWT","java.lang.Boolean","java.lang.String","Beginnen/beenden","java.util.List","Letzte Buchung um 17:06 (Gehen)",0,1,2,3,1,3,0,3,1,4,5,6,1,4,7,1,3,0
        """;

    // Error response
    private const string ErrorResponse = """
        3,"com.bodet.bwt.core.type.communication.BWPResponse","ExceptionBWT","Server error occurred",0,1,2
        """;

    #endregion

    #region Parse Success Tests

    [Fact]
    public void Parse_ClockInResponse_ReturnsSuccess()
    {
        var result = _parser.Parse(ClockInResponse);

        result.ShouldNotBeNull();
        result.Success.ShouldBeTrue();
    }

    [Fact]
    public void Parse_ClockInResponse_IdentifiesClockInType()
    {
        var result = _parser.Parse(ClockInResponse);

        result.ShouldNotBeNull();
        result.Type.ShouldBe(PunchType.ClockIn);
    }

    [Fact]
    public void Parse_ClockInResponse_ExtractsMessage()
    {
        var result = _parser.Parse(ClockInResponse);

        result.ShouldNotBeNull();
        result.Message.ShouldNotBeNull();
        result.Message!.ShouldContain("Kommen");
        result.Message.ShouldContain("09:26");
    }

    [Fact]
    public void Parse_ClockInResponse_ExtractsTimestamp()
    {
        var result = _parser.Parse(ClockInResponse);

        result.ShouldNotBeNull();
        result.Timestamp.ShouldNotBeNull();
        result.Timestamp!.Value.Hour.ShouldBe(9);
        result.Timestamp!.Value.Minute.ShouldBe(26);
    }

    [Fact]
    public void Parse_ClockInResponse_ExtractsLabel()
    {
        var result = _parser.Parse(ClockInResponse);

        result.ShouldNotBeNull();
        result.Label.ShouldBe("Beginnen/beenden");
    }

    [Fact]
    public void Parse_ClockOutResponse_ReturnsSuccess()
    {
        var result = _parser.Parse(ClockOutResponse);

        result.ShouldNotBeNull();
        result.Success.ShouldBeTrue();
    }

    [Fact]
    public void Parse_ClockOutResponse_IdentifiesClockOutType()
    {
        var result = _parser.Parse(ClockOutResponse);

        result.ShouldNotBeNull();
        result.Type.ShouldBe(PunchType.ClockOut);
    }

    [Fact]
    public void Parse_ClockOutResponse_ExtractsMessage()
    {
        var result = _parser.Parse(ClockOutResponse);

        result.ShouldNotBeNull();
        result.Message.ShouldNotBeNull();
        result.Message!.ShouldContain("Gehen");
        result.Message.ShouldContain("17:06");
    }

    [Fact]
    public void Parse_ClockOutResponse_ExtractsTimestamp()
    {
        var result = _parser.Parse(ClockOutResponse);

        result.ShouldNotBeNull();
        result.Timestamp.ShouldNotBeNull();
        result.Timestamp!.Value.Hour.ShouldBe(17);
        result.Timestamp!.Value.Minute.ShouldBe(6);
    }

    #endregion

    #region Parse Error Tests

    [Fact]
    public void Parse_ErrorResponse_ReturnsFailure()
    {
        var result = _parser.Parse(ErrorResponse);

        result.ShouldNotBeNull();
        result.Success.ShouldBeFalse();
        result.Type.ShouldBe(PunchType.Unknown);
        result.Error.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public void Parse_NullInput_ReturnsNull()
    {
        var result = _parser.Parse(null!);

        result.ShouldBeNull();
    }

    [Fact]
    public void Parse_EmptyInput_ReturnsNull()
    {
        var result = _parser.Parse("");

        result.ShouldBeNull();
    }

    [Fact]
    public void Parse_InvalidGwtRpc_ReturnsNull()
    {
        var result = _parser.Parse("not a valid gwt-rpc response");

        result.ShouldBeNull();
    }

    [Fact]
    public void Parse_WrongResponseType_ReturnsNull()
    {
        // Response from a different service (not BadgerSignaler)
        const string otherResponse = """
            3,"com.bodet.bwt.core.type.communication.BWPResponse","NULL","com.bodet.bwt.core.type.time.BDateHeure",0,1,2,20260113,33993
            """;

        var result = _parser.Parse(otherResponse);

        result.ShouldBeNull();
    }

    #endregion

    #region PunchResultDto Helper Tests

    [Theory]
    [InlineData("Letzte Buchung um 09:26 (Kommen)", PunchType.ClockIn)]
    [InlineData("Letzte Buchung um 17:06 (Gehen)", PunchType.ClockOut)]
    [InlineData("(Kommen)", PunchType.ClockIn)]
    [InlineData("(Gehen)", PunchType.ClockOut)]
    [InlineData("Kommen", PunchType.ClockIn)]
    [InlineData("Gehen", PunchType.ClockOut)]
    [InlineData("", PunchType.Unknown)]
    [InlineData(null, PunchType.Unknown)]
    [InlineData("Some other message", PunchType.Unknown)]
    public void ParsePunchType_VariousMessages_ReturnsCorrectType(string? message, PunchType expected)
    {
        var result = PunchResultDto.ParsePunchType(message);

        result.ShouldBe(expected);
    }

    [Theory]
    [InlineData("Letzte Buchung um 09:26 (Kommen)", 9, 26)]
    [InlineData("Letzte Buchung um 17:06 (Gehen)", 17, 6)]
    [InlineData("um 08:00", 8, 0)]
    [InlineData("um 23:59", 23, 59)]
    public void ParseTimeFromMessage_ValidTime_ReturnsCorrectTime(string message, int expectedHour, int expectedMinute)
    {
        var result = PunchResultDto.ParseTimeFromMessage(message);

        result.ShouldNotBeNull();
        result!.Value.Hour.ShouldBe(expectedHour);
        result!.Value.Minute.ShouldBe(expectedMinute);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("No time here")]
    [InlineData("Invalid format")]
    public void ParseTimeFromMessage_InvalidInput_ReturnsNull(string? message)
    {
        var result = PunchResultDto.ParseTimeFromMessage(message);

        result.ShouldBeNull();
    }

    #endregion
}
