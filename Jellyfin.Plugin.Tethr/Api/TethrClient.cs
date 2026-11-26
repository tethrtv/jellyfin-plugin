using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Tethr.Configuration;
using MediaBrowser.Common.Net;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Tethr.Api;

public class SignInEndedException(HttpStatusCode status) : Exception("tethr sign-in ended with " + (int)status)
{
    public HttpStatusCode Status { get; } = status;
}

public class TethrClient(IHttpClientFactory http, ILogger<TethrClient> log)
{
    // Every Tethr server has this app built in, self-hosted ones included.
    private const string ClientId = "tethr-jellyfin";

    private const int MaxAttempts = 5;

    private static readonly TimeSpan MaxWait = TimeSpan.FromMinutes(1);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static PluginConfiguration Config => Plugin.Instance?.Configuration ?? new PluginConfiguration();

    public async Task<DeviceSignIn> StartSignIn(CancellationToken ct)
    {
        using var res = await Send(HttpMethod.Post, "/oauth/device/code", new { client_id = ClientId }, null, ct).ConfigureAwait(false);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<DeviceSignIn>(Json, ct).ConfigureAwait(false))!;
    }

    // Null means the person hasn't entered the code yet.
    public async Task<Tokens?> PollSignIn(string deviceCode, CancellationToken ct)
    {
        using var res = await Send(HttpMethod.Post, "/oauth/device/token", new { code = deviceCode, client_id = ClientId }, null, ct).ConfigureAwait(false);
        if (res.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.TooManyRequests)
        {
            return null;
        }

        if (!res.IsSuccessStatusCode)
        {
            throw new SignInEndedException(res.StatusCode);
        }

        return await res.Content.ReadFromJsonAsync<Tokens>(Json, ct).ConfigureAwait(false);
    }

    public async Task<Profile> Profile(UserLink link, CancellationToken ct)
    {
        using var res = await Authed(HttpMethod.Get, "/v1/profile", null, link, ct).ConfigureAwait(false);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<Profile>(Json, ct).ConfigureAwait(false))!;
    }

    public async Task Scrobble(UserLink link, ScrobbleRequest scrobble, CancellationToken ct)
    {
        using var res = await Authed(HttpMethod.Post, "/v1/scrobble", scrobble, link, ct, retry: false).ConfigureAwait(false);
        if (res.StatusCode is not (HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity or HttpStatusCode.NotFound))
        {
            res.EnsureSuccessStatusCode();
        }
    }

    public async Task AddHistory(UserLink link, IReadOnlyList<NewWatch> watches, CancellationToken ct)
    {
        using var res = await Authed(HttpMethod.Post, "/v1/history", new NewWatches(watches), link, ct).ConfigureAwait(false);
        res.EnsureSuccessStatusCode();
    }

    public async Task<Activity> Activity(UserLink link, CancellationToken ct)
    {
        using var res = await Authed(HttpMethod.Get, "/v1/activity", null, link, ct).ConfigureAwait(false);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<Activity>(Json, ct).ConfigureAwait(false))!;
    }

    public async Task<HistoryPage> History(UserLink link, int page, int limit, CancellationToken ct)
    {
        using var res = await Authed(HttpMethod.Get, $"/v1/history?page={page}&limit={limit}", null, link, ct).ConfigureAwait(false);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<HistoryPage>(Json, ct).ConfigureAwait(false))!;
    }

    public async Task SignOut(UserLink link, CancellationToken ct)
    {
        using var res = await Send(HttpMethod.Post, "/oauth/revoke", new { token = link.AccessToken, client_id = ClientId }, null, ct).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> Authed(HttpMethod method, string path, object? body, UserLink link, CancellationToken ct, bool retry = true)
    {
        for (var attempt = 1; ; attempt++)
        {
            if (link.ExpiresAt < DateTime.UtcNow.AddHours(1))
            {
                await Refresh(link, ct).ConfigureAwait(false);
            }

            var res = await Send(method, path, body, link.AccessToken, ct).ConfigureAwait(false);
            if (res.StatusCode is not HttpStatusCode.TooManyRequests || !retry || attempt == MaxAttempts)
            {
                return res;
            }

            var asked = res.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(1);
            var wait = (asked < MaxWait ? asked : MaxWait) + TimeSpan.FromMilliseconds(Random.Shared.Next(1000));
            res.Dispose();
            log.LogInformation("Tethr asked to slow down on {Method} {Path}, trying again in {Seconds:0.#}s (attempt {Attempt} of {Max})", method, path, wait.TotalSeconds, attempt + 1, MaxAttempts);
            await Task.Delay(wait, ct).ConfigureAwait(false);
        }
    }

    private async Task Refresh(UserLink link, CancellationToken ct)
    {
        var body = new { grant_type = "refresh_token", refresh_token = link.RefreshToken, client_id = ClientId };
        using var res = await Send(HttpMethod.Post, "/oauth/token", body, null, ct).ConfigureAwait(false);
        res.EnsureSuccessStatusCode();
        var tokens = (await res.Content.ReadFromJsonAsync<Tokens>(Json, ct).ConfigureAwait(false))!;
        link.AccessToken = tokens.AccessToken;
        link.RefreshToken = tokens.RefreshToken;
        link.ExpiresAt = DateTime.UtcNow.AddSeconds(tokens.ExpiresIn);
        Links.Save(link);
    }

    private async Task<HttpResponseMessage> Send(HttpMethod method, string path, object? body, string? token, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(method, Config.ServerUrl.TrimEnd('/') + path);
        if (body is not null)
        {
            req.Content = JsonContent.Create(body, body.GetType(), options: Json);
        }

        if (token is not null)
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        req.Headers.UserAgent.ParseAdd("jellyfin-plugin-tethr/" + typeof(TethrClient).Assembly.GetName().Version);
        return await http.CreateClient(NamedClient.Default).SendAsync(req, ct).ConfigureAwait(false);
    }
}
