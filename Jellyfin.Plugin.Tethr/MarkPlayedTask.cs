using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Tethr;

public class MarkPlayedTask(IUserManager users, ILibraryManager library, IUserDataManager userData, Api.TethrClient tethr, ILogger<MarkPlayedTask> log) : IScheduledTask
{
    private const int PageSize = 100;

    public string Name => "Mark plays from Tethr as played";

    public string Key => "TethrMarkPlayed";

    public string Description => "Marks things each connected person watched elsewhere as played here.";

    public string Category => "Tethr";

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() =>
    [
        new TaskTriggerInfo { Type = TaskTriggerInfoType.IntervalTrigger, IntervalTicks = TimeSpan.FromMinutes(15).Ticks },
    ];

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        if (Plugin.Instance?.Configuration.MarkPlayed != true)
        {
            return;
        }

        foreach (var link in Links.All())
        {
            var user = users.GetUserById(link.UserId);
            if (user is null)
            {
                continue;
            }

            var activity = await tethr.Activity(link, cancellationToken).ConfigureAwait(false);
            if (link.HistorySeenUpTo is { } seen && activity.History <= seen)
            {
                continue;
            }

            var marked = 0;
            DateTime? newest = null;
            for (var page = 1; ; page++)
            {
                var history = await tethr.History(link, page, PageSize, cancellationToken).ConfigureAwait(false);
                var done = history.Items.Count < PageSize;
                foreach (var watch in history.Items)
                {
                    newest ??= watch.WatchedAt;
                    if (link.HistorySeenUpTo is { } upTo && watch.WatchedAt <= upTo)
                    {
                        done = true;
                        break;
                    }

                    // Marking the library's cached copy keeps Jellyfin's own views up to date without a restart.
                    if (Items.Find(library, watch.Item) is not { } found || library.GetItemById(found.Id) is not { } item)
                    {
                        continue;
                    }

                    var data = userData.GetUserData(user, item);
                    if (data is null || data.Played)
                    {
                        continue;
                    }

                    data.Played = true;
                    data.PlayCount = Math.Max(data.PlayCount, 1);
                    data.LastPlayedDate = watch.WatchedAt;
                    userData.SaveUserData(user, item, data, UserDataSaveReason.Import, cancellationToken);
                    marked++;
                }

                if (done)
                {
                    break;
                }
            }

            log.LogInformation("Marked {Count} plays from Tethr as played for {User}", marked, user.Username);
            link.HistorySeenUpTo = newest ?? activity.History;
            Links.Save(link);
        }
    }
}
