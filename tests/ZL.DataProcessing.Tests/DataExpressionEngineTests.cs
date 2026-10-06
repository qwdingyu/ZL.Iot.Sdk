using ZL.DataProcessing;
using Xunit;

namespace ZL.DataProcessing.Tests;

/// <summary>
/// Tests for DataExpressionEngine — focused on discovering logic bugs in token replacement
/// and state management.
/// </summary>
public class DataExpressionEngineTests
{
    #region Token replacement — ARG

    [Fact]
    public void Process_ArgPlaceholder_ShouldReplace()
    {
        var engine = new DataExpressionEngine();
        var result = engine.Process("Value: {ARG}", "42", Array.Empty<string>());
        Assert.Equal("Value: 42", result);
    }

    [Fact]
    public void Process_ArgIndexPlaceholder_ShouldReplace()
    {
        var engine = new DataExpressionEngine();
        var result = engine.Process("{ARG0} and {ARG1}", "a b", new[] { "a", "b" });
        Assert.Equal("a and b", result);
    }

    [Fact]
    public void Process_ArgPlaceholder_CaseInsensitive_ShouldReplace()
    {
        var engine = new DataExpressionEngine();
        // {arg} should also work (case insensitive)
        var result = engine.Process("{arg}", "42", Array.Empty<string>());
        Assert.Equal("42", result);
    }

    #endregion

    #region State tokens — BUG: missing key returns "0"

    /// <summary>
    /// BUG: When a STATE key doesn't exist, ReplaceStateTokens returns "0" instead of empty string.
    /// This means {STATE:nonexistent} → "0" which can silently corrupt data.
    /// Compare with TemplateContext.GetState which returns "" for missing keys.
    /// This inconsistency between DataExpressionEngine and TemplateContext is a real bug.
    /// </summary>
    [Fact]
    public void Process_MissingStateKey_ReturnsZero_Bug()
    {
        var engine = new DataExpressionEngine();
        // "nonexistent" key is not set
        var result = engine.Process("{STATE:nonexistent}", "", Array.Empty<string>());
        // BUG: Returns "0" instead of "" — silently injects a default value
        Assert.Equal("0", result);
    }

    [Fact]
    public void Process_StateKey_Set_ShouldReturnCorrectValue()
    {
        var engine = new DataExpressionEngine();
        engine.SetState("voltage", "25.5");
        var result = engine.Process("{STATE:voltage}", "", Array.Empty<string>());
        Assert.Equal("25.5", result);
    }

    [Fact]
    public void Process_StateKey_CaseInsensitive_ShouldWork()
    {
        var engine = new DataExpressionEngine();
        engine.SetState("Voltage", "25.5");
        // {STATE:voltage} should match "Voltage" (case insensitive)
        var result = engine.Process("{STATE:voltage}", "", Array.Empty<string>());
        Assert.Equal("25.5", result);
    }

    #endregion

    #region Session tokens — same "0" bug

    /// <summary>
    /// Same bug as State tokens: missing SESSION key returns "0".
    /// </summary>
    [Fact]
    public void Process_MissingSessionKey_ReturnsZero_Bug()
    {
        var engine = new DataExpressionEngine();
        var result = engine.Process("{SESSION:nonexistent}", "", Array.Empty<string>());
        // BUG: Returns "0" instead of ""
        Assert.Equal("0", result);
    }

    [Fact]
    public void Process_SessionKey_Set_ShouldReturnCorrectValue()
    {
        var engine = new DataExpressionEngine();
        engine.SetSession("counter", "42");
        var result = engine.Process("{SESSION:counter}", "", Array.Empty<string>());
        Assert.Equal("42", result);
    }

    #endregion

    #region RAND tokens

    [Fact]
    public void Process_RandToken_SingleValue_ShouldReturnNumber()
    {
        var engine = new DataExpressionEngine();
        var result = engine.Process("{RAND:10}", "", Array.Empty<string>());
        // Should be a number between 0 and 10
        double num = double.Parse(result, System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(num, 0.0, 10.0);
    }

    [Fact]
    public void Process_RandToken_Range_ShouldReturnInRange()
    {
        var engine = new DataExpressionEngine();
        for (int i = 0; i < 10; i++)
        {
            var result = engine.Process("{RAND:5,15}", "", Array.Empty<string>());
            double num = double.Parse(result, System.Globalization.CultureInfo.InvariantCulture);
            Assert.InRange(num, 5.0, 15.0);
        }
    }

    /// <summary>
    /// BUG: DataExpressionEngine.ReplaceRandomTokens uses regex [^}]+ (one or more),
    /// so {RAND:} with empty content is NOT matched and returned as-is.
    /// This is the same bug that was fixed in StandardTemplateProcessor.
    /// The regex should be [^}]* (zero or more).
    /// </summary>
    [Fact]
    public void Process_RandToken_EmptyParam_NotMatched_Bug()
    {
        var engine = new DataExpressionEngine();
        var result = engine.Process("{RAND:}", "", Array.Empty<string>());
        // BUG: Returns "{RAND:}" unchanged because regex [^}]+ requires at least 1 char
        Assert.Equal("{RAND:}", result);
    }

    #endregion

    #region Session management

    [Fact]
    public void UseSession_SwitchesSession_ShouldIsolateData()
    {
        var engine = new DataExpressionEngine();
        engine.SetSession("key", "default_value");

        engine.UseSession("session2");
        engine.SetSession("key", "session2_value");

        // session2 should have its own value
        Assert.Equal("session2_value", engine.GetSession("key"));

        // Switch back to default
        engine.UseSession("default");
        Assert.Equal("default_value", engine.GetSession("key"));
    }

    [Fact]
    public void ResetSession_ShouldClearAllSessionData()
    {
        var engine = new DataExpressionEngine();
        engine.SetSession("a", "1");
        engine.SetSession("b", "2");

        engine.ResetSession();
        Assert.Null(engine.GetSession("a"));
        Assert.Null(engine.GetSession("b"));
    }

    [Fact]
    public void RemoveSession_ShouldRemoveSpecificSession()
    {
        var engine = new DataExpressionEngine();
        engine.UseSession("toRemove");
        engine.SetSession("key", "value");

        engine.RemoveSession("toRemove");
        // UseSession auto-creates removed sessions (no exception thrown)
        // After removal + re-use, data should be empty
        engine.UseSession("toRemove");
        Assert.Null(engine.GetSession("key"));
    }

    #endregion

    #region Null/empty handling

    [Fact]
    public void Process_NullTemplate_ShouldReturnEmpty()
    {
        var engine = new DataExpressionEngine();
        var result = engine.Process(null!, "", Array.Empty<string>());
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Process_EmptyTemplate_ShouldReturnEmpty()
    {
        var engine = new DataExpressionEngine();
        var result = engine.Process("", "", Array.Empty<string>());
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Process_NullArgsText_ShouldHandleGracefully()
    {
        var engine = new DataExpressionEngine();
        var result = engine.Process("{ARG}", null!, Array.Empty<string>());
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Process_NullGroups_ShouldHandleGracefully()
    {
        var engine = new DataExpressionEngine();
        var result = engine.Process("{ARG}", "hello", null!);
        Assert.Equal("hello", result);
    }

    #endregion

    #region ErrorResponse

    [Fact]
    public void ErrorResponse_WhenSet_ShouldReturnOnError()
    {
        var engine = new DataExpressionEngine();
        engine.ErrorResponse = "SCRIPT_ERROR";

        // Script that will fail
        var result = engine.Process("@invalid syntax here", "", Array.Empty<string>());
        Assert.Equal("SCRIPT_ERROR", result);
    }

    #endregion
}
