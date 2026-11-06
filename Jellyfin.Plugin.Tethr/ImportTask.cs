using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Tethr.Api;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Tethr;

public class ImportTask(IUserManager users, ILibraryManager library, IUserDataManager userData, TethrClient tethr, ILogger<ImportTask> log) : IScheduledTask
{
    public string Name => "Send past plays to Tethr";

    public string Key => "TethrImport";

    public string Description => "Sends what each newly connected person has already watched here to their Tethr profile. It runs by itself after someone connects.";

    public string Category => "Tethr";

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => [];

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var links = Links.All().Where(l => l.Imported is null).ToArray();
        for (var i = 0; i < links.Length; i++)
        {
            var link = links[i];
            var user = users.GetUserById(link.UserId);
            if (user is null)
            {
                continue;
            }

            var played = library.GetItemList(new InternalItemsQuery(user)
            {
                IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Episode],
                IsPlayed = true,
                Recursive = true,
            });
            var logged = await Logged(link, cancellationToken).ConfigureAwait(false);
            var watches = new List<NewWatch>();
            foreach (var item in played)
            {
                if (userData.GetUserData(user, item)?.LastPlayedDate is not { } at || Items.Ref(item) is not { } target)
                {
                    continue;
                }

                var already = Items.Keys(target).Any(k => logged.TryGetValue(k, out var times) && times.Any(t => Math.Abs((t - at).TotalMinutes) <= 10));
                if (!already)
                {
                    watches.Add(new NewWatch(target, at));
                }
            }

            foreach (var batch in watches.Chunk(100))
            {
                await tethr.AddHistory(link, batch, cancellationToken).ConfigureAwait(false);
            }

            log.LogInformation("Past plays for {User}: {Sent} sent to Tethr, {Skipped} skipped", user.Username, watches.Count, played.Count - watches.Count);
            link.Imported = DateTime.UtcNow;
            Links.Save(link);
            progress.Report(100.0 * (i + 1) / links.Length);
        }
    }

    // Jellyfin keeps one last-played time per item, a little off from when an app or webhook logged the same play, so anything already within ten minutes counts as there.
    private async Task<Dictionary<string, List<DateTime>>> Logged(Configuration.UserLink link, CancellationToken cancellationToken)
    {
        var logged = new Dictionary<string, List<DateTime>>();
        for (var page = 1; ; page++)
        {
            var history = await tethr.History(link, page, 100, cancellationToken).ConfigureAwait(false);
            foreach (var watch in history.Items)
            {
                foreach (var key in Items.Keys(watch.Item))
                {
                    if (!logged.TryGetValue(key, out var times))
                    {
                        logged[key] = times = [];
                    }

                    times.Add(watch.WatchedAt);
                }
            }

            if (history.Items.Count < 100)
            {
                return logged;
            }
        }
    }
}
