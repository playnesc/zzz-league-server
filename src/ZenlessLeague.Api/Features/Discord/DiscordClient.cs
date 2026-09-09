using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace ZenlessLeague.Api.Features.Discord;

public record DiscordPermissionOverwrite(string Id, int Type, string? Allow = null, string? Deny = null);

public class DiscordApiException(int statusCode, string body) : Exception($"Discord API error ({statusCode}): {body}")
{
    public int StatusCode { get; } = statusCode;
}

public interface IDiscordClient
{
    Task AddMemberRoleAsync(string discordUserId, string roleId, CancellationToken ct = default);
    Task RemoveMemberRoleAsync(string discordUserId, string roleId, CancellationToken ct = default);
    Task<string> CreateGuildRoleAsync(string name, CancellationToken ct = default);
    Task DeleteGuildRoleAsync(string roleId, CancellationToken ct = default);

    Task<string> CreateGuildChannelAsync(
        string name, string? parentId, IReadOnlyList<DiscordPermissionOverwrite> permissionOverwrites, CancellationToken ct = default);

    Task DeleteGuildChannelAsync(string channelId, CancellationToken ct = default);
}

// REST client for the Discord bot API. Ports functions/discord/discordClient.js's ~7-endpoint
// surface (a plain REST wrapper in legacy too, built on @discordjs/rest — not the full discord.js
// client) using a typed HttpClient instead.
//
// NOTE: written directly from the legacy source but not yet exercised against a real Discord
// bot/guild (no bot token configured in this environment) — verify against a real server before
// relying on this for tournament role/channel automation.
public class DiscordClient(HttpClient httpClient, IOptions<DiscordOptions> options) : IDiscordClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task AddMemberRoleAsync(string discordUserId, string roleId, CancellationToken ct = default)
    {
        var guildId = RequireGuildId();
        var response = await SendAsync(HttpMethod.Put, $"guilds/{guildId}/members/{discordUserId}/roles/{roleId}", null, ct);
        await ThrowIfErrorAsync(response, ct);
    }

    public async Task RemoveMemberRoleAsync(string discordUserId, string roleId, CancellationToken ct = default)
    {
        var guildId = RequireGuildId();
        var response = await SendAsync(HttpMethod.Delete, $"guilds/{guildId}/members/{discordUserId}/roles/{roleId}", null, ct);
        await ThrowIfErrorAsync(response, ct);
    }

    public async Task<string> CreateGuildRoleAsync(string name, CancellationToken ct = default)
    {
        var guildId = RequireGuildId();
        var response = await SendAsync(HttpMethod.Post, $"guilds/{guildId}/roles", new { name, mentionable = true }, ct);
        return (await ReadAsync<DiscordResource>(response, ct)).Id;
    }

    public async Task DeleteGuildRoleAsync(string roleId, CancellationToken ct = default)
    {
        var guildId = RequireGuildId();
        var response = await SendAsync(HttpMethod.Delete, $"guilds/{guildId}/roles/{roleId}", null, ct);
        await ThrowIfErrorAsync(response, ct);
    }

    public async Task<string> CreateGuildChannelAsync(
        string name, string? parentId, IReadOnlyList<DiscordPermissionOverwrite> permissionOverwrites, CancellationToken ct = default)
    {
        var guildId = RequireGuildId();
        var response = await SendAsync(HttpMethod.Post, $"guilds/{guildId}/channels", new
        {
            name,
            type = 0,
            parent_id = parentId,
            permission_overwrites = permissionOverwrites.Select(o => new { id = o.Id, type = o.Type, allow = o.Allow, deny = o.Deny }),
        }, ct);
        return (await ReadAsync<DiscordResource>(response, ct)).Id;
    }

    public async Task DeleteGuildChannelAsync(string channelId, CancellationToken ct = default)
    {
        var response = await SendAsync(HttpMethod.Delete, $"channels/{channelId}", null, ct);
        await ThrowIfErrorAsync(response, ct);
    }

    private string RequireGuildId() =>
        options.Value.GuildId ?? throw new InvalidOperationException("Discord:GuildId is not configured.");

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        var botToken = options.Value.BotToken ?? throw new InvalidOperationException("Discord:BotToken is not configured.");

        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bot", botToken);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        return await httpClient.SendAsync(request, ct);
    }

    private static async Task ThrowIfErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        throw new DiscordApiException((int)response.StatusCode, body);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        await ThrowIfErrorAsync(response, ct);
        var result = await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct);
        return result ?? throw new InvalidOperationException("Discord API returned an empty response body.");
    }

    private record DiscordResource([property: JsonPropertyName("id")] string Id);
}
