using Elogio.Persistence.Dto;
using Elogio.Persistence.Protocol;
using Shouldly;
using Xunit;

namespace Elogio.Persistence.Tests.Integration;

/// <summary>
/// Integration tests for the Punch (clock-in/clock-out) functionality.
///
/// IMPORTANT: These tests use mocked data based on real HAR captures.
/// They do NOT make real API calls to prevent accidental production writes.
///
/// The tests verify the complete flow from request building through BWP encoding/decoding
/// to response parsing.
/// </summary>
[Trait("Category", "Integration")]
public class PunchIntegrationTests
{
    private readonly GwtRpcRequestBuilder _requestBuilder = new();
    private readonly BwpCodec _bwpCodec = new();
    private readonly BadgerSignalerResponseParser _responseParser = new();
    private readonly GwtRpcTokenizer _tokenizer = new();

    // Test constants (anonymized)
    private const string TestSessionId1 = "00000000-0000-0000-0000-000000000001";
    private const string TestSessionId2 = "00000000-0000-0000-0000-000000000002";
    private const int TestEmployeeId1 = 12345;
    private const int TestEmployeeId2 = 67890;

    #region Test Fixtures (anonymized from HAR captures)

    // Clock-in request format
    private const string ClockInRequestDecoded = """
        9,"com.bodet.bwt.core.type.communication.BWPRequest","java.util.List","NULL","java.lang.Boolean","java.lang.Integer","java.lang.String","00000000-0000-0000-0000-000000000001","badgerSignaler","com.bodet.bwt.portail.serveur.service.commun.vignette.presence.BadgerSignalerPortailBWTService",0,1,3,2,2,3,0,4,12345,5,6,5,7,5,8
        """;

    // Clock-in response format
    private const string ClockInResponseDecoded = """
        8,"com.bodet.bwt.core.type.communication.BWPResponse","NULL","com.bodet.bwt.portail.serveur.domain.portail.vignette.badger_signaler.resultat.PortailVignetteBadgerSignalerResultatMessageBWT","java.lang.Boolean","java.lang.String","Beginnen/beenden","java.util.List","Letzte Buchung um 09:26 (Kommen)",0,1,2,3,1,3,0,3,1,4,5,6,1,4,7,1,3,0
        """;

    // Clock-out request format
    private const string ClockOutRequestDecoded = """
        9,"com.bodet.bwt.core.type.communication.BWPRequest","java.util.List","NULL","java.lang.Boolean","java.lang.Integer","java.lang.String","00000000-0000-0000-0000-000000000002","badgerSignaler","com.bodet.bwt.portail.serveur.service.commun.vignette.presence.BadgerSignalerPortailBWTService",0,1,3,2,2,3,0,4,67890,5,6,5,7,5,8
        """;

    // Clock-out response format
    private const string ClockOutResponseDecoded = """
        8,"com.bodet.bwt.core.type.communication.BWPResponse","NULL","com.bodet.bwt.portail.serveur.domain.portail.vignette.badger_signaler.resultat.PortailVignetteBadgerSignalerResultatMessageBWT","java.lang.Boolean","java.lang.String","Beginnen/beenden","java.util.List","Letzte Buchung um 17:06 (Gehen)",0,1,2,3,1,3,0,3,1,4,5,6,1,4,7,1,3,0
        """;

    #endregion

    #region Request Builder Integration Tests

