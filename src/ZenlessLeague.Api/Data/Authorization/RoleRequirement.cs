using Microsoft.AspNetCore.Authorization;
using ZenlessLeague.Api.Features.Players;

namespace ZenlessLeague.Api.Data.Authorization;

public class RoleRequirement(PlayerRole minRole) : IAuthorizationRequirement
{
    public PlayerRole MinRole { get; } = minRole;
}
