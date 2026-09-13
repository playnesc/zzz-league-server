using System.Globalization;
using ZenlessLeague.Api.Data;
using ZenlessLeague.Api.Features.CommunityCosts.Catalog;

namespace ZenlessLeague.Api.Features.CommunityCosts.Engines;

public class EngineCostCsvException(string message) : ValidationApiException(message);

public record ParsedEngineCostRow(string EngineName, string? OverrideAgentName, int[] Costs);

// Hand-rolled export/import for the exact NESC_engine_costs.csv shape:
// "Engine,R1,R2,R3,R4,R5,off,bis" — a blank "bis" is the engine's own base row, a named "bis" is
// a per-agent override row. Both row kinds carry the same 6 cost values (R1-R5 + the unlabeled
// "off" 6th value) for CSV fidelity, even though the frontend only ever displays 5 for overrides.
public static class EngineCostCsv
{
	private const string Header = "Engine,R1,R2,R3,R4,R5,off,bis";

	public static string Export(List<EngineCatalogEntry> engineCatalog, Dictionary<string, string> agentNamesById, List<EngineCostRow> rows)
	{
		var rowsByEngine = rows.GroupBy(r => r.EngineId).ToDictionary(g => g.Key, g => g.ToList());
		var lines = new List<string> { Header };

		foreach (var engine in engineCatalog.OrderBy(e => e.SortOrder))
		{
			rowsByEngine.TryGetValue(engine.Id, out var engineRows);
			engineRows ??= [];

			var baseRow = engineRows.FirstOrDefault(r => r.AgentId is null);
			lines.Add(FormatRow(engine.Name, baseRow?.Costs ?? new int[6], null));

			foreach (var overrideRow in engineRows.Where(r => r.AgentId is not null))
			{
				var agentName = agentNamesById.TryGetValue(overrideRow.AgentId!, out var name) ? name : overrideRow.AgentId!;
				lines.Add(FormatRow(engine.Name, overrideRow.Costs, agentName));
			}
		}

		return string.Join("\r\n", lines) + "\r\n";
	}

	private static string FormatRow(string engineName, int[] costs, string? bis)
	{
		var fields = new List<string> { CsvUtil.EscapeField(engineName) };
		fields.AddRange(costs.Select(c => c.ToString(CultureInfo.InvariantCulture)));
		fields.Add(bis is null ? "" : CsvUtil.EscapeField(bis));
		return string.Join(',', fields);
	}

	// All-or-nothing: throws on the first malformed row rather than partially applying (plan §7
	// open risk 6). Name resolution happens in the caller (service layer).
	public static List<ParsedEngineCostRow> Parse(string csv)
	{
		var lines = csv.Replace("\r\n", "\n").Split('\n').Where(l => l.Length > 0).ToList();
		if (lines.Count == 0 || lines[0] != Header)
		{
			throw new EngineCostCsvException($"Expected header '{Header}'.");
		}

		var result = new List<ParsedEngineCostRow>();
		foreach (var line in lines.Skip(1))
		{
			var fields = CsvUtil.SplitLine(line);
			if (fields.Length != 8)
			{
				throw new EngineCostCsvException($"Malformed row (expected 8 columns, got {fields.Length}): {line}");
			}

			var name = fields[0];
			var costs = new int[6];
			for (var i = 0; i < 6; i++)
			{
				if (!int.TryParse(fields[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out costs[i]))
				{
					throw new EngineCostCsvException($"Malformed cost value '{fields[i + 1]}' for engine '{name}'.");
				}
			}

			var bis = fields[7].Length == 0 ? null : fields[7];
			result.Add(new ParsedEngineCostRow(name, bis, costs));
		}

		return result;
	}
}