    [Fact]
    public void BuildRequest_MatchesExpectedFormat()
    {
        // Build request using our code
        var generatedRequest = _requestBuilder.BuildBadgerSignalerRequest(TestSessionId1, TestEmployeeId1);

        // Compare with expected format
        var expectedRequest = ClockInRequestDecoded.Trim();

        // Both should tokenize to the same structure
        var generatedMessage = _tokenizer.Tokenize(generatedRequest);
        var expectedMessage = _tokenizer.Tokenize(expectedRequest);

        generatedMessage.StringTable.Length.ShouldBe(expectedMessage.StringTable.Length);
        generatedMessage.IsRequest.ShouldBeTrue();
        expectedMessage.IsRequest.ShouldBeTrue();

        // Key strings should match
        generatedRequest.ShouldContain(TestSessionId1);
        generatedRequest.ShouldContain(TestEmployeeId1.ToString());
        generatedRequest.ShouldContain("badgerSignaler");
        generatedRequest.ShouldContain("BadgerSignalerPortailBWTService");
    }

    [Fact]
    public void BuildRequest_ForDifferentEmployees_ProducesValidRequests()
    {
        var sessionId = "test-session-id";
        var employeeIds = new[] { 100, 500, 1000, 9999 };

        foreach (var employeeId in employeeIds)
        {
            var request = _requestBuilder.BuildBadgerSignalerRequest(sessionId, employeeId);

            // Should be valid GWT-RPC
            var message = _tokenizer.Tokenize(request);
            message.IsRequest.ShouldBeTrue();
            message.StringTable.Length.ShouldBe(9);

            // Should contain the employee ID
            request.ShouldContain(employeeId.ToString());
        }
    }

    #endregion

    #region BWP Encode/Decode Round-Trip Tests

    [Fact]
    public void BwpEncode_Request_RoundTripSucceeds()
    {
        var originalRequest = ClockInRequestDecoded.Trim();

        // Encode
        var encoded = _bwpCodec.Encode(originalRequest);

        // Verify it's BWP encoded
        _bwpCodec.IsBwp(encoded).ShouldBeTrue();

        // Decode
        var decoded = _bwpCodec.Decode(encoded);

        decoded.Decoded.ShouldBe(originalRequest);
    }

    [Fact]
    public void BwpEncode_Response_RoundTripSucceeds()
    {
        var originalResponse = ClockInResponseDecoded.Trim();

        // Encode
        var encoded = _bwpCodec.Encode(originalResponse);

        // Verify it's BWP encoded
        _bwpCodec.IsBwp(encoded).ShouldBeTrue();

        // Decode
        var decoded = _bwpCodec.Decode(encoded);

        decoded.Decoded.ShouldBe(originalResponse);
    }

    [Fact]
    public void BwpEncode_GeneratedRequest_RoundTripSucceeds()
    {
        var request = _requestBuilder.BuildBadgerSignalerRequest("test-session", 123);

        // Encode
        var encoded = _bwpCodec.Encode(request);

        // Verify encoding
        _bwpCodec.IsBwp(encoded).ShouldBeTrue();
        encoded.ShouldStartWith("\u00A4"); // BWP marker

        // Decode
        var decoded = _bwpCodec.Decode(encoded);

        decoded.Decoded.ShouldBe(request);
    }

    #endregion

    #region Response Parsing Integration Tests

    [Fact]
    public void ParseResponse_ClockIn_ReturnsCorrectResult()
    {
        var response = ClockInResponseDecoded.Trim();

        var result = _responseParser.Parse(response);

        result.ShouldNotBeNull();
        result.Success.ShouldBeTrue();
        result.Type.ShouldBe(PunchType.ClockIn);
        result.Timestamp.ShouldNotBeNull();
        result.Timestamp!.Value.Hour.ShouldBe(9);
        result.Timestamp!.Value.Minute.ShouldBe(26);
    }

    [Fact]
    public void ParseResponse_ClockOut_ReturnsCorrectResult()
    {
        var response = ClockOutResponseDecoded.Trim();

        var result = _responseParser.Parse(response);

        result.ShouldNotBeNull();
        result.Success.ShouldBeTrue();
        result.Type.ShouldBe(PunchType.ClockOut);
        result.Timestamp.ShouldNotBeNull();
        result.Timestamp!.Value.Hour.ShouldBe(17);
        result.Timestamp!.Value.Minute.ShouldBe(6);
    }

