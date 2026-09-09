namespace ZenlessLeague.Api.Features.Discord;

// Legacy hardcodes these guild-specific ids directly in functions/config/discordRoles.js —
// made configurable here since this deployment isn't tied to that Discord server.
public class DiscordOptions
{
    public string? BotToken { get; set; }
    public string? GuildId { get; set; }
    public string? NewbieRoleId { get; set; }
    public string? MidRoleId { get; set; }
    public string? HighRoleId { get; set; }
    public List<string> TournamentStaffRoleIds { get; set; } = [];
    public string? TournamentCategoryId { get; set; }
}
