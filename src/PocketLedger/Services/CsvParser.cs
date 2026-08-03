using System.Text;

namespace PocketLedger.Services;

public static class CsvParser
{
    public static IReadOnlyList<IReadOnlyList<string>> Parse(string content)
    {
        var rows = new List<IReadOnlyList<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;

        for (var index = 0; index < content.Length; index++)
        {
            var character = content[index];
            if (quoted)
            {
                if (character == '"' && index + 1 < content.Length && content[index + 1] == '"')
                {
                    field.Append('"');
                    index++;
                }
                else if (character == '"')
                {
                    quoted = false;
                }
                else
                {
                    field.Append(character);
                }
            }
            else if (character == '"')
            {
                quoted = true;
            }
            else if (character == ',')
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if (character is '\r' or '\n')
            {
                if (character == '\r' && index + 1 < content.Length && content[index + 1] == '\n') index++;
                row.Add(field.ToString());
                field.Clear();
                if (row.Any(value => value.Length > 0)) rows.Add(row);
                row = [];
            }
            else
            {
                field.Append(character);
            }
        }

        if (quoted) throw new BusinessRuleException("CSV contains an unterminated quoted field.");
        row.Add(field.ToString());
        if (row.Any(value => value.Length > 0)) rows.Add(row);
        return rows;
    }

    public static string Escape(string? value)
    {
        value ??= string.Empty;
        return value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r') ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }
}
