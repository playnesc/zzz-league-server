using ZenlessLeague.Api.Features.Players;
using ZenlessLeague.Api.Infrastructure.Persistence;

namespace ZenlessLeague.Api.Tests;

// Shared player-seeding helper — Player.Id (not Uid) is the real PK/FK target now, so every test
// that seeds a player and then references it from a dependent row needs the generated Id, not
// just the mnemonic Uid string. Centralizing this avoids hand-rolling Guid wiring in every file.
public static class TestPlayers
{
	public static Player Create(AppDbContext context, string uid, int elo = 1000, bool midConfirmed = false, bool highConfirmed = false)
	{
		var player = new Player
		{
			Id = Guid.NewGuid(),
			Uid = uid,
			Name = uid,
			Elo = elo,
			IsMidConfirmed = midConfirmed,
			IsHighConfirmed = highConfirmed,
			CreatedAt = DateTimeOffset.UtcNow,
		};
		context.Players.Add(player);
		return player;
	}
}
