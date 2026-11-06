using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Tethr.Api;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.Tethr;

public static class Items
{
    public static ItemRef? Ref(BaseItem item)
    {
        switch (item)
        {
            case Movie movie when IdsOf(movie) is { } ids:
                return new ItemRef("movie", Ids: ids);
            case MediaBrowser.Controller.Entities.TV.Episode { Series: { } series, ParentIndexNumber: { } season, IndexNumber: { } number }
                when IdsOf(series) is { } show:
                return new ItemRef("episode", Show: show, Season: season, Number: number);
            default:
                return null;
        }
    }

    public static double? Progress(BaseItem item, long? positionTicks)
    {
        if (item.RunTimeTicks is not > 0 || positionTicks is null)
        {
            return null;
        }

        return 100.0 * positionTicks.Value / item.RunTimeTicks.Value;
    }

    public static double? Seconds(BaseItem item) => item.RunTimeTicks is > 0 ? item.RunTimeTicks.Value / 1e7 : null;

    // Jellyfin's TMDB metadata gives episodes TVDB and IMDb IDs but not their own TMDB IDs, so episodes are found through their show.
    public static BaseItem? Find(ILibraryManager library, Api.Item watched)
    {
        if (watched.Type == "movie")
        {
            return ByIds(library, BaseItemKind.Movie, watched.Title.Ids);
        }

        if (watched.Episode is null || ByIds(library, BaseItemKind.Series, watched.Title.Ids) is not { } series)
        {
            return null;
        }

        return library.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Episode],
            AncestorIds = [series.Id],
            ParentIndexNumber = watched.Episode.Season,
            IndexNumber = watched.Episode.Number,
            Recursive = true,
        }).FirstOrDefault();
    }

    public static IEnumerable<string> Keys(ItemRef item) =>
        item.Type == "movie" ? IdKeys("movie", item.Ids) : IdKeys("show", item.Show).Select(k => $"{k}:{item.Season}:{item.Number}");

    public static IEnumerable<string> Keys(Api.Item item) =>
        item.Type == "movie" ? IdKeys("movie", item.Title.Ids)
        : item.Episode is null ? []
        : IdKeys("show", item.Title.Ids).Select(k => $"{k}:{item.Episode.Season}:{item.Episode.Number}");

    private static IEnumerable<string> IdKeys(string kind, Ids? ids)
    {
        if (ids?.Tmdb is { } tmdb)
        {
            yield return $"{kind}:tmdb:{tmdb}";
        }

        if (!string.IsNullOrEmpty(ids?.Imdb))
        {
            yield return $"{kind}:imdb:{ids.Imdb}";
        }

        if (ids?.Tvdb is { } tvdb)
        {
            yield return $"{kind}:tvdb:{tvdb}";
        }
    }

    private static BaseItem? ByIds(ILibraryManager library, BaseItemKind kind, Ids ids)
    {
        var wanted = new List<(MetadataProvider, string?)>
        {
            (MetadataProvider.Tmdb, ids.Tmdb?.ToString(CultureInfo.InvariantCulture)),
            (MetadataProvider.Imdb, ids.Imdb),
            (MetadataProvider.Tvdb, ids.Tvdb?.ToString(CultureInfo.InvariantCulture)),
        };
        foreach (var (provider, value) in wanted)
        {
            if (string.IsNullOrEmpty(value))
            {
                continue;
            }

            var found = library.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = [kind],
                HasAnyProviderId = new Dictionary<string, string> { [provider.ToString()] = value },
                Recursive = true,
            }).FirstOrDefault();
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private static Ids? IdsOf(BaseItem item)
    {
        var tmdb = long.TryParse(item.GetProviderId(MetadataProvider.Tmdb), out var t) ? t : (long?)null;
        var tvdb = long.TryParse(item.GetProviderId(MetadataProvider.Tvdb), out var v) ? v : (long?)null;
        var imdb = item.GetProviderId(MetadataProvider.Imdb);
        if (tmdb is null && tvdb is null && string.IsNullOrEmpty(imdb))
        {
            return null;
        }

        return new Ids(tmdb, string.IsNullOrEmpty(imdb) ? null : imdb, tvdb);
    }
}
