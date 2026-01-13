using Elogio.Core.Protocol;
using Shouldly;
using Xunit;

namespace Elogio.Tests.Protocol;

/// <summary>
/// Tests for GWT-RPC request builder.
/// </summary>
public class GwtRpcRequestBuilderTests
{
    private readonly GwtRpcRequestBuilder _builder = new();
    private const string TestSessionId = "00000000-0000-0000-0000-000000000000";

    #region GetSemaine Request Tests

    [Fact]
    public void BuildGetSemaineRequest_ContainsSessionId()
    {
        var request = _builder.BuildGetSemaineRequest(TestSessionId, 20260105);

        request.ShouldContain(TestSessionId);
    }

    [Fact]
    public void BuildGetSemaineRequest_ContainsDate()
    {
        var request = _builder.BuildGetSemaineRequest(TestSessionId, 20260105);

        request.ShouldContain("20260105");
    }

    [Fact]
    public void BuildGetSemaineRequest_ContainsService()
    {
        var request = _builder.BuildGetSemaineRequest(TestSessionId, 20260105);

        request.ShouldContain("DeclarationPresenceCompteurBWTService");
    }

    [Fact]
    public void BuildGetSemaineRequest_ContainsMethod()
    {
        var request = _builder.BuildGetSemaineRequest(TestSessionId, 20260105);

        request.ShouldContain("getSemaine");
    }

    [Fact]
    public void BuildGetSemaineRequest_ContainsBwpRequestType()
    {
        var request = _builder.BuildGetSemaineRequest(TestSessionId, 20260105);

        request.ShouldContain("BWPRequest");
    }

    [Fact]
    public void BuildGetSemaineRequest_ContainsBDateType()
    {
        var request = _builder.BuildGetSemaineRequest(TestSessionId, 20260105);

        request.ShouldContain("BDate");
    }

    [Fact]
    public void BuildGetSemaineRequest_StartsWithStringTableCount()
    {
        var request = _builder.BuildGetSemaineRequest(TestSessionId, 20260105);

        request.ShouldStartWith("9,");
    }

    [Fact]
    public void BuildGetSemaineRequest_MatchesExpectedFormat()
    {
        // Expected format from real API captures (with different session and date)
        var request = _builder.BuildGetSemaineRequest(TestSessionId, 20260105, 227);

        // Verify key structural elements
        request.ShouldContain("\"com.bodet.bwt.core.type.communication.BWPRequest\"");
        request.ShouldContain("\"java.util.List\"");
        request.ShouldContain("\"com.bodet.bwt.core.type.time.BDate\"");
        request.ShouldContain("\"NULL\"");
        request.ShouldContain("\"java.lang.Integer\"");
        request.ShouldContain("\"java.lang.String\"");
    }

    [Fact]
    public void BuildGetSemaineRequest_WithCustomEmployeeId()
    {
        var request = _builder.BuildGetSemaineRequest(TestSessionId, 20260105, 12345);

        request.ShouldContain("12345");
    }

    #endregion

    #region GetServerTime Request Tests

    [Fact]
    public void BuildGetServerTimeRequest_ContainsSessionId()
    {
        var request = _builder.BuildGetServerTimeRequest(TestSessionId);

        request.ShouldContain(TestSessionId);
    }

    [Fact]
    public void BuildGetServerTimeRequest_ContainsService()
    {
        var request = _builder.BuildGetServerTimeRequest(TestSessionId);

        request.ShouldContain("GlobalBWTService");
    }

    [Fact]
    public void BuildGetServerTimeRequest_ContainsMethod()
    {
        var request = _builder.BuildGetServerTimeRequest(TestSessionId);

        request.ShouldContain("getHeureServeur");
    }

    [Fact]
    public void BuildGetServerTimeRequest_StartsWithStringTableCount()
    {
        var request = _builder.BuildGetServerTimeRequest(TestSessionId);

        request.ShouldStartWith("7,");
    }

