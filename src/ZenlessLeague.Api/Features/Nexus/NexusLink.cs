using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace ZenlessLeague.Api.Features.Nexus;

// Only links to Nexus draft/result pages (zzz.nexus, formerly nexus-shiyu.com) are accepted — any of
// /drafts/{id}, /results/{id}, /results/{id}/obs, with or without a query (?DraftKey=…,
// ?adminToken=…, which are ignored and never stored). The API rebuilds the fetch URL from the
// extracted id itself, so a pasted link can never make the server request an arbitrary address.
// Keep in sync with NEXUS_LINK_PATTERN in the frontend's TournamentMatchPopup.
public static partial class NexusLink
{
	[GeneratedRegex(@"^https?://(www\.)?(zzz\.nexus|nexus-shiyu\.com)/(drafts|results)/(?<id>[a-z0-9]{10,40})(/obs)?/?([?#].*)?$", RegexOptions.IgnoreCase)]
	private static partial Regex ResultLinkRegex();

	public static bool TryParseId(string? url, [NotNullWhen(true)] out string? resultId)
	{
		var match = url is null ? null : ResultLinkRegex().Match(url.Trim());
		resultId = match is { Success: true } ? match.Groups["id"].Value.ToLowerInvariant() : null;
		return resultId is not null;
	}
}
