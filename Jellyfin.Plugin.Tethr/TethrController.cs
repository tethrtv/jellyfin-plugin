using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Tethr.Api;
using Jellyfin.Plugin.Tethr.Configuration;
using MediaBrowser.Model.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Tethr;

public record Settings(string ServerUrl, bool MarkPlayed);

public record Connection(Guid UserId, string ProfileName, DateTime? Imported);

public record SettingsView(string ServerUrl, bool MarkPlayed, Connection[] Connections);

public record SignInView(string Status, string? UserCode = null, string? VerificationUrl = null, int? Interval = null, string? ProfileName = null);

[ApiController]
[Route("Tethr")]
[Authorize(Policy = "RequiresElevation")]
public class TethrController(TethrClient tethr, ITaskManager tasks) : ControllerBase
{
    private static readonly ConcurrentDictionary<Guid, string> Pending = new();

    [HttpGet("Settings")]
    public SettingsView GetSettings()
    {
        var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        var connections = Links.All().Select(l => new Connection(l.UserId, l.ProfileName, l.Imported)).ToArray();
        return new SettingsView(config.ServerUrl, config.MarkPlayed, connections);
    }

    [HttpPost("Settings")]
    public NoContentResult SaveSettings([FromBody] Settings settings)
    {
        Links.Settings(settings.ServerUrl, settings.MarkPlayed);
        return NoContent();
    }

    [HttpPost("Users/{userId}/SignIn")]
    public async Task<ActionResult<SignInView>> StartSignIn(Guid userId, CancellationToken ct)
    {
        try
        {
            var code = await tethr.StartSignIn(ct).ConfigureAwait(false);
            Pending[userId] = code.DeviceCode;
            return new SignInView("waiting", code.UserCode, code.VerificationUrl, code.Interval);
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException)
        {
            return StatusCode(502, new SignInView("unreachable"));
        }
    }

    [HttpGet("Users/{userId}/SignIn")]
    public async Task<SignInView> PollSignIn(Guid userId, CancellationToken ct)
    {
        if (!Pending.TryGetValue(userId, out var deviceCode))
        {
            return new SignInView("expired");
        }

        Tokens? tokens;
        try
        {
            tokens = await tethr.PollSignIn(deviceCode, ct).ConfigureAwait(false);
        }
        catch (SignInEndedException ex)
        {
            Pending.TryRemove(userId, out _);
            return new SignInView(ex.Status == (HttpStatusCode)418 ? "denied" : "expired");
        }

        if (tokens is null)
        {
            return new SignInView("waiting");
        }

        Pending.TryRemove(userId, out _);
        var link = new UserLink
        {
            UserId = userId,
            AccessToken = tokens.AccessToken,
            RefreshToken = tokens.RefreshToken,
            ExpiresAt = DateTime.UtcNow.AddSeconds(tokens.ExpiresIn),
        };
        link.ProfileName = (await tethr.Profile(link, ct).ConfigureAwait(false)).Name;
        Links.Save(link);
        tasks.QueueScheduledTask<ImportTask>();
        return new SignInView("connected", ProfileName: link.ProfileName);
    }

    [HttpDelete("Users/{userId}")]
    public async Task<NoContentResult> Disconnect(Guid userId, CancellationToken ct)
    {
        if (Links.For(userId) is { } link)
        {
            Links.Remove(userId);
            try
            {
                await tethr.SignOut(link, ct).ConfigureAwait(false);
            }
            catch (System.Net.Http.HttpRequestException)
            {
            }
        }

        return NoContent();
    }

    [HttpPost("Import")]
    public NoContentResult Import()
    {
        tasks.QueueScheduledTask<ImportTask>();
        return NoContent();
    }
}
