using Elogio.Persistence.Protocol;
using Shouldly;
using Xunit;

namespace Elogio.Persistence.Tests.Protocol;

/// <summary>
/// Tests for GWT-RPC tokenizer.
/// </summary>
public class GwtRpcTokenizerTests
{
    private readonly GwtRpcTokenizer _tokenizer = new();

    #region Basic Tokenization

    [Fact]
    public void Tokenize_SimpleMessage_ParsesCorrectly()
    {
        const string input = "3,\"Hello\",\"World\",\"Test\",0,1,2";

        var result = _tokenizer.Tokenize(input);

        result.StringTable.Length.ShouldBe(3);
        result.StringTable[0].ShouldBe("Hello");
        result.StringTable[1].ShouldBe("World");
        result.StringTable[2].ShouldBe("Test");
        result.DataTokens.Count.ShouldBe(3);
    }

    [Fact]
    public void Tokenize_QuotedStrings_HandlesEscapes()
    {
        const string input = "2,\"Hello\\\"World\",\"Tab\\tHere\",0";

        var result = _tokenizer.Tokenize(input);

        result.StringTable[0].ShouldBe("Hello\"World");
        result.StringTable[1].ShouldBe("Tab\tHere");
    }

    [Fact]
    public void Tokenize_EmptyString_ReturnsEmptyMessage()
    {
        var result = _tokenizer.Tokenize("");

        result.StringTable.ShouldBeEmpty();
        result.DataTokens.ShouldBeEmpty();
    }

    [Fact]
    public void Tokenize_NullString_ReturnsEmptyMessage()
    {
        var result = _tokenizer.Tokenize(null!);

        result.StringTable.ShouldBeEmpty();
        result.DataTokens.ShouldBeEmpty();
    }

    #endregion

    #region Token Types

    [Fact]
    public void Tokenize_IntegerTokens_IdentifiedCorrectly()
    {
        const string input = "0,42,-10,2147483647";

        var result = _tokenizer.Tokenize(input);

        result.DataTokens[0].Type.ShouldBe(GwtRpcTokenType.Integer);
        result.DataTokens[0].Value.ShouldBe("42");
        result.DataTokens[1].Type.ShouldBe(GwtRpcTokenType.Integer);
        result.DataTokens[1].Value.ShouldBe("-10");
    }

    [Fact]
    public void Tokenize_FloatTokens_IdentifiedCorrectly()
    {
        const string input = "0,3.14,0.5,-1.23";

        var result = _tokenizer.Tokenize(input);

        result.DataTokens[0].Type.ShouldBe(GwtRpcTokenType.Float);
        result.DataTokens[0].AsDouble().ShouldBe(3.14);
    }

    [Fact]
    public void Tokenize_IdentifierTokens_IdentifiedCorrectly()
    {
        const string input = "0,NULL,ENUM,ARRAY1_Type";

        var result = _tokenizer.Tokenize(input);

        result.DataTokens[0].Type.ShouldBe(GwtRpcTokenType.Identifier);
        result.DataTokens[0].Value.ShouldBe("NULL");
    }

    #endregion

    #region Real GWT-RPC Data

    [Fact]
    public void Tokenize_RealBwpResponse_ParsesStringTable()
    {
        const string input = "7,\"com.bodet.bwt.core.type.communication.BWPResponse\",\"NULL\",\"java.util.Map\",\"java.lang.String\",\"key1\",\"value1\",\"key2\",0,1,2,3,4";

        var result = _tokenizer.Tokenize(input);

        result.StringTable.Length.ShouldBe(7);
        result.StringTable[0].ShouldBe("com.bodet.bwt.core.type.communication.BWPResponse");
        result.IsResponse.ShouldBeTrue();
        result.IsRequest.ShouldBeFalse();
    }

    [Fact]
    public void Tokenize_RealBwpRequest_ParsesStringTable()
    {
        const string input = "7,\"com.bodet.bwt.core.type.communication.BWPRequest\",\"java.util.List\",\"java.lang.Integer\",\"java.lang.String\",\"session-id\",\"getMethod\",\"Service\",0,1,2";

        var result = _tokenizer.Tokenize(input);

        result.IsRequest.ShouldBeTrue();
        result.IsResponse.ShouldBeFalse();
    }

    [Fact]
    public void Tokenize_SemainePresenceBWT_FindsResponseType()
    {
        const string input = "5,\"com.bodet.bwt.core.type.communication.BWPResponse\",\"NULL\",\"com.bodet.bwt.app.portail.serveur.domain.declaration.presence.SemainePresenceBWT\",\"[Z\",\"java.lang.Boolean\",0,1,2";

        var result = _tokenizer.Tokenize(input);

        result.ResponseType.ShouldBe("com.bodet.bwt.app.portail.serveur.domain.declaration.presence.SemainePresenceBWT");
    }

    #endregion

    #region GetString Helper

    [Fact]
    public void GetString_ValidIndex_ReturnsString()
    {
        const string input = "3,\"First\",\"Second\",\"Third\",0";

        var result = _tokenizer.Tokenize(input);

        result.GetString(1).ShouldBe("First");
        result.GetString(2).ShouldBe("Second");
        result.GetString(3).ShouldBe("Third");
    }

    [Fact]
    public void GetString_InvalidIndex_ReturnsNull()
    {
        const string input = "2,\"First\",\"Second\",0";

        var result = _tokenizer.Tokenize(input);

        result.GetString(0).ShouldBeNull();
        result.GetString(3).ShouldBeNull();
        result.GetString(-1).ShouldBeNull();
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void Tokenize_EmptyStrings_HandledCorrectly()
    {
        const string input = "3,\"\",\"value\",\"\",0";

        var result = _tokenizer.Tokenize(input);

        result.StringTable[0].ShouldBe("");
        result.StringTable[1].ShouldBe("value");
        result.StringTable[2].ShouldBe("");
    }

    [Fact]
    public void Tokenize_UnicodeStrings_HandledCorrectly()
    {
        const string input = "2,\"Übersicht\",\"日本語\",0";

        var result = _tokenizer.Tokenize(input);

        result.StringTable[0].ShouldBe("Übersicht");
        result.StringTable[1].ShouldBe("日本語");
    }

    [Fact]
    public void Tokenize_LargeNumbers_HandledCorrectly()
    {
        const string input = "0,20260105,2147483647,126000";

        var result = _tokenizer.Tokenize(input);

        result.DataTokens[0].AsInt().ShouldBe(20260105);
        result.DataTokens[1].AsLong().ShouldBe(2147483647);
        result.DataTokens[2].AsInt().ShouldBe(126000);
    }

    #endregion
}
