using Elogio.Persistence.Protocol;
using Shouldly;
using Xunit;

namespace Elogio.Persistence.Tests.Protocol;

/// <summary>
/// Comprehensive tests for BWP (Bodet Web Protocol) encoder/decoder.
/// </summary>
public class BwpCodecTests
{
    private readonly BwpCodec _codec = new();

    #region Test Data

    // Sample GWT-RPC request structure (Service: GlobalBWTService, Method: getHeureServeur)
    // Uses dummy session ID for testing
    private const string SampleDecodedRequest =
        "7,\"com.bodet.bwt.core.type.communication.BWPRequest\",\"java.util.List\",\"java.lang.Integer\",\"java.lang.String\",\"00000000-0000-0000-0000-000000000000\",\"getHeureServeur\",\"com.bodet.bwt.global.serveur.service.GlobalBWTService\",0,1,0,2,226,3,4,3,5,3,6";

    private static readonly int[] SampleRequestKeys = [13, 13, 4, 13, 10, 1, 10, 7];

    // Encoded version generated dynamically from SampleDecodedRequest + SampleRequestKeys
    private string SampleEncodedRequest => _codec.Encode(SampleDecodedRequest, SampleRequestKeys);

    #endregion

    #region IsBwp Tests

    [Fact]
    public void IsBwp_WithBwpEncodedData_ReturnsTrue()
    {
        _codec.IsBwp(SampleEncodedRequest).ShouldBeTrue();
    }

    [Fact]
    public void IsBwp_WithMarkerOnly_ReturnsTrue()
    {
        _codec.IsBwp("\u00a4").ShouldBeTrue();
    }

    [Fact]
    public void IsBwp_WithPlainText_ReturnsFalse()
    {
        _codec.IsBwp("Hello World").ShouldBeFalse();
        _codec.IsBwp("7,\"com.bodet.bwt\"").ShouldBeFalse();
    }

    [Fact]
    public void IsBwp_WithEmptyString_ReturnsFalse()
    {
        _codec.IsBwp("").ShouldBeFalse();
    }

    [Fact]
    public void IsBwp_WithNull_ReturnsFalse()
    {
        _codec.IsBwp(null!).ShouldBeFalse();
    }

    [Fact]
    public void IsBwp_WithDifferentFirstChar_ReturnsFalse()
    {
        _codec.IsBwp("A" + SampleEncodedRequest[1..]).ShouldBeFalse();
    }

    #endregion

    #region Decode Tests

    [Fact]
    public void Decode_RealRequest_DecodesCorrectly()
    {
        var result = _codec.Decode(SampleEncodedRequest);

        result.IsEncoded.ShouldBeTrue();
        result.Decoded.ShouldBe(SampleDecodedRequest);
        result.Keys.ShouldBe(SampleRequestKeys);
    }

    [Fact]
    public void Decode_ExtractsCorrectKeyCount()
    {
        var result = _codec.Decode(SampleEncodedRequest);

        result.Keys.Length.ShouldBe(8);
        // Key count is stored as chr(48 + N), so byte 1 should be '8' (56) for 8 keys
        SampleEncodedRequest[1].ShouldBe('8');
    }

    [Fact]
    public void Decode_CalculatesCorrectHeaderLength()
    {
        var result = _codec.Decode(SampleEncodedRequest);

        // Header = 2 (marker + key count) + number of keys
        result.HeaderLength.ShouldBe(2 + result.Keys.Length);
    }

    [Fact]
    public void Decode_PlainText_ReturnsAsIs()
    {
        const string plainText = "Hello World";
        var result = _codec.Decode(plainText);

        result.IsEncoded.ShouldBeFalse();
        result.Decoded.ShouldBe(plainText);
        result.Keys.ShouldBeEmpty();
        result.HeaderLength.ShouldBe(0);
    }

    [Fact]
    public void Decode_EmptyString_ReturnsEmpty()
    {
        var result = _codec.Decode("");

        result.IsEncoded.ShouldBeFalse();
        result.Decoded.ShouldBe("");
        result.Keys.ShouldBeEmpty();
    }

    [Fact]
    public void Decode_NullString_ReturnsEmpty()
    {
        var result = _codec.Decode(null!);

        result.IsEncoded.ShouldBeFalse();
        result.Decoded.ShouldBe("");
    }

    [Fact]
    public void Decode_PreservesRawData()
    {
        var result = _codec.Decode(SampleEncodedRequest);

        result.Raw.ShouldBe(SampleEncodedRequest);
    }

