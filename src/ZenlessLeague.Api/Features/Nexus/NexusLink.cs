using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace ZenlessLeague.Api.Features.Nexus;

// Only links to nexus-shiyu.com result pages are accepted — the API rebuilds the fetch URL from the
// extracted id itself, so a pasted link can never make the server request an arbitrary address.
public static partial class NexusLink
{
	[GeneratedRegex(@"^https?://(www\.)?nexus-shiyu\.com/results/(?<id>[a-z0-9]{10,40})(/obs)?/?([?#].*)?$", RegexOptions.IgnoreCase)]
	private static partial Regex ResultLinkRegex();

	public static bool TryParseId(string? url, [NotNullWhen(true)] out string? resultId)
	{
		var match = url is null ? null : ResultLinkRegex().Match(url.Trim());
		resultId = match is { Success: true } ? match.Groups["id"].Value.ToLowerInvariant() : null;
		return resultId is not null;
	}
}
