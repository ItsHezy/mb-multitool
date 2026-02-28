using Membran.MultiTool.Osint.Services;

namespace Membran.MultiTool.Tests;

public sealed class PhoneParserServiceTests
{
    [Fact]
    public void Parse_ReturnsStructuredResult_ForValidNumber()
    {
        var parser = new PhoneParserService();

        var result = parser.Parse("+14155552671");

        Assert.True(string.IsNullOrWhiteSpace(result.Error));
        Assert.StartsWith("+1", result.E164);
        Assert.True(result.IsPossible);
    }

    [Fact]
    public void Parse_ReturnsError_ForInvalidInput()
    {
        var parser = new PhoneParserService();

        var result = parser.Parse("not-a-phone-number");

        Assert.False(string.IsNullOrWhiteSpace(result.Error));
        Assert.False(result.IsValid);
    }
}
