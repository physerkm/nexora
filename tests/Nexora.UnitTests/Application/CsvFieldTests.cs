using Nexora.Application.Common;

namespace Nexora.UnitTests.Application;

public class CsvFieldTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("Yıldız Market", "\"Yıldız Market\"")]
    [InlineData("Acme, \"Main\"", "\"Acme, \"\"Main\"\"\"")]
    [InlineData("line1\nline2", "\"line1\nline2\"")]
    public void Plain_values_are_quoted(string? value, string expected) =>
        Assert.Equal(expected, CsvField.Quote(value));

    [Theory]
    [InlineData("=HYPERLINK(\"http://x\")")]
    [InlineData("+1+1")]
    [InlineData("-1+1")]
    [InlineData("@SUM(A1)")]
    [InlineData("\t=1+1")]
    [InlineData("\r=1+1")]
    public void Formula_triggers_are_neutralised(string value) =>
        Assert.StartsWith("\"'", CsvField.Quote(value));
}
