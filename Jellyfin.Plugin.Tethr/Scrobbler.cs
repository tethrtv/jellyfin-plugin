using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Tethr.Api;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Tethr;

public sealed class Scrobbler(ISessionManager sessions, IUserDataManager userData, TethrClient tethr, ILogger<Scrobbler> log) : IHostedService
{
    private readonly ConcurrentDictionary<string, bool> paused = new();

    public Task StartAsync(CancellationToken cancellationToken)
    {
        sessions.PlaybackStart += OnStart;
        sessions.PlaybackProgress += OnProgress;
        sessions.PlaybackStopped += OnStopped;
        userData.UserDataSaved += OnSaved;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        sessions.PlaybackStart -= OnStart;
        sessions.PlaybackProgress -= OnProgress;
        sessions.PlaybackStopped -= OnStopped;
        userData.UserDataSaved -= OnSaved;
        return Task.CompletedTask;
    }

    private void OnStart(object? sender, PlaybackProgressEventArgs e)
    {
        paused[e.PlaySessionId ?? string.Empty] = false;
        Scrobble(e.Users.Select(u => u.Id), e.Item, "start", Items.Progress(e.Item, e.PlaybackPositionTicks) ?? 0);
    }

    // Jellyfin reports progress every few seconds, so only a change between paused and playing is worth sending.
    private void OnProgress(object? sender, PlaybackProgressEventArgs e)
    {
        var key = e.PlaySessionId ?? string.Empty;
        if (paused.TryGetValue(key, out var was) && was == e.IsPaused)
        {
            return;
        }

        paused[key] = e.IsPaused;
        Scrobble(e.Users.Select(u => u.Id), e.Item, e.IsPaused ? "pause" : "start", Items.Progress(e.Item, e.PlaybackPositionTicks) ?? 0);
    }

    private void OnStopped(object? sender, PlaybackStopEventArgs e)
    {
        paused.TryRemove(e.PlaySessionId ?? string.Empty, out _);
        if (e.PlayedToCompletion)
        {
            Scrobble(e.Users.Select(u => u.Id), e.Item, "stop", 100);
        }
        else if (Items.Progress(e.Item, e.PlaybackPositionTicks) is { } progress)
        {
            Scrobble(e.Users.Select(u => u.Id), e.Item, "pause", progress);
        }
    }

    private void OnSaved(object? sender, UserDataSaveEventArgs e)
    {
        if (e.SaveReason != UserDataSaveReason.TogglePlayed || !e.UserData.Played || Links.For(e.UserId) is not { } link || Items.Ref(e.Item) is not { } item)
        {
            return;
        }

        Run(e.Item, () => tethr.AddHistory(link, [new NewWatch(item, DateTime.UtcNow)], CancellationToken.None));
    }

    private void Scrobble(IEnumerable<Guid> users, BaseItem? item, string action, double progress)
    {
        if (item is null || Items.Ref(item) is not { } target)
        {
            return;
        }

        foreach (var id in users)
        {
            if (Links.For(id) is { } link)
            {
                Run(item, () => tethr.Scrobble(link, new ScrobbleRequest(action, progress, target, Items.Seconds(item)), CancellationToken.None));
            }
        }
    }

    private void Run(BaseItem item, Func<Task> send)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await send().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Couldn't send {Name} to Tethr", item.Name);
            }
        });
    }
}