    [Fact]
    public void ParseResponse_AfterBwpDecode_ReturnsCorrectResult()
    {
        // Simulate what happens when we receive a BWP-encoded response
        var originalResponse = ClockInResponseDecoded.Trim();

        // Encode (simulate what server sends)
        var bwpEncoded = _bwpCodec.Encode(originalResponse);

        // Decode (what our client does)
        var decoded = _bwpCodec.Decode(bwpEncoded);

        // Parse
        var result = _responseParser.Parse(decoded.Decoded);

        result.ShouldNotBeNull();
        result.Success.ShouldBeTrue();
        result.Type.ShouldBe(PunchType.ClockIn);
    }

    #endregion

    #region Full Flow Integration Tests

    [Fact]
    public void FullFlow_BuildRequest_Encode_Decode_VerifyFormat()
    {
        // Step 1: Build request
        var request = _requestBuilder.BuildBadgerSignalerRequest(TestSessionId1, TestEmployeeId1);

        // Step 2: Encode with BWP (what our client does before sending)
        var encoded = _bwpCodec.Encode(request);

        // Step 3: Verify it's properly encoded
        _bwpCodec.IsBwp(encoded).ShouldBeTrue();

        // Step 4: Decode (simulate server receiving and decoding)
        var decoded = _bwpCodec.Decode(encoded);

        // Step 5: Verify the decoded request has correct structure
        var message = _tokenizer.Tokenize(decoded.Decoded);
        message.IsRequest.ShouldBeTrue();
        message.StringTable.ShouldContain("badgerSignaler");
        message.StringTable.ShouldContain(s => s.Contains("BadgerSignalerPortailBWTService"));
    }

    [Fact]
    public void FullFlow_SimulateServerResponse_ParseSuccessfully()
    {
        // Simulate receiving an encoded response from server
        var serverResponse = ClockOutResponseDecoded.Trim();
        var encodedResponse = _bwpCodec.Encode(serverResponse);

        // Client receives and decodes
        var decoded = _bwpCodec.Decode(encodedResponse);

        // Client parses
        var result = _responseParser.Parse(decoded.Decoded);

        // Verify full chain worked
        result.ShouldNotBeNull();
        result.Success.ShouldBeTrue();
        result.Type.ShouldBe(PunchType.ClockOut);
        result.Message.ShouldNotBeNull();
        result.Message!.ShouldContain("Gehen");
        result.Label.ShouldBe("Beginnen/beenden");
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void Request_WithSpecialCharactersInSessionId_HandlesCorrectly()
    {
        // Session IDs should not have special chars, but test edge case
        var sessionId = "test-session-with-dash";
        var request = _requestBuilder.BuildBadgerSignalerRequest(sessionId, 123);

        // Should still produce valid GWT-RPC
        var message = _tokenizer.Tokenize(request);
        message.IsRequest.ShouldBeTrue();
    }

    [Fact]
    public void Response_WithDifferentTimeFormats_ParsesCorrectly()
    {
        // Test various German time message formats
        var testCases = new[]
        {
            ("Letzte Buchung um 08:00 (Kommen)", PunchType.ClockIn, 8, 0),
            ("Letzte Buchung um 23:59 (Gehen)", PunchType.ClockOut, 23, 59),
            ("Letzte Buchung um 00:01 (Kommen)", PunchType.ClockIn, 0, 1),
        };

        foreach (var (message, expectedType, expectedHour, expectedMinute) in testCases)
        {
            var punchType = PunchResultDto.ParsePunchType(message);
            var timestamp = PunchResultDto.ParseTimeFromMessage(message);

            punchType.ShouldBe(expectedType);
            timestamp.ShouldNotBeNull();
            timestamp!.Value.Hour.ShouldBe(expectedHour);
            timestamp!.Value.Minute.ShouldBe(expectedMinute);
        }
    }

    #endregion
}