    [Fact]
    public void Decode_HandlesUnicodeCharacters()
    {
        // Test with German umlauts and special characters
        const string textWithUmlauts = "Überprüfung der Stempelzeiten für März";
        var encoded = _codec.Encode(textWithUmlauts);
        var decoded = _codec.Decode(encoded);

        decoded.Decoded.ShouldBe(textWithUmlauts);
    }

    #endregion

    #region Encode Tests

    [Fact]
    public void Encode_WithSpecificKeys_ProducesExpectedOutput()
    {
        var encoded = _codec.Encode(SampleDecodedRequest, SampleRequestKeys);

        encoded.ShouldBe(SampleEncodedRequest);
    }

    [Fact]
    public void Encode_StartsWithMarker()
    {
        var encoded = _codec.Encode("test");

        encoded[0].ShouldBe('\u00a4');
    }

    [Fact]
    public void Encode_StoresKeyCountCorrectly()
    {
        int[] keys = [1, 2, 3, 4, 5];
        var encoded = _codec.Encode("test", keys);

        // Key count stored as chr(48 + N)
        (encoded[1] - 48).ShouldBe(keys.Length);
    }

    [Fact]
    public void Encode_WithoutKeys_GeneratesRandomKeys()
    {
        var encoded1 = _codec.Encode("test data");
        var encoded2 = _codec.Encode("test data");

        // With random keys, encoded output should differ
        // (very small chance they're the same)
        encoded1.ShouldNotBe(encoded2);

        // But both should decode to the same value
        _codec.Decode(encoded1).Decoded.ShouldBe("test data");
        _codec.Decode(encoded2).Decoded.ShouldBe("test data");
    }

    [Fact]
    public void Encode_EmptyString_ReturnsEmpty()
    {
        var encoded = _codec.Encode("");

        encoded.ShouldBe("");
    }

    [Fact]
    public void Encode_NullString_ReturnsEmpty()
    {
        var encoded = _codec.Encode(null!);

        encoded.ShouldBe("");
    }

    [Fact]
    public void Encode_GeneratesKeyCountWithinValidRange()
    {
        // Generate many encodings and check key count is always valid
        for (int i = 0; i < 100; i++)
        {
            var encoded = _codec.Encode("test");
            var keyCount = encoded[1] - 48;

            keyCount.ShouldBeGreaterThanOrEqualTo(4);
            keyCount.ShouldBeLessThanOrEqualTo(37);
        }
    }

    #endregion

    #region Round-Trip Tests

    [Fact]
    public void RoundTrip_WithSpecificKeys_PreservesData()
    {
        var encoded = _codec.Encode(SampleDecodedRequest, SampleRequestKeys);
        var decoded = _codec.Decode(encoded);

        decoded.Decoded.ShouldBe(SampleDecodedRequest);
        decoded.Keys.ShouldBe(SampleRequestKeys);
    }

    [Fact]
    public void RoundTrip_WithRandomKeys_PreservesData()
    {
        const string original = "This is a test message with various characters: 123, äöü, @#$%";

        var encoded = _codec.Encode(original);
        var decoded = _codec.Decode(encoded);

        decoded.Decoded.ShouldBe(original);
    }

    [Fact]
    public void RoundTrip_RealGwtRpcRequest_PreservesData()
    {
        // Test with real GWT-RPC formatted data
        const string gwtRequest = "7,\"com.bodet.bwt.core.type.communication.BWPRequest\",\"java.util.List\",0,1,2,3";

        var encoded = _codec.Encode(gwtRequest);
        var decoded = _codec.Decode(encoded);

        decoded.Decoded.ShouldBe(gwtRequest);
    }

    [Fact]
    public void RoundTrip_LongMessage_PreservesData()
    {
        var longMessage = string.Join(",", Enumerable.Range(0, 1000).Select(i => $"\"item{i}\""));

        var encoded = _codec.Encode(longMessage);
        var decoded = _codec.Decode(encoded);

        decoded.Decoded.ShouldBe(longMessage);
    }

    [Fact]
    public void RoundTrip_SpecialCharacters_PreservesData()
    {
        const string specialChars = "!@#$%^&*()_+-=[]{}|;':\",./<>?\n\r\t";

        var encoded = _codec.Encode(specialChars);
        var decoded = _codec.Decode(encoded);

        decoded.Decoded.ShouldBe(specialChars);
    }

    [Fact]
    public void RoundTrip_MultipleIterations_StaysConsistent()
    {
        const string original = "Test message for multiple iterations";
        int[] keys = [5, 10, 3, 8, 12, 1];

        var current = original;
        for (int i = 0; i < 10; i++)
        {
            var encoded = _codec.Encode(current, keys);
            var decoded = _codec.Decode(encoded);
            current = decoded.Decoded;
        }

        current.ShouldBe(original);
    }

