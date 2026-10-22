using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using ZenlessLeague.Api.Features.Players;
using ZenlessLeague.Api.Infrastructure.Persistence;

namespace ZenlessLeague.Api.Infrastructure.Authorization;

// Ports functions/utils/roles.js's ROLE_RANK/hasRole check: role lives on the player row and
// is read fresh on every request, not baked into the JWT — so a role change (promote/demote)
// takes effect immediately without waiting for the client to refresh its Firebase ID token.
public class RoleAuthorizationHandler(AppDbContext dbContext) : AuthorizationHandler<RoleRequirement>
{
	protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, RoleRequirement requirement)
	{
		var uid = context.User.GetUid();
		if (uid is null)
		{
			return;
		}

		var role = await dbContext.Players.AsNoTracking()
			.Where(p => p.Uid == uid)
			.Select(p => (PlayerRole?)p.Role)
			.FirstOrDefaultAsync();

		if (role is not null && role.Value >= requirement.MinRole)
		{
			context.Succeed(requirement);
		}
	}
}