    #endregion

    #region Connect Request Tests

    [Fact]
    public void BuildConnectRequest_ContainsSessionId()
    {
        var request = _builder.BuildConnectRequest(TestSessionId, 1234567890);

        request.ShouldContain(TestSessionId);
    }

    [Fact]
    public void BuildConnectRequest_ContainsService()
    {
        var request = _builder.BuildConnectRequest(TestSessionId, 1234567890);

        request.ShouldContain("PortailBWTService");
    }

    [Fact]
    public void BuildConnectRequest_ContainsMethod()
    {
        var request = _builder.BuildConnectRequest(TestSessionId, 1234567890);

        request.ShouldContain("connect");
    }

    [Fact]
    public void BuildConnectRequest_ContainsNegativeTimestamp()
    {
        var timestamp = 1234567890L;
        var request = _builder.BuildConnectRequest(TestSessionId, timestamp);

        request.ShouldContain($"-{timestamp}");
    }

    #endregion

    #region ToKelioDate Tests

    [Theory]
    [InlineData(2026, 1, 5, 20260105)]
    [InlineData(2025, 12, 29, 20251229)]
    [InlineData(2020, 1, 1, 20200101)]
    [InlineData(2030, 12, 31, 20301231)]
    public void ToKelioDate_DateOnly_ConvertsCorrectly(int year, int month, int day, int expected)
    {
        var date = new DateOnly(year, month, day);
        var result = GwtRpcRequestBuilder.ToKelioDate(date);

        result.ShouldBe(expected);
    }

    [Theory]
    [InlineData(2026, 1, 5, 20260105)]
    [InlineData(2025, 12, 29, 20251229)]
    public void ToKelioDate_DateTime_ConvertsCorrectly(int year, int month, int day, int expected)
    {
        var date = new DateTime(year, month, day);
        var result = GwtRpcRequestBuilder.ToKelioDate(date);

        result.ShouldBe(expected);
    }

    #endregion

    #region Round-Trip Tests

    [Fact]
    public void BuildGetSemaineRequest_CanBeTokenized()
    {
        var request = _builder.BuildGetSemaineRequest(TestSessionId, 20260105);
        var tokenizer = new GwtRpcTokenizer();

        var message = tokenizer.Tokenize(request);

        message.StringTable.ShouldNotBeEmpty();
        message.DataTokens.ShouldNotBeEmpty();
        message.IsRequest.ShouldBeTrue();
    }

    [Fact]
    public void BuildGetServerTimeRequest_CanBeTokenized()
    {
        var request = _builder.BuildGetServerTimeRequest(TestSessionId);
        var tokenizer = new GwtRpcTokenizer();

        var message = tokenizer.Tokenize(request);

        message.StringTable.ShouldNotBeEmpty();
        message.IsRequest.ShouldBeTrue();
    }

    [Fact]
    public void BuildConnectRequest_CanBeTokenized()
    {
        var request = _builder.BuildConnectRequest(TestSessionId, 1234567890);
        var tokenizer = new GwtRpcTokenizer();

        var message = tokenizer.Tokenize(request);

        message.StringTable.ShouldNotBeEmpty();
        message.IsRequest.ShouldBeTrue();
    }

    #endregion

    #region String Escaping Tests

    [Fact]
    public void BuildGetSemaineRequest_EscapesQuotes()
    {
        var sessionWithQuote = "test\"session";
        var request = _builder.BuildGetSemaineRequest(sessionWithQuote, 20260105);

        request.ShouldContain("test\\\"session");
    }

    [Fact]
    public void BuildGetSemaineRequest_EscapesBackslashes()
    {
        var sessionWithBackslash = "test\\session";
        var request = _builder.BuildGetSemaineRequest(sessionWithBackslash, 20260105);

        request.ShouldContain("test\\\\session");
    }

    #endregion
}