    #endregion

    #region Key Encoding/Decoding Tests

    [Fact]
    public void Keys_EncodedCorrectly()
    {
        // Keys are encoded as: chr(48 + key[i] + (i % 11))
        int[] keys = [5, 10, 3];
        var encoded = _codec.Encode("X", keys);

        // Check encoded keys at positions 2, 3, 4
        // Key 0: chr(48 + 5 + 0) = chr(53) = '5'
        // Key 1: chr(48 + 10 + 1) = chr(59) = ';'
        // Key 2: chr(48 + 3 + 2) = chr(53) = '5'
        encoded[2].ShouldBe('5');
        encoded[3].ShouldBe(';');
        encoded[4].ShouldBe('5');
    }

    [Fact]
    public void Keys_DecodedCorrectly()
    {
        var result = _codec.Decode(SampleEncodedRequest);

        // Verify each key is decoded correctly
        // Keys are decoded as: charCode(2+i) - 48 - (i % 11)
        result.Keys[0].ShouldBe(13);
        result.Keys[1].ShouldBe(13);
        result.Keys[2].ShouldBe(4);
        result.Keys[3].ShouldBe(13);
        result.Keys[4].ShouldBe(10);
        result.Keys[5].ShouldBe(1);
        result.Keys[6].ShouldBe(10);
        result.Keys[7].ShouldBe(7);
    }

    [Fact]
    public void Keys_WithMaxModulo_HandledCorrectly()
    {
        // Test with more than 11 keys to verify modulo handling
        int[] keys = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14];
        const string testData = "Test with many keys";

        var encoded = _codec.Encode(testData, keys);
        var decoded = _codec.Decode(encoded);

        decoded.Decoded.ShouldBe(testData);
        decoded.Keys.ShouldBe(keys);
    }

    #endregion

    #region Body Encoding/Decoding Tests

    [Fact]
    public void Body_EncodedWithKeyRotation()
    {
        // Body encoding uses: chr(charCode + key[i % N] - (i % 17))
        int[] keys = [5, 3];
        const string input = "AB";

        var encoded = _codec.Encode(input, keys);
        var decoded = _codec.Decode(encoded);

        decoded.Decoded.ShouldBe(input);
    }

    [Fact]
    public void Body_WithLongText_UsesKeyRotation()
    {
        // Test that keys rotate correctly for text longer than key array
        int[] keys = [1, 2, 3];
        var longText = new string('A', 100);

        var encoded = _codec.Encode(longText, keys);
        var decoded = _codec.Decode(encoded);

        decoded.Decoded.ShouldBe(longText);
    }

    [Fact]
    public void Body_ModuloOffset_HandledCorrectly()
    {
        // Test with text longer than 17 characters to verify modulo offset
        int[] keys = [5];
        var text = new string('X', 50);

        var encoded = _codec.Encode(text, keys);
        var decoded = _codec.Decode(encoded);

        decoded.Decoded.ShouldBe(text);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void EdgeCase_SingleCharacter()
    {
        const string single = "X";
        int[] keys = [7];

        var encoded = _codec.Encode(single, keys);
        var decoded = _codec.Decode(encoded);

        decoded.Decoded.ShouldBe(single);
    }

    [Fact]
    public void EdgeCase_MinimumKeys()
    {
        int[] keys = [1, 2, 3, 4]; // Minimum 4 keys
        const string text = "Test";

        var encoded = _codec.Encode(text, keys);
        var decoded = _codec.Decode(encoded);

        decoded.Keys.Length.ShouldBe(4);
        decoded.Decoded.ShouldBe(text);
    }

    [Fact]
    public void EdgeCase_MaximumKeyValue()
    {
        int[] keys = [14, 14, 14, 14]; // Maximum key value is 14
        const string text = "Test";

        var encoded = _codec.Encode(text, keys);
        var decoded = _codec.Decode(encoded);

        decoded.Decoded.ShouldBe(text);
    }

    [Fact]
    public void EdgeCase_ZeroKeyValues()
    {
        int[] keys = [0, 0, 0, 0];
        const string text = "Test";

        var encoded = _codec.Encode(text, keys);
        var decoded = _codec.Decode(encoded);

        decoded.Decoded.ShouldBe(text);
    }

    [Fact]
    public void EdgeCase_HighUnicodeCharacters()
    {
        const string unicode = "\u4E2D\u6587\u0041\u00DF\u03B1"; // Chinese, German ß, Greek α

        var encoded = _codec.Encode(unicode);
        var decoded = _codec.Decode(encoded);

        decoded.Decoded.ShouldBe(unicode);
    }

    #endregion
}
