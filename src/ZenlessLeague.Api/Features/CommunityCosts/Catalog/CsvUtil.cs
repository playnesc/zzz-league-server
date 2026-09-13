using System.Text;

namespace ZenlessLeague.Api.Features.CommunityCosts.Catalog;

// Minimal RFC4180-ish CSV helpers shared by the agent/engine cost import/export endpoints. No
// CsvHelper dependency — both formats are small and fixed-shape (see NESC_agent_costs.csv /
// NESC_engine_costs.csv), so hand-rolling avoids a package for eight columns.
internal static class CsvUtil
{
	public static string EscapeField(string value)
	{
		if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
		{
			return $"\"{value.Replace("\"", "\"\"")}\"";
		}

		return value;
	}

	// Splits one CSV line into fields, honoring double-quoted fields with embedded commas and
	// doubled-quote escaping. Doesn't handle embedded newlines inside a quoted field — callers
	// split the whole CSV into lines first, which is sufficient for this data.
	public static string[] SplitLine(string line)
	{
		var fields = new List<string>();
		var current = new StringBuilder();
		var inQuotes = false;

		for (var i = 0; i < line.Length; i++)
		{
			var c = line[i];
			if (inQuotes)
			{
				if (c == '"')
				{
					if (i + 1 < line.Length && line[i + 1] == '"')
					{
						current.Append('"');
						i++;
					}
					else
					{
						inQuotes = false;
					}
				}
				else
				{
					current.Append(c);
				}
			}
			else if (c == '"')
			{
				inQuotes = true;
			}
			else if (c == ',')
			{
				fields.Add(current.ToString());
				current.Clear();
			}
			else
			{
				current.Append(c);
			}
		}

		fields.Add(current.ToString());
		return [.. fields];
	}
}
