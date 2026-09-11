namespace ZenlessLeague.Api.Features.Discord;

public class DiscordOptions
{
	public string? BotToken { get; set; }
	public string? GuildId { get; set; }
	public string? ClientId { get; set; }
	public string? ClientSecret { get; set; }
	public string? NewbieRoleId { get; set; }
	public string? MidRoleId { get; set; }
	public string? HighRoleId { get; set; }
	public List<string> TournamentStaffRoleIds { get; set; } = [];
	public string? TournamentCategoryId { get; set; }
}
