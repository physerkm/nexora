namespace Nexora.Application.Common;

public static class CsvField
{
    /// <summary>
    /// Quotes a user-supplied CSV field and neutralises spreadsheet formula injection.
    /// A leading '=', '+', '-', '@', tab or carriage return makes Excel and LibreOffice treat the cell
    /// as a formula, so such values get a leading apostrophe (OWASP CSV Injection guidance).
    /// </summary>
    public static string Quote(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            value = "'" + value;
        }

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
