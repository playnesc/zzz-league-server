using System.Globalization;
using ZenlessLeague.Api.Data;
using ZenlessLeague.Api.Features.CommunityCosts.Catalog;

namespace ZenlessLeague.Api.Features.CommunityCosts.Agents;

public class AgentCostCsvException(string message) : ValidationApiException(message);

public record ParsedAgentCostRow(string AgentName, int? PotentialTier, int[] Costs);

// Hand-rolled export/import for the exact NESC_agent_costs.csv shape: "Agent,M0,M1,M2,M3,M4,M5,M6,P".
// P semantics (plan §7 open risk 2): null = a single row is the agent's whole M0-M6 cost; 0 = the
// base row when tiers exist; 1-6 = additive tier-overlay rows.
public static class AgentCostCsv
{
	private const string Header = "Agent,M0,M1,M2,M3,M4,M5,M6,P";

	public static string Export(List<AgentCatalogEntry> catalog, List<AgentCostRow> rows)
	{
		var rowsByAgent = rows
			.GroupBy(r => r.AgentId)
			.ToDictionary(g => g.Key, g => g.OrderBy(r => r.PotentialTier ?? -1).ToList());

		var lines = new List<string> { Header };
		foreach (var agent in catalog.OrderBy(a => a.SortOrder))
		{
			if (!rowsByAgent.TryGetValue(agent.Id, out var agentRows))
			{
				continue;
			}

			foreach (var row in agentRows)
			{
				var p = row.PotentialTier is null ? "null" : row.PotentialTier.Value.ToString(CultureInfo.InvariantCulture);
				var fields = new List<string> { CsvUtil.EscapeField(agent.Name) };
				fields.AddRange(row.Costs.Select(c => c.ToString(CultureInfo.InvariantCulture)));
				fields.Add(p);
				lines.Add(string.Join(',', fields));
			}
		}

		return string.Join("\r\n", lines) + "\r\n";
	}

	// All-or-nothing: throws on the first malformed row or unresolvable name rather than
	// partially applying (plan §7 open risk 6). Name resolution happens in the caller (service
	// layer), which has the catalog loaded.
	public static List<ParsedAgentCostRow> Parse(string csv)
	{
		var lines = csv.Replace("\r\n", "\n").Split('\n').Where(l => l.Length > 0).ToList();
		if (lines.Count == 0 || lines[0] != Header)
		{
			throw new AgentCostCsvException($"Expected header '{Header}'.");
		}

		var result = new List<ParsedAgentCostRow>();
		foreach (var line in lines.Skip(1))
		{
			var fields = CsvUtil.SplitLine(line);
			if (fields.Length != 9)
			{
				throw new AgentCostCsvException($"Malformed row (expected 9 columns, got {fields.Length}): {line}");
			}

			var name = fields[0];
			var costs = new int[7];
			for (var i = 0; i < 7; i++)
			{
				if (!int.TryParse(fields[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out costs[i]))
				{
					throw new AgentCostCsvException($"Malformed cost value '{fields[i + 1]}' for agent '{name}'.");
				}
			}

			int? tier = null;
			if (!string.Equals(fields[8], "null", StringComparison.OrdinalIgnoreCase))
			{
				if (!int.TryParse(fields[8], NumberStyles.Integer, CultureInfo.InvariantCulture, out var tierValue))
				{
					throw new AgentCostCsvException($"Malformed P value '{fields[8]}' for agent '{name}'.");
				}

				tier = tierValue;
			}

			result.Add(new ParsedAgentCostRow(name, tier, costs));
		}

		return result;
	}
}
