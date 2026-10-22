using System.Security.Claims;

namespace ZenlessLeague.Api.Infrastructure.Authorization;

public static class AuthenticatedUserExtensions
{
	// The uid lives in the standard OIDC "sub" claim — true for Firebase ID tokens today, and
	// for any other OIDC-compliant provider (Keycloak, Auth0, a future self-issued JWT) we might
	// swap in later, since JwtBearer's Authority-based validation doesn't remap claim names.
	// "user_id" (Firebase-specific) and ClaimTypes.NameIdentifier are fallbacks only.
	public static string? GetUid(this ClaimsPrincipal user) =>
		user.FindFirstValue("sub") ?? user.FindFirstValue("user_id") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
}
